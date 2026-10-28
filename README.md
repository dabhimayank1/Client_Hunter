# 🏹 AI Client Hunter (v2.0)
> **Zero-Cost, Local-First Lead Research, Verification & CRM System**

AI Client Hunter is an intelligent, privacy-first B2B client acquisition CRM. It searches real-world Indian businesses and global freelance projects across **8 major platforms**, automatically discards businesses with existing websites, enriches missing contact details (Phone & Address) via Google, and presents clean, pitch-ready leads in a local CRM with Excel export.

---

## 📁 Project Directory Structure

```
c:\Users\DELL\Desktop\client hunter\
├── client/                     # Frontend Web App (React 19 + Vite + CSS)
│   ├── src/
│   │   ├── pages/
│   │   │   ├── LeadList.jsx    # Leads CRM with filters, search & Excel CSV export
│   │   │   ├── SearchJobs.jsx  # Multi-platform Search Job launcher & live monitor
│   │   │   ├── LeadDetail.jsx  # Detailed lead view, score breakdown & evidence
│   │   │   └── ApprovalQueue.jsx # Outreach draft review & approval workflow
│   │   ├── api.js              # Centralized API fetch client
│   │   ├── App.jsx             # Main Router & Navigation
│   │   └── main.jsx
│   ├── package.json
│   └── vite.config.js
├── server/                     # Backend API (.NET 9 Minimal APIs + SQL Server 2022)
│   ├── AiClientHunter.Api/     # Web API, Background Worker, EF Core DbContext
│   │   ├── Jobs/               # Background JobWorker (Channel-based queue)
│   │   ├── Sources/            # 8 Source Adapters (Justdial, Zauba, Google, Upwork...)
│   │   ├── Services/           # ContactEnrichmentService, Dedupe, Normalizer, Scorer
│   │   ├── Endpoints/          # REST Endpoints (/api/leads, /api/search-jobs, etc.)
│   │   └── appsettings.json
│   ├── AiClientHunter.Tests/   # Unit Test Suite (56 Passing xUnit tests)
│   ├── backup-db.ps1           # SQL Server backup script
│   └── restore-db.ps1          # SQL Server restore script
├── scraper/                    # Headless Chrome Puppeteer Scrapers (0% paid APIs)
│   ├── scrape-justdial.js      # Justdial local Indian business scraper
│   ├── scrape-zaubacorp.js     # Zauba Corp MCA new registered companies
│   ├── scrape-maps.js          # Google Maps headless scraper
│   ├── enrich-contact.js       # Fast multi-tab Google phone & address lookup
│   ├── scrape-upwork.js        # Upwork public project briefs scraper
│   ├── scrape-reddit.js        # Reddit r/forhire client jobs scraper
│   ├── scrape-linkedin.js      # LinkedIn hiring postings scraper
│   └── package.json
├── docs/                       # Project Documentation & Guides
│   └── AI_Client_Hunter_Complete_Guide_v1.2.pdf
├── start.bat                   # ⚡ 1-Click Windows Launcher (Starts Backend + Frontend)
├── start-production.ps1        # Production PowerShell Launcher
├── global.json                 # .NET SDK Version pin
├── .gitignore                  # Git ignore rules
└── README.md                   # This documentation
```

---

## 🚀 Quick Start (1-Click)

### Option 1: Double-Click `start.bat`
Simply double-click **`start.bat`** in the root folder. It will:
1. Start the Backend API on `http://localhost:5000`
2. Start the Frontend Dev Server on `http://localhost:5173`
3. Automatically open your browser to `http://localhost:5173`

### Option 2: Manual Start

**Terminal 1 (Backend API):**
```bash
cd server\AiClientHunter.Api
dotnet run --urls "http://localhost:5000"
```

**Terminal 2 (Frontend):**
```bash
cd client
npm run dev -- --host
```

---

## 🌐 Supported Lead Sources (8 Platforms)

| Platform | Type | Best For | What it Fetches |
|---|---|---|---|
| **📞 Justdial** | Local Directory | Local Indian Businesses | Doctors, Salons, Interior Designers with direct verified phone numbers & NO website |
| **🏢 Zauba Corp** | Company Registry | Newly Registered Companies | Recently incorporated Pvt Ltd / LLP entities needing their first official website |
| **🌐 Google Web/Maps**| Search Engine | Local Businesses | Physical businesses found via Google Maps without an official website |
| **🗺️ OpenStreetMap** | Map Directory | City Directories | Local places extracted via Overpass API bbox queries |
| **🧑‍💻 Freelancer.com** | Freelance Jobs | Live Client Projects | Direct client project briefs, budgets, and links for website development |
| **💼 Upwork** | Freelance Jobs | Remote Client Briefs | Client job posts looking for WordPress, React, Mobile App developers |
| **🔴 Reddit** | Client Community | Direct Client Outreach | r/forhire & r/freelance client posts wanting websites/software built |
| **👔 LinkedIn** | Job Board | Corporate Hiring | Companies hiring developers/designers for web projects |

---

## 🛡️ Strict No-Website Filter (Core Guarantee)

- For business directories (**Justdial, Zauba Corp, Google, OSM**):
  - Any business that **already has an active official website is strictly discarded** by the pipeline.
  - The system ONLY saves businesses that have **NO website** (`WebsiteUrl: null / WebsiteStatus: "missing"`), so you can pitch website creation directly.
  - Direct verified **Phone Numbers (+91...)** and Addresses are attached via Google lookup for instant WhatsApp / Calling outreach.
- For freelance platforms (**Freelancer, Upwork, Reddit, LinkedIn**):
  - These are saved as `🔥 Hot` leads because the client is explicitly asking to build a website/app.

---

## 🔐 Required Environment Variables

When deploying to production (e.g. Render, Docker, VPS), configure the following environment variables in your deployment dashboard:

| Variable | Description | Requirement / Example |
|---|---|---|
| `Jwt__Secret` | Cryptographic key used to sign and verify JWT authentication tokens. | **Required in Production** (min. 32 characters, e.g. `openssl rand -base64 32`). |
| `ADMIN_EMAIL` | Initial administrator account email seeded on first run. | `admin@yourcompany.com` |
| `ADMIN_PASSWORD` | Initial administrator account password. | Minimum 10 characters. |
| `Cors__AllowedOrigins__0` | Allowed cross-origin origin. | Live URL (e.g. `https://client-hunter.onrender.com`), only required if frontend is hosted on a separate domain. |

---

## 🧪 Testing

Run the full xUnit test suite (59 passing tests):
```bash
dotnet test "server\AiClientHunter.Tests\AiClientHunter.Tests.csproj"
```

