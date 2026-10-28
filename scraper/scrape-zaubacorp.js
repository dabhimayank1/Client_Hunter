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


function getNicCode(category) {
  if (!category) return null;
  const c = category.toLowerCase();
  if (c.includes("soft") || c.includes("web") || c.includes("it") || c.includes("comput") || c.includes("tech") || c.includes("app")) {
    return "nic-72"; // Computer & related activities
  }
  if (c.includes("consult") || c.includes("market") || c.includes("advertis") || c.includes("design") || c.includes("media")) {
    return "nic-74"; // Other business activities
  }
  if (c.includes("health") || c.includes("medic") || c.includes("clinic") || c.includes("pharma") || c.includes("care")) {
    return "nic-85"; // Health and social work
  }
  if (c.includes("educat") || c.includes("school") || c.includes("coach") || c.includes("teach") || c.includes("class")) {
    return "nic-80"; // Education
  }
  if (c.includes("hotel") || c.includes("restaur") || c.includes("food") || c.includes("cafe")) {
    return "nic-55"; // Hotels and restaurants
  }
  if (c.includes("real") || c.includes("estat") || c.includes("build") || c.includes("prop")) {
    return "nic-70"; // Real estate activities
  }
  if (c.includes("retail") || c.includes("trad") || c.includes("ecom") || c.includes("shop")) {
    return "nic-52"; // Retail trade
  }
  return null;
}

function slugify(name) {
  return name
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

async function scrapeZaubaCorp(city = "Ahmedabad", category = "Software", limit = 15) {
  const browserPath = getBrowserPath();
  const browser = await puppeteer.launch({
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
      "--lang=en-US,en",
    ],
  });

  const cleanCity = city.trim().toUpperCase();
  const nicCode = getNicCode(category);
  
  // Construct Zauba URL
  // e.g. https://www.zaubacorp.com/company-list/city-AHMEDABAD/status-Active/nic-72-company.html
  // or https://www.zaubacorp.com/company-list/city-AHMEDABAD/status-Active/p-1-company.html
  let url = `https://www.zaubacorp.com/company-list/city-${cleanCity}/status-Active/`;
  if (nicCode) {
    url += `${nicCode}-company.html`;
  } else {
    url += `p-1-company.html`;
  }

  const page = await browser.newPage();
  await page.setUserAgent(
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
  );

  const results = [];

  try {
    await page.goto(url, { waitUntil: "domcontentloaded", timeout: 30000 });
    await new Promise((r) => setTimeout(r, 3500));

    const rawData = await page.evaluate(() => {
      const text = document.body.innerText || "";
      const lines = text.split("\n").map((l) => l.trim()).filter(Boolean);
      const items = [];

      // Indian CIN regex: [UL]\d{5}[A-Z]{2}\d{4}[A-Z]{3}\d{6} or LLP [A-Z]{3}-\d{4}
      const cinRegex = /^[UL]\d{5}[A-Z]{2}\d{4}[A-Z]{3}\d{6}$/;

      for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (cinRegex.test(line)) {
          let companyName = "";
          let status = "Active";

          const prev = lines[i - 1] || "";
          if (["Active", "Strike Off", "Amalgamated", "Dissolved", "Under process of striking off"].includes(prev)) {
            status = prev;
            companyName = lines[i - 2] || "";
          } else {
            companyName = prev;
          }

          const address = lines[i + 1] || "";
          const capital = lines[i + 2] || "";

          // Extract year from CIN: e.g. U72900GJ2022PTC134567 -> 2022
          const yearMatch = line.match(/[A-Z]{2}(\d{4})[A-Z]{3}/);
          const year = yearMatch ? yearMatch[1] : null;

          if (companyName && companyName.length > 2 && !companyName.includes("Page ") && !companyName.includes("Companies Incorpated")) {
            items.push({
              cin: line,
              name: companyName,
              status,
              address,
              capital,
              year,
            });
          }
        }
      }
      return items;
    });

    for (const r of rawData) {
      if (results.length >= limit) break;
      const slug = slugify(r.name);
      results.push({
        id: r.cin,
        name: r.name,
        city: city,
        cin: r.cin,
        year: r.year,
        status: r.status,
        capital: r.capital,
        address: r.address || `${city}, India`,
        category: category || "Registered Company",
        website: null, // Newly incorporated companies do not have an official website yet!
        phone: null,
        sourceUrl: `https://www.zaubacorp.com/company/${slug}/${r.cin}`,
      });
    }
  } catch (err) {
    // If navigation error, output what we have or empty
  } finally {
    await browser.close();
  }

  return results;
}

// CLI Execution
if (require.main === module) {
  const city = process.argv[2] || "Ahmedabad";
  const category = process.argv[3] || "Software";
  const limit = parseInt(process.argv[4] || "15", 10);

  scrapeZaubaCorp(city, category, limit)
    .then((results) => {
      process.stdout.write(JSON.stringify(results, null, 2));
      process.exit(0);
    })
    .catch((err) => {
      process.stderr.write(JSON.stringify({ error: err.message }));
      process.exit(1);
    });
}

module.exports = { scrapeZaubaCorp };
