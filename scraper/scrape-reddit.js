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


function extractBudget(text) {
  if (!text) return "Flexible / Inquire";
  const budgetMatch =
    text.match(/\$[\d,]+(?:\.\d{2})?(?:\s*(?:-|to)\s*\$[\d,]+)?(?:\s*\/(?:hr|hour|mo|month|fixed|project))?/i) ||
    text.match(/(?:budget|pay|rate|fixed|reward)\s*[:=-]?\s*(\$[\d,]+|\d+\s*(?:usd|eur|gbp))/i);
  if (budgetMatch) return budgetMatch[0].trim();
  if (/hourly/i.test(text)) return "Hourly Rate";
  if (/fixed/i.test(text)) return "Fixed Budget";
  return "Flexible / Open";
}

function isRealWebsiteProject(title, description = "") {
  const text = (title + " " + description).toLowerCase();

  // 1. Exclude corporate employee jobs, internships, sales, marketing, and non-tech gigs
  const isJunkOrCorporateJob =
    /(?:intern|sales|cold call|content creator|survey|voice actor|audio|voiceover|tutor|lesson|writer|article writer|virtual assistant|data entry|assistant|test ai|get paid to|crypto shill|promoter|full-time job|full time employee|w2|salary\s*:\s*\$[0-9]+k)/i.test(
      text
    );
  if (isJunkOrCorporateJob) return false;

  // 2. Exclude freelancers advertising themselves [FOR HIRE]
  if (/\[for\s*hire\]/i.test(title)) return false;

  // 3. Must be an actual client project asking to build, design, or develop a website, app, portal, or SEO/marketing work
  const isWebsiteOrDevProject =
    /(?:website|web\s*app|web\s*dev|developer|full-?stack|front-?end|back-?end|landing\s*page|shopify|wordpress|woocommerce|build a site|create a site|redesign|web platform|software development|mvp|saas|3d\s*model|seo|search\s*engine|ranking|digital\s*marketing|local\s*seo|backlink|link\s*building|gmb)/i.test(
      text
    );

  return isWebsiteOrDevProject;
}

async function scrapeReddit(query, limit = 15) {
  let browser = null;
  const items = [];
  const seenIds = new Set();

  try {
    const browserPath = getBrowserPath();
    browser = await puppeteer.launch({
      headless: true,
      executablePath: browserPath,
      args: [
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--no-zygote",
        "--single-process",
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

    const isSeoQuery = /seo|ranking|marketing|backlink/i.test(query);
    const fallbackTerm = isSeoQuery ? "seo" : "developer";

    const searchUrls = [
      `https://www.reddit.com/r/forhire/search/?q=flair%3Ahiring+${encodeURIComponent(query)}&restrict_sr=1&sort=new`,
      `https://www.reddit.com/r/freelance_forhire/search/?q=${encodeURIComponent(query)}&restrict_sr=1&sort=new`,
      `https://www.reddit.com/r/forhire/search/?q=flair%3Ahiring+${encodeURIComponent(fallbackTerm)}&restrict_sr=1&sort=new`
    ];

    for (const searchUrl of searchUrls) {
      if (items.length >= limit) break;

      try {
        await page.goto(searchUrl, { waitUntil: "domcontentloaded", timeout: 25000 });
        await new Promise((r) => setTimeout(r, 3500));
      } catch (e) {
        // Continue if DOM loaded
      }

      const rawPosts = await page.evaluate(() => {
        const results = [];
        const postElements = document.querySelectorAll(
          'shreddit-post, a[data-testid="post-title"], a[href*="/comments/"]'
        );

        for (const el of postElements) {
          if (el.tagName.toLowerCase() === "shreddit-post") {
            const title = el.getAttribute("post-title") || el.querySelector('[slot="title"]')?.innerText;
            const permalink = el.getAttribute("permalink");
            const author = el.getAttribute("author");
            const created = el.getAttribute("created-timestamp");
            if (title && permalink) {
              results.push({
                title: title.trim(),
                author: author || "Reddit Client",
                url: permalink.startsWith("http") ? permalink : `https://www.reddit.com${permalink}`,
                posted: created || "Recently",
              });
            }
          } else {
            const text = el.innerText?.trim();
            const href = el.href;
            if (text && text.length > 5 && href && href.includes("/comments/")) {
              results.push({
                title: text,
                author: "Reddit Client",
                url: href,
                posted: "Recently",
              });
            }
          }
        }
        return results;
      });

      for (const p of rawPosts) {
        if (items.length >= limit) break;

        // Apply strict filter: only genuine client website/software project requests
        if (!isRealWebsiteProject(p.title)) continue;

        const idMatch = p.url.match(/comments\/([a-zA-Z0-9]+)/);
        const id = idMatch ? idMatch[1] : `reddit_${items.length + 1}`;
        if (seenIds.has(id)) continue;
        seenIds.add(id);

        // Clean title for clarity (remove [Hiring] tag)
        const cleanTitle = p.title.replace(/^\[(?:hiring|urgent|paid)\]\s*/i, "").trim();

        items.push({
          id,
          title: cleanTitle,
          url: p.url,
          author: p.author,
          budget: extractBudget(p.title),
          description: `Client Project Brief: "${cleanTitle}". Client is seeking a developer to build this project. Reach out via direct message on Reddit to discuss requirements and proposal.`,
          posted: p.posted,
          subreddit: searchUrl.includes("freelance_forhire") ? "r/freelance_forhire" : "r/forhire",
        });
      }
    }
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
const query = args[0] || "website";
const limit = parseInt(args[1], 10) || 15;

scrapeReddit(query, limit)
  .then((results) => {
    console.log(JSON.stringify(results));
  })
  .catch((err) => {
    console.error(err);
    console.log("[]");
  });
