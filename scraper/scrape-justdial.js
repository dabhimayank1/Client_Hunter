const fs = require("fs");
const { getProxyArgs, attachProxyAuth } = require("./proxy-helper");

async function getPuppeteer() {
  const mod = await import("puppeteer-core");
  return mod.default || mod;
}

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


function cleanPhone(raw) {
  if (!raw) return null;
  // If string contains _ (like 8488944391_vndtlpg), take before underscore
  let p = raw.split("_")[0].trim();
  p = p.replace(/[^\d+]/g, "");
  if (p.startsWith("0") && p.length === 11) {
    p = p.substring(1);
  }
  if (!p.startsWith("+91") && p.length === 10) {
    p = "+91" + p;
  }
  return p.length >= 10 ? p : null;
}

async function scrapeJustdial(city = "Ahmedabad", category = "Dental-Clinics", limit = 15) {
  const puppeteer = await getPuppeteer();
  const browserPath = getBrowserPath();
  const browser = await puppeteer.launch({
    headless: true,
    executablePath: browserPath,
    dumpio: true,
    args: [
      "--no-sandbox",
      "--disable-setuid-sandbox",
      "--disable-dev-shm-usage",
      "--disable-gpu",
      "--disable-accelerated-2d-canvas",
      "--lang=en-US,en",
      ...getProxyArgs(),
    ],
  });

  const cleanCity = city.trim();
  const cleanCat = category.trim().replace(/\s+/g, "-");
  const targetUrl = `https://www.justdial.com/${encodeURIComponent(cleanCity)}/${encodeURIComponent(cleanCat)}`;

  let capturedApiResults = [];
  let domResults = [];

  try {
    const page = await browser.newPage();
    await attachProxyAuth(page);
    await page.emulate({
      viewport: { width: 390, height: 844, isMobile: true, hasTouch: true },
      userAgent:
        "Mozilla/5.0 (iPhone; CPU iPhone OS 16_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.5 Mobile/15E148 Safari/604.1",
    });

    page.on("response", async (res) => {
    const u = res.url();
    if (u.includes("searchziva.php")) {
      try {
        const json = await res.json();
        if (json && json.main && Array.isArray(json.main.data)) {
          const colMap = json.main.columns || {};
          const nameIdx = colMap.name !== undefined ? colMap.name : 1;
          const docIdIdx = colMap.docid !== undefined ? colMap.docid : 0;
          const addrIdx = colMap.NewAddress !== undefined ? colMap.NewAddress : 3;
          const areaIdx = colMap.area !== undefined ? colMap.area : 12;
          const phoneIdx = colMap.VNumber !== undefined ? colMap.VNumber : 15;
          const ratingIdx = colMap.compRating !== undefined ? colMap.compRating : 7;
          const reviewsIdx = colMap.totalReviews !== undefined ? colMap.totalReviews : 16;
          const cityIdx = colMap.city !== undefined ? colMap.city : 18;
          const webUrlIdx = colMap.web_url !== undefined ? colMap.web_url : 69;

          for (const item of json.main.data) {
            const name = item[nameIdx];
            if (!name || typeof name !== "string" || name.trim().length < 2) continue;

            const docId = item[docIdIdx] || `jd_${Math.random().toString(36).substring(2, 9)}`;
            const rawAddr = (item[addrIdx] || "").toString().trim();
            const rawArea = (item[areaIdx] || "").toString().trim();
            const fullAddress = [rawAddr, rawArea].filter(Boolean).join(", ") || `${cleanCity}, India`;

            let phone = null;
            const phoneObj = item[phoneIdx];
            if (phoneObj && typeof phoneObj === "object") {
              if (Array.isArray(phoneObj.list) && phoneObj.list.length > 0) {
                phone = cleanPhone(phoneObj.list[0]);
              } else if (phoneObj.vnumber) {
                phone = cleanPhone(phoneObj.vnumber);
              }
            } else if (typeof phoneObj === "string") {
              phone = cleanPhone(phoneObj);
            }

            const rating = item[ratingIdx] || null;
            const reviews = item[reviewsIdx] || null;
            let officialWeb = null;
            if (typeof item[webUrlIdx] === "string" && item[webUrlIdx].startsWith("http") && !item[webUrlIdx].includes("justdial.com")) {
              officialWeb = item[webUrlIdx];
            }

            capturedApiResults.push({
              id: docId,
              name: name.trim(),
              city: (item[cityIdx] || cleanCity).toString().trim(),
              phone: phone,
              address: fullAddress,
              category: cleanCat.replace(/-/g, " "),
              rating: rating ? String(rating) : null,
              reviews: reviews ? String(reviews) : null,
              website: officialWeb,
              sourceUrl: targetUrl,
            });
          }
        }
      } catch (e) {
        // Ignore parse error on non-json response
      }
    }
  });

  try {
    await page.goto(targetUrl, {
      waitUntil: "networkidle2",
      timeout: 30000,
    });
  } catch (e) {
    // Timeout or partial load, check if we have data
  }

  // Scroll down to load more results if capturedApiResults is less than limit
  try {
    for (let i = 0; i < 4; i++) {
      if (capturedApiResults.length >= limit) break;
      await page.evaluate(() => window.scrollBy(0, window.innerHeight * 2));
      await new Promise((r) => setTimeout(r, 1200));
    }
  } catch (e) {}

  // If API didn't provide enough results, extract from DOM
  if (capturedApiResults.length < limit) {
    try {
      domResults = await page.evaluate((targetCity, catName, pageUrl) => {
        const list = [];
        const cards = document.querySelectorAll(
          'div[class*="resultbox"], div[class*="card"], li[class*="cntr"], div[class*="jsx-"]'
        );

        for (const card of cards) {
          const titleEl = card.querySelector(
            'h2, h3, a[title], div[class*="title"], div[class*="font20"], div[class*="font18"]'
          );
          if (!titleEl) continue;
          const name = titleEl.innerText.trim();
          if (
            !name ||
            name.length < 3 ||
            name.includes("Top ") ||
            name.includes("Best ") ||
            name.includes("Results in")
          )
            continue;

          const phoneAnchor = card.querySelector('a[href^="tel:"]');
          let phone = phoneAnchor ? phoneAnchor.getAttribute("href").replace("tel:", "").trim() : null;

          const addrEl = card.querySelector(
            'div[class*="address"], p[class*="address"], span[class*="address"], div[class*="area"]'
          );
          const address = addrEl ? addrEl.innerText.trim() : targetCity + ", India";

          if (!list.some((x) => x.name.toLowerCase() === name.toLowerCase())) {
            list.push({
              id: "jd_" + Math.random().toString(36).substring(2, 9),
              name,
              city: targetCity,
              phone,
              address,
              category: catName,
              rating: null,
              reviews: null,
              website: null,
              sourceUrl: pageUrl,
            });
          }
        }
        return list;
      }, cleanCity, cleanCat.replace(/-/g, " "), targetUrl);
    } catch (e) {}
  }
  } finally {
    if (browser) await browser.close().catch(() => {});
  }

  // Merge and deduplicate
  const seen = new Set();
  const merged = [];

  for (const item of [...capturedApiResults, ...domResults]) {
    const key = item.name.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    merged.push(item);
    if (merged.length >= limit) break;
  }

  return merged;
}

// CLI Execution
if (require.main === module) {
  const city = process.argv[2] || "Ahmedabad";
  const category = process.argv[3] || "Dental Clinics";
  const limit = parseInt(process.argv[4] || "15", 10);

  scrapeJustdial(city, category, limit)
    .then((results) => {
      process.stdout.write(JSON.stringify(results, null, 2));
      process.exit(0);
    })
    .catch((err) => {
      process.stderr.write(JSON.stringify({ error: err.message }));
      process.exit(1);
    });
}

module.exports = { scrapeJustdial };
