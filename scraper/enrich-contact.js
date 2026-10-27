const puppeteer = require("puppeteer-core");
const fs = require("fs");

function getBrowserPath() {
  const candidates = [
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
  ];
  for (const p of candidates) {
    if (fs.existsSync(p)) return p;
  }
  throw new Error("Neither Google Chrome nor Microsoft Edge was found.");
}

const JUNK_EMAIL_DOMAINS = ["sentry.io", "wixpress.com", "example.com", "domain.com", "email.com"];
const JUNK_EXTENSIONS = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".css", ".js"];

function cleanEmail(email) {
  if (!email) return null;
  const lower = email.trim().toLowerCase();
  if (JUNK_DOMAINS_MATCH(lower)) return null;
  for (const ext of JUNK_EXTENSIONS) {
    if (lower.endsWith(ext)) return null;
  }
  return lower;
}

function JUNK_DOMAINS_MATCH(email) {
  return JUNK_EMAIL_DOMAINS.some((d) => email.includes(d));
}

async function scrapeEmailsFromWebsite(url) {
  if (!url || !url.startsWith("http")) return null;
  try {
    const res = await fetch(url, {
      signal: AbortSignal.timeout(5000),
      headers: {
        "User-Agent":
          "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
      },
    });
    if (!res.ok) return null;
    const html = await res.text();

    const matches = html.match(/[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}/g) || [];
    const valid = matches
      .map(cleanEmail)
      .filter(Boolean)
      .filter((e) => !e.includes("schema.org") && !e.includes("rating") && !e.includes("node_modules"));

    if (valid.length > 0) {
      // Prioritize admin, info, contact, support
      const priority = valid.find(
        (e) =>
          e.startsWith("info@") ||
          e.startsWith("admin@") ||
          e.startsWith("contact@") ||
          e.startsWith("support@") ||
          e.startsWith("hello@") ||
          e.startsWith("care@")
      );
      return priority || valid[0];
    }
  } catch (err) {
    // Ignore timeout / network failure
  }
  return null;
}

function cleanNameForSearch(name) {
  if (!name) return "";
  const cleaned = name
    .replace(/\b(private\s+limited|pvt\.?\s*ltd\.?|limited|ltd\.?|llp|\(opc\))\b/gi, "")
    .replace(/\s+/g, " ")
    .trim();
  return cleaned.length >= 3 ? cleaned : name;
}

async function enrichSingle(page, name, city) {
  const cleanName = cleanNameForSearch(name);
  const query = `${cleanName} ${city || ""}`.trim();
  const mapsUrl = `https://www.google.com/maps/search/${encodeURIComponent(query)}`;

  try {
    await page.goto(mapsUrl, { waitUntil: "domcontentloaded", timeout: 20000 });
  } catch (e) {
    // Timeout or partial load
  }

  // Wait a moment for dynamic cards to populate
  await new Promise((r) => setTimeout(r, 2800));

  const data = await page.evaluate(() => {
    // 1. Phone number
    let phone = null;
    const phoneBtn = document.querySelector(
      'button[data-item-id*="phone:"], button[aria-label*="Phone:"], [data-tooltip="Copy phone number"]'
    );
    if (phoneBtn) {
      const raw =
        phoneBtn.getAttribute("data-item-id") ||
        phoneBtn.getAttribute("aria-label") ||
        phoneBtn.innerText ||
        "";
      const clean = raw.replace(/[^\d+]/g, "");
      if (clean.length >= 10) phone = clean;
    }
    if (!phone) {
      const bodyText = document.body.innerText || "";
      const matches = bodyText.match(/(?:(?:\+91|0)[\s-]?)?[6789]\d{4}[\s-]?\d{5}|0[1-9]\d{1,4}[\s-]?\d{5,8}/g);
      if (matches && matches.length > 0) {
        phone = matches[0].replace(/[^\d+]/g, "");
      }
    }

    // 2. Website URL
    let website = null;
    const webBtn = document.querySelector(
      'a[data-item-id="authority"], a[aria-label*="website" i], a[data-value="Website"]'
    );
    if (webBtn && webBtn.href && !webBtn.href.includes("google.com")) {
      website = webBtn.href;
    }

    // 3. Address
    let address = null;
    const addrBtn = document.querySelector('button[data-item-id*="address"], div[aria-label*="Address:"]');
    if (addrBtn) {
      address = addrBtn.innerText
        .replace(/^Address:\s*/i, "")
        .replace(/[^\x20-\x7E\u0900-\u097F\s,.-]/g, "")
        .trim();
    }

    // 4. Any direct email displayed in maps
    let email = null;
    const mailto = document.querySelector('a[href^="mailto:"]');
    if (mailto) {
      email = mailto.href.replace(/^mailto:/i, "").split("?")[0].trim();
    }

    return { phone, website, address, email };
  });

  let foundEmail = data.email;
  if (!foundEmail && data.website) {
    foundEmail = await scrapeEmailsFromWebsite(data.website);
  }

  return {
    name,
    city,
    phone: data.phone,
    website: data.website,
    address: data.address,
    email: foundEmail,
    enriched: Boolean(data.phone || data.website || data.address || foundEmail),
  };
}

