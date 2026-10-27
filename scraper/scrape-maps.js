const puppeteer = require("puppeteer-core");
const fs = require("fs");

function getBrowserPath() {
  if (process.env.PUPPETEER_EXECUTABLE_PATH && fs.existsSync(process.env.PUPPETEER_EXECUTABLE_PATH)) {
    return process.env.PUPPETEER_EXECUTABLE_PATH;
  }
  const candidates = [
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
    "/usr/bin/google-chrome-stable",
    "/usr/bin/google-chrome",
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
  ];
  for (const p of candidates) {
    if (fs.existsSync(p)) return p;
  }
  throw new Error("Neither Chromium nor Google Chrome / Edge was found.");
}


async function scrapeGoogleMaps(city, category, limit = 15) {
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
      "--window-size=1280,800",
    ],
  });

  let places = [];
  try {
    const page = await browser.newPage();
  await page.setUserAgent(
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
  );
  await page.setViewport({ width: 1280, height: 800 });

  const query = `${category} in ${city}`;
  const url = `https://www.google.com/maps/search/${encodeURIComponent(query)}`;

  try {
    await page.goto(url, { waitUntil: "networkidle2", timeout: 30000 });
  } catch (e) {
    // ignore networkidle timeout if DOM loaded
  }

  // Wait for place cards to render
  try {
    await page.waitForSelector("div.Nv2PK, div[role='article'], a.hfpxzc", {
      timeout: 10000,
    });
  } catch (e) {
    // No results or direct place
  }

  // Auto-scroll feed panel to load more results if needed
  try {
    await page.evaluate(async () => {
      const feed = document.querySelector('div[role="feed"]');
      if (feed) {
        for (let i = 0; i < 5; i++) {
          feed.scrollTop = feed.scrollHeight;
          await new Promise((r) => setTimeout(r, 900));
        }
      }
    });
  } catch (e) {}

  // Extract places
  const places = await page.evaluate(
    (targetCity, targetCategory) => {
      const results = [];
      const seen = new Set();
      const cards = document.querySelectorAll(
        "div.Nv2PK, div[role='article']"
      );

      cards.forEach((card) => {
        const linkEl = card.querySelector("a.hfpxzc");
        const nameEl = card.querySelector(
          "div.qBF1Pd, div.fontHeadlineSmall"
        );
        let name = nameEl
          ? nameEl.innerText.trim()
          : linkEl
          ? linkEl.getAttribute("aria-label")
          : "";

        if (!name || seen.has(name.toLowerCase())) return;
        seen.add(name.toLowerCase());

        const fullText = card.innerText || "";

        // Check website button / link
        const siteLink = card.querySelector(
          'a[data-value="Website"], a[aria-label*="website" i], a[data-item-id="authority"]'
        );
        let website = siteLink ? siteLink.href : null;
        if (website && (website.includes("google.com") || website.includes("goo.gl"))) {
          website = null;
        }
        const hasWebsite = Boolean(website);

        // Extract Phone Number (Indian mobile 10-digit or landline with STD code)
        let phone = null;
        const phoneMatch = fullText.match(
          /(?:\+?91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}|0\d{2,4}[\s-]?\d{6,8}/
        );
        if (phoneMatch) {
          phone = phoneMatch[0].replace(/[\s-]/g, "");
          if (phone.length === 10) phone = "+91" + phone;
          else if (phone.startsWith("91") && phone.length === 12) phone = "+" + phone;
        }

        // Extract address / subtitle lines
        const lines = fullText
          .split("\n")
          .map((l) => l.trim())
          .filter((l) => l.length > 0);

        // Address usually is the line containing road, area, or after category
        let address = null;
        for (const line of lines) {
          if (
            line !== name &&
            !line.startsWith("★") &&
            !line.includes("reviews") &&
            !line.includes("Open") &&
            !line.includes("Closed") &&
            !line.includes("Closes") &&
            !line.includes("Opens") &&
            !line.includes("AED") &&
            !line.match(/^[0-9.]+\s*\(/) && // rating like 4.5 (20)
            line.length > 4
          ) {
            if (
              line.toLowerCase().includes(targetCity.toLowerCase()) ||
              line.includes("Road") ||
              line.includes("Colony") ||
              line.includes("Nagar") ||
              line.includes("Marg") ||
              line.includes("Street") ||
              line.includes("Shop") ||
              line.includes("Opp") ||
              line.includes("Near")
            ) {
              address = line;
              break;
            }
          }
        }

        if (!address) {
          address = `${targetCity}, Gujarat, India`;
        }

        results.push({
          name,
          phone,
          address,
          hasWebsite,
          website,
          city: targetCity,
          category: targetCategory,
        });
      });

      return results;
    },
    city,
    category
  );
  } finally {
    if (browser) await browser.close().catch(() => {});
  }
  // Prioritize businesses without websites so they appear first
  places.sort((a, b) => (a.hasWebsite === b.hasWebsite ? 0 : a.hasWebsite ? 1 : -1));
  return places.slice(0, limit);
}

// CLI entry point
const [cityArg, catArg, limitArg] = process.argv.slice(2);
const city = cityArg || "Jamnagar";
const category = catArg || "Dental Clinic";
const limit = parseInt(limitArg, 10) || 15;

scrapeGoogleMaps(city, category, limit)
  .then((results) => {
    console.log(JSON.stringify(results, null, 2));
  })
  .catch((err) => {
    console.error("Scraping error:", err);
    process.exit(1);
  });
