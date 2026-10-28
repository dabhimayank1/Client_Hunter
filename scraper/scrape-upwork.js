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


async function scrapeUpwork(query, limit = 15) {
  let browser = null;
  const items = [];

  try {
    const browserPath = getBrowserPath();
    browser = await puppeteer.launch({
      headless: true,
      executablePath: browserPath,
      dumpio: true,
      args: [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--disable-accelerated-2d-canvas",
        "--disable-blink-features=AutomationControlled",
        "--lang=en-US,en",
        "--window-size=1280,800",
      ],
    });

    const page = await browser.newPage();
    await page.setUserAgent(
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
    );
    await page.setViewport({ width: 1280, height: 800 });

    const searchUrl = `https://www.upwork.com/nx/search/jobs/?q=${encodeURIComponent(query)}&sort=recency`;
    
    try {
      await page.goto(searchUrl, { waitUntil: "domcontentloaded", timeout: 25000 });
      await new Promise((r) => setTimeout(r, 3500));
    } catch (e) {
      // Continue if DOM is loaded
    }

    let jobData = await page.evaluate((targetLimit) => {
      const results = [];
      const seenUrls = new Set();

      // Look for job tiles
      const cards = document.querySelectorAll(
        'article[data-test="JobTile"], div[data-test="job-tile-list"] > section, article, div.up-card-section'
      );

      for (const card of cards) {
        if (results.length >= targetLimit) break;

        const a = card.querySelector('a[href*="/jobs/"], a[href*="/freelance-jobs/apply/"]');
        if (!a) continue;

        const href = a.href;
        if (seenUrls.has(href)) continue;
        seenUrls.add(href);

        let title = a.innerText.trim();
        if (!title || title.toLowerCase() === "see more") continue;

        // Clean up title (remove highlights or html artifacts if any)
        title = title.replace(/<[^>]+>/g, "").replace(/\s+/g, " ").trim();

        // Clean up URL parameters and any highlight slugs
        let cleanUrl = href.replace(/\/\?referrer_url_path=.*$/, "");
        cleanUrl = cleanUrl.replace(/span-class-highlight-([A-Za-z0-9]+)-span/g, "$1");

        // Extract raw card text
        const cardText = card.innerText || "";
        const lines = cardText.split("\n").map((l) => l.trim()).filter(Boolean);

        // Extract budget / job type / posted time
        let budget = null;
        let jobType = "Contract";
        let posted = null;
        let description = "";

        for (const line of lines) {
          if (!posted && (line.includes("ago") || line.includes("yesterday") || line.includes("today"))) {
            posted = line;
          }
          if (!budget && (line.startsWith("$") || line.includes("Est. Budget") || line.includes("Fixed-price") || line.includes("Hourly"))) {
            budget = line;
          }
          if (line.toLowerCase().includes("hourly")) jobType = "Hourly";
          if (line.toLowerCase().includes("fixed")) jobType = "Fixed Price";
        }

        // Extract description snippet (usually longer line)
        for (const line of lines) {
          if (line.length > 50 && line !== title && !line.includes("Hourly") && !line.includes("Posted")) {
            description = line;
            break;
          }
        }

        if (!description && lines.length > 2) {
          description = lines[lines.length - 1];
        }

        // Job ID extraction from href
        let idMatch = href.match(/_~([0-9a-zA-Z]+)/);
        const jobId = idMatch ? idMatch[1] : `upwork_${results.length + 1}`;

        results.push({
          id: jobId,
          title,
          url: cleanUrl,
          description: description.slice(0, 500),
          budget: budget || "Not specified",
          jobType,
          posted: posted || "Recently",
        });
      }

      return results;
    }, limit);

    // If search didn't return cards, try fallback category page
    if (jobData.length === 0) {
      const slug = query.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
      const catUrl = `https://www.upwork.com/freelance-jobs/${slug}/`;
      try {
        await page.goto(catUrl, { waitUntil: "domcontentloaded", timeout: 20000 });
        await new Promise((r) => setTimeout(r, 3500));

        jobData = await page.evaluate((targetLimit) => {
          const results = [];
          const seen = new Set();
          const links = document.querySelectorAll('a[href*="/freelance-jobs/apply/"], a[href*="/jobs/"]');

          for (const a of links) {
            if (results.length >= targetLimit) break;
            const href = a.href;
            if (seen.has(href)) continue;
            seen.add(href);

            const title = a.innerText.trim();
            if (!title || title.toLowerCase() === "see more") continue;

            let container = a.closest("article, section, div.up-card-section") || a.parentElement;
            for (let i = 0; i < 4 && container && container.innerText.length < 50; i++) {
              container = container.parentElement;
            }

            const text = container ? container.innerText : "";
            const lines = text.split("\n").map((l) => l.trim()).filter(Boolean);

            let budget = null;
            let desc = "";
            for (const l of lines) {
              if (l.startsWith("$")) budget = l;
              if (l.length > 40 && l !== title) desc = l;
            }

            let idMatch = href.match(/_~([0-9a-zA-Z]+)/);
            results.push({
              id: idMatch ? idMatch[1] : `upwork_${results.length + 1}`,
              title,
              url: href,
              description: desc.slice(0, 500),
              budget: budget || "See posting",
              jobType: text.includes("Hourly") ? "Hourly" : "Fixed Price",
              posted: "Recently",
            });
          }
          return results;
        }, limit);
      } catch (e) {
        // Fallback failed
      }
    }

    items.push(...jobData);
  } catch (err) {
    // Return whatever was collected
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

scrapeUpwork(query, limit)
  .then((results) => {
    console.log(JSON.stringify(results));
  })
  .catch((err) => {
    console.error(err);
    console.log("[]");
  });