async function enrichBatch(items) {
  if (!items || items.length === 0) return [];

  const browserPath = getBrowserPath();
  const browser = await puppeteer.launch({
    headless: "new",
    executablePath: browserPath,
    args: [
      "--no-sandbox",
      "--disable-setuid-sandbox",
      "--disable-dev-shm-usage",
      "--disable-gpu",
      "--lang=en-US,en",
    ],
  });

  const results = [];
  try {
    const concurrency = Math.min(items.length, 3);
    const queue = items.map((item, idx) => ({ ...item, _idx: idx }));
    const rawResults = new Array(items.length);

    const workers = Array.from({ length: concurrency }, async () => {
      const page = await browser.newPage();
      await page.setUserAgent(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
      );

      while (queue.length > 0) {
        const item = queue.shift();
        if (!item) break;
        try {
          const enriched = await enrichSingle(page, item.name, item.city);
          rawResults[item._idx] = enriched;
        } catch (err) {
          rawResults[item._idx] = {
            name: item.name,
            city: item.city,
            phone: null,
            website: null,
            address: null,
            email: null,
            enriched: false,
          };
        }
      }

      try {
        await page.close();
      } catch (e) {}
    });

    await Promise.all(workers);
    for (const r of rawResults) {
      if (r) results.push(r);
    }
  } finally {
    await browser.close();
  }

  return results;
}

// CLI handler
async function main() {
  const args = process.argv.slice(2);
  let items = [];

  if (args.includes("--file")) {
    const filePathIndex = args.indexOf("--file") + 1;
    const filePath = args[filePathIndex];
    if (fs.existsSync(filePath)) {
      let raw = fs.readFileSync(filePath, "utf-8");
      if (raw.charCodeAt(0) === 0xFEFF) raw = raw.slice(1);
      items = JSON.parse(raw);
    }
  } else if (args.includes("--json")) {
    const jsonIndex = args.indexOf("--json") + 1;
    let jsonStr = args[jsonIndex];
    if (jsonStr.charCodeAt(0) === 0xFEFF) jsonStr = jsonStr.slice(1);
    items = JSON.parse(jsonStr);
  } else if (args.length >= 1) {
    const name = args[0];
    const city = args[1] || "";
    items = [{ name, city }];
  } else {
    // Try reading from stdin
    let stdinData = fs.readFileSync(0, "utf-8");
    if (stdinData.charCodeAt(0) === 0xFEFF) stdinData = stdinData.slice(1);
    if (stdinData.trim()) {
      items = JSON.parse(stdinData);
    }
  }

  const results = await enrichBatch(items);
  process.stdout.write(JSON.stringify(results, null, 2));
}

if (require.main === module) {
  main().catch((err) => {
    process.stderr.write(JSON.stringify({ error: err.message }));
    process.exit(1);
  });
}

module.exports = { enrichBatch, enrichSingle };
