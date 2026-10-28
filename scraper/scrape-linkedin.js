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


async function scrapeLinkedIn(query, limit = 15) {
  let browser = null;
  const items = [];
  const seenIds = new Set();

  try {
    const puppeteer = await getPuppeteer();
    const browserPath = getBrowserPath();
    browser = await puppeteer.launch({
      headless: true,
      executablePath: browserPath,
      args: [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--disable-accelerated-2d-canvas",
        "--disable-blink-features=AutomationControlled",
        "--lang=en-US,en",
        "--window-size=1280,800",
        ...getProxyArgs(),
      ],
    });

    const page = await browser.newPage();
    await attachProxyAuth(page);
    await page.setUserAgent(
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
    );
    await page.setViewport({ width: 1280, height: 800 });

    const searchUrl = `https://www.linkedin.com/jobs/search?keywords=${encodeURIComponent(query)}&location=Worldwide`;
    
    try {
      await page.goto(searchUrl, { waitUntil: "domcontentloaded", timeout: 25000 });
      await new Promise((r) => setTimeout(r, 4000));
    } catch (e) {
      // Continue if DOM loaded
    }

    const jobData = await page.evaluate((targetLimit) => {
      const results = [];
      const cards = document.querySelectorAll(
        "div.base-card, li, .jobs-search__results-list > li, div.job-search-card"
      );

      for (const card of cards) {
        if (results.length >= targetLimit) break;

        const titleEl = card.querySelector(
          "h3.base-search-card__title, .job-card-list__title, h3"
        );
        const companyEl = card.querySelector(
          "h4.base-search-card__subtitle, .job-card-container__company-name, h4, a[data-tracking-control-name*='company']"
        );
        const linkEl = card.querySelector(
          "a.base-card__full-link, a.job-card-list__title, a[href*='/jobs/view/']"
        );
        const locEl = card.querySelector(
          "span.job-search-card__location, .job-search-card__location"
        );
        const timeEl = card.querySelector(
          "time.job-search-card__listdate, time"
        );

        if (titleEl && linkEl) {
          const rawTitle = titleEl.innerText ? titleEl.innerText.trim() : "";
          const href = linkEl.href ? linkEl.href.split("?")[0] : "";
          if (!rawTitle || !href || rawTitle.toLowerCase().includes("see all")) continue;

          const company = companyEl && companyEl.innerText ? companyEl.innerText.trim() : "Hiring Company";
          const location = locEl && locEl.innerText ? locEl.innerText.trim() : "Worldwide / Remote";
          const posted = timeEl && timeEl.innerText ? timeEl.innerText.trim() : (timeEl ? timeEl.getAttribute("datetime") : "Recently");

          results.push({
            title: rawTitle,
            company,
            location,
            url: href,
            posted: posted || "Recently",
          });
        }
      }
      return results;
    }, limit * 2);

    for (const job of jobData) {
      if (items.length >= limit) break;

      // Extract LinkedIn Job ID
      const idMatch = job.url.match(/([0-9]{8,15})/);
      const id = idMatch ? idMatch[1] : `li_${items.length + 1}`;
      if (seenIds.has(id)) continue;
      seenIds.add(id);

      items.push({
        id,
        title: job.title,
        company: job.company,
        location: job.location,
        url: job.url,
        posted: job.posted,
        description: `Client / Organization "${job.company}" hiring for "${job.title}" located in ${job.location}. View job requirements and apply directly on LinkedIn.`,
        budget: "See LinkedIn Posting",
        jobType: "Full-time / Contract",
      });
    }
  } catch (err) {
    // Return collected
  } finally {
    if (browser) {
      await browser.close();
    }
  }

  return items;
}

// CLI Execution
const args = process.argv.slice(2);
const query = args[0] || "website developer";
const limit = parseInt(args[1], 10) || 15;

scrapeLinkedIn(query, limit)
  .then((results) => {
    console.log(JSON.stringify(results));
  })
  .catch((err) => {
    console.error(err);
    console.log("[]");
  });
