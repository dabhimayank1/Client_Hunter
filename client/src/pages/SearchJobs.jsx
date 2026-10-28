import { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";

const CITY_OPTIONS = [
  { value: "Ahmedabad", label: "Ahmedabad (Gujarat)" },
  { value: "Surat", label: "Surat (Gujarat)" },
  { value: "Mumbai", label: "Mumbai (Maharashtra)" },
  { value: "Delhi", label: "Delhi / NCR" },
  { value: "Bengaluru", label: "Bengaluru (Karnataka)" },
  { value: "Pune", label: "Pune (Maharashtra)" },
  { value: "Jaipur", label: "Jaipur (Rajasthan)" },
  { value: "Hyderabad", label: "Hyderabad (Telangana)" },
  { value: "Kolkata", label: "Kolkata (West Bengal)" },
  { value: "Lucknow", label: "Lucknow (Uttar Pradesh)" },
  { value: "Indore", label: "Indore (Madhya Pradesh)" },
  { value: "Chandigarh", label: "Chandigarh (Punjab/Haryana)" },
  { value: "Vadodara", label: "Vadodara (Gujarat)" },
  { value: "Rajkot", label: "Rajkot (Gujarat)" },
  { value: "Gandhinagar", label: "Gandhinagar (Gujarat)" },
  { value: "Bhopal", label: "Bhopal (Madhya Pradesh)" },
  { value: "Nagpur", label: "Nagpur (Maharashtra)" },
  { value: "Patna", label: "Patna (Bihar)" },
  { value: "Agra", label: "Agra (Uttar Pradesh)" },
  { value: "Nashik", label: "Nashik (Maharashtra)" },
  { value: "Varanasi", label: "Varanasi (Uttar Pradesh)" },
  { value: "Amritsar", label: "Amritsar (Punjab)" },
  { value: "Coimbatore", label: "Coimbatore (Tamil Nadu)" },
  { value: "Remote", label: "🌐 Remote / Global (International)" },
  { value: "__custom__", label: "✏️ Other City (Type manually...)" },
];

const CATEGORY_OPTIONS = [
  { value: "SEO", label: "🔍 SEO (Search Engine Optimization)" },
  { value: "Local SEO & GMB", label: "📍 Local SEO & Google Business Profile" },
  { value: "SEO & Digital Marketing", label: "📈 SEO & Digital Marketing" },
  { value: "Dental Clinic", label: "🦷 Dental Clinic" },
  { value: "Software", label: "💻 Software & Tech Companies (Zauba Corp)" },
  { value: "Consulting", label: "📈 Business Consulting & Agency" },
  { value: "WordPress", label: "💻 WordPress Website & Plugin" },
  { value: "Web Development", label: "🌐 Web Development (React / Fullstack)" },
  { value: "Mobile App", label: "📱 Mobile App Development (Flutter / iOS / Android)" },
  { value: "UI/UX Design", label: "🎨 UI/UX Design & Figma" },
  { value: "E-commerce Website", label: "🛍️ E-commerce Website (Shopify / WooCommerce)" },
  { value: "Interior Designer", label: "🏠 Interior Designer" },
  { value: "Restaurant", label: "🍽️ Restaurant & Cafe" },
  { value: "Salon", label: "💇 Salon & Beauty Parlour" },
  { value: "Gym", label: "🏋️ Gym & Fitness Centre" },
  { value: "Clinic", label: "🩺 Doctor / Medical Clinic" },
  { value: "Hospital", label: "🏥 Hospital & Eye Hospital" },
  { value: "Ayurvedic Clinic", label: "🌿 Ayurvedic & Homeopathy Clinic" },
  { value: "Lawyer", label: "⚖️ Lawyer & Legal Services" },
  { value: "Chartered Accountant", label: "📊 Chartered Accountant (CA)" },
  { value: "Car Repair", label: "🚗 Car Repair & Detailing" },
  { value: "Hotel", label: "🏨 Hotel & Guest House" },
  { value: "Coaching Classes", label: "🏫 School & Coaching Classes" },
  { value: "Real Estate", label: "🏢 Real Estate Agency" },
  { value: "Boutique", label: "👗 Boutique & Fashion" },
  { value: "Photographer", label: "📸 Photography & Studio" },
  { value: "Plumber", label: "🔧 Plumbing Services" },
  { value: "Electrician", label: "⚡ Electrician Services" },
  { value: "Bakery", label: "🍰 Bakery & Cake Shop" },
  { value: "__custom__", label: "✏️ Other Category (Type manually...)" },
];

function formatJobTime(isoString) {
  if (!isoString) return "";
  let s = String(isoString);
  if (!s.endsWith("Z") && !s.includes("+") && s.includes("T")) {
    s += "Z";
  }
  const d = new Date(s);
  if (isNaN(d.getTime())) return "";
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();
  const timeStr = d.toLocaleTimeString("en-IN", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });

  if (isToday) {
    return timeStr;
  }
  const dateStr = d.toLocaleDateString("en-IN", {
    day: "numeric",
    month: "short",
  });
  return `${dateStr}, ${timeStr}`;
}

export default function SearchJobs() {
  const [jobs, setJobs] = useState([]);
  const [source, setSource] = useState("justdial");
  const [selectedCity, setSelectedCity] = useState("Ahmedabad");
  const [customCity, setCustomCity] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("Dental Clinic");
  const [customCategory, setCustomCategory] = useState("");
  const [leadType, setLeadType] = useState("potential");
  const [limit, setLimit] = useState(25);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState(null);
  const [retryingId, setRetryingId] = useState(null);

  const handleSourceChange = (newSource) => {
    setSource(newSource);
    if (newSource === "freelancer" || newSource === "upwork" || newSource === "reddit" || newSource === "linkedin") {
      setLeadType("hot");
      if (selectedCity !== "__custom__") {
        setSelectedCity("Remote");
      }
      if (selectedCategory === "Dental Clinic" || selectedCategory === "Software") {
        setSelectedCategory("WordPress");
      }
    } else if (newSource === "justdial") {
      setLeadType("potential");
      if (selectedCity === "Remote") {
        setSelectedCity("Ahmedabad");
      }
      if (selectedCategory === "WordPress" || selectedCategory === "Software") {
        setSelectedCategory("Dental Clinic");
      }
    } else if (newSource === "zaubacorp") {
      setLeadType("potential");
      if (selectedCity === "Remote") {
        setSelectedCity("Ahmedabad");
      }
      if (selectedCategory === "Dental Clinic" || selectedCategory === "WordPress") {
        setSelectedCategory("Software");
      }
    } else if (newSource === "google" || newSource === "osm") {
      setLeadType("potential");
      if (selectedCity === "Remote") {
        setSelectedCity("Ahmedabad");
      }
      if (selectedCategory === "WordPress") {
        setSelectedCategory("Dental Clinic");
      }
    }
  };

  const fetchJobs = async () => {
    try {
      const data = await api("/api/search-jobs");
      setJobs(data || []);
    } catch (err) {
      console.error("Failed to fetch jobs:", err);
    }
  };

  useEffect(() => {
    fetchJobs();
  }, []);

  // Poll every 3 seconds if any job is queued or running
  useEffect(() => {
    const hasActiveJob = jobs.some(
      (j) => j.status === "queued" || j.status === "running"
    );

    if (!hasActiveJob) return;

    const interval = setInterval(() => {
      fetchJobs();
    }, 3000);

    return () => clearInterval(interval);
  }, [jobs]);

  const handleSubmit = async (e) => {
    e.preventDefault();
    const finalCity = selectedCity === "__custom__" ? customCity.trim() : selectedCity.trim();
    const finalCategory = selectedCategory === "__custom__" ? customCategory.trim() : selectedCategory.trim();

    if (!finalCity) {
      setFormError("Please select or enter a target city.");
      return;
    }
    if (!finalCategory) {
      setFormError("Please select or enter a business category.");
      return;
    }

    setFormError(null);
    setSubmitting(true);

    try {
      await api("/api/search-jobs", {
        method: "POST",
        body: JSON.stringify({
          city: finalCity,
          category: finalCategory,
          source,
          leadType,
          limit: parseInt(limit, 10),
        }),
      });
      await fetchJobs();
    } catch (err) {
      setFormError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleRetry = async (jobId) => {
    setRetryingId(jobId);
    try {
      await api(`/api/search-jobs/${jobId}/retry`, { method: "POST" });
      await fetchJobs();
    } catch (err) {
      alert(`Retry failed: ${err.message}`);
    } finally {
      setRetryingId(null);
    }
  };

  return (
    <div className="search-jobs-page">
      <div className="page-header">
        <div>
          <h2>Search Jobs & Background Worker</h2>
          <p className="subtext">
            Search any Indian City and Business Category without API keys. Businesses without websites are automatically discovered and queued for outreach.
          </p>
        </div>
      </div>

      {/* Search Command Form */}
      <div className="card job-form-card">
        <h3>⚡ Launch New Search Job</h3>
        <form onSubmit={handleSubmit} className="job-form">
          <div className="form-grid">
            <div className="form-group">
              <label htmlFor="job-source-select">Search Engine / Source</label>
              <select
                id="job-source-select"
                name="source"
                value={source}
                onChange={(e) => handleSourceChange(e.target.value)}
                className="select-input"
              >
                <option value="justdial">📞 Justdial (Local Indian Businesses & Direct Phone)</option>
                <option value="zaubacorp">🏢 Zauba Corp (Newly Registered Indian Companies & LLPs)</option>
                <option value="google">🌐 Google Web Search (No-Website Local Businesses)</option>
                <option value="freelancer">🧑‍💻 Freelancer.com (Live Client Projects & Budgets)</option>
                <option value="upwork">💼 Upwork (Live Remote Client Briefs & Proposals)</option>
                <option value="reddit">🔴 Reddit (Client Website & Software Projects Only)</option>
                <option value="osm">🗺️ OpenStreetMap (Directory Extraction)</option>
              </select>
            </div>

            {/* Target City Dropdown */}
            <div className="form-group">
              <label htmlFor="job-city-select">Target City</label>
              <select
                id="job-city-select"
                name="city"
                value={selectedCity}
                onChange={(e) => setSelectedCity(e.target.value)}
                className="select-input"
              >
                {CITY_OPTIONS.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
              {selectedCity === "__custom__" && (
                <input
                  id="job-custom-city"
                  name="customCity"
                  type="text"
                  value={customCity}
                  onChange={(e) => setCustomCity(e.target.value)}
                  placeholder="Enter custom city name (e.g. Udaipur, Surat, Goa)..."
                  className="text-input"
                  style={{ marginTop: 8 }}
                  required
                  autoFocus
                />
              )}
            </div>

            {/* Business Category Dropdown */}
            <div className="form-group">
              <label htmlFor="job-category-select">Business Category</label>
              <select
                id="job-category-select"
                name="category"
                value={selectedCategory}
                onChange={(e) => setSelectedCategory(e.target.value)}
                className="select-input"
              >
                {CATEGORY_OPTIONS.map((cat) => (
                  <option key={cat.value} value={cat.value}>
                    {cat.label}
                  </option>
                ))}
              </select>
              {selectedCategory === "__custom__" && (
                <input
                  id="job-custom-category"
                  name="customCategory"
                  type="text"
                  value={customCategory}
                  onChange={(e) => setCustomCategory(e.target.value)}
                  placeholder="Enter custom business category (e.g. Yoga Studio, Event Planner)..."
                  className="text-input"
                  style={{ marginTop: 8 }}
                  required
                  autoFocus
                />
              )}
            </div>

            <div className="form-group">
              <label htmlFor="job-leadtype-select">Lead Type Focus</label>
              <select
                id="job-leadtype-select"
                name="leadType"
                value={leadType}
                onChange={(e) => setLeadType(e.target.value)}
                className="select-input"
              >
                <option value="potential">🌱 Potential (No Website - Best for Pitching)</option>
                <option value="warm">⚡ Warm (New Entity - No Website)</option>
                <option value="hot">🔥 Hot (Client Brief / Project - Needs Website/App)</option>
              </select>
              <span style={{ fontSize: 11, color: "#64748b", marginTop: 4, display: "block" }}>
                {source === "freelancer" || source === "upwork" || source === "reddit"
                  ? "🎯 Live client projects where clients actively want a website built."
                  : "🛡️ Strict Filter Enabled: Businesses with existing websites are automatically discarded."}
              </span>
            </div>

            <div className="form-group">
              <label htmlFor="job-limit-input">Result Limit (1 to 200)</label>
              <input
                id="job-limit-input"
                name="limit"
                type="number"
                min="1"
                max="200"
                value={limit}
                onChange={(e) => setLimit(e.target.value)}
                className="text-input"
              />
            </div>
          </div>

          {formError && <div className="alert alert-error">{formError}</div>}

          <button
            type="submit"
            disabled={submitting}
            className="btn btn-primary btn-submit-job"
          >
            {submitting ? "Searching & Enqueueing Job..." : "🚀 Launch Search Job"}
          </button>
        </form>
      </div>

      {/* Jobs History Table */}
      <div className="card jobs-table-card">
        <div className="card-header-row">
          <h3>Recent Search Jobs</h3>
          <button onClick={fetchJobs} className="btn-sm btn-outline">
            🔄 Refresh List
          </button>
        </div>

        {jobs.length === 0 ? (
          <div className="empty-state">No search jobs found yet. Launch one above!</div>
        ) : (
          <>
            {/* Desktop Compact 5-Column Table (No Horizontal Dragging) */}
            <div className="desktop-jobs-view">
              <table className="jobs-table">
                <thead>
                  <tr>
                    <th>Job & Source</th>
                    <th>Target & Query</th>
                    <th>Results</th>
                    <th>Status & Time</th>
                    <th style={{ textAlign: "right" }}>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {jobs.map((j) => (
                    <tr key={j.id}>
                      <td>
                        <div className="job-id-cell">
                          <strong>#{j.id}</strong>
                          <span className={`badge ${
                            j.source === "justdial" ? "badge-source-justdial" :
                            j.source === "zaubacorp" ? "badge-source-zaubacorp" :
                            j.source === "google" ? "badge-source-google" :
                            j.source === "freelancer" ? "badge-source-freelancer" :
                            j.source === "upwork" ? "badge-source-upwork" :
                            j.source === "reddit" ? "badge-source-reddit" :
                            j.source === "linkedin" ? "badge-source-linkedin" :
                            "badge-source-osm"
                          }`}>
                            {j.source === "justdial" ? "📞 Justdial" :
                             j.source === "zaubacorp" ? "🏢 Zauba Corp" :
                             j.source === "google" ? "🌐 Google" :
                             j.source === "freelancer" ? "🧑‍💻 Freelancer" :
                             j.source === "upwork" ? "💼 Upwork" :
                             j.source === "reddit" ? "🔴 Reddit" :
                             j.source === "linkedin" ? "💼 LinkedIn" : "🗺️ OSM"}
                          </span>
                        </div>
                      </td>
                      <td>
                        <div className="job-target-cell">
                          <strong className="job-target-title">{j.category}</strong>
                          <span className="job-target-city">📍 {j.city}</span>
                          <div className="cell-sub">Limit: {j.resultLimit} • Type: {j.leadType}</div>
                        </div>
                      </td>
                      <td>
                        <div className="job-results-cell">
                          <span className="results-highlight">
                            <strong>{j.newLeads} new leads</strong>
                          </span>
                          <div className="cell-sub">
                            {j.recordsFound} found {j.duplicates > 0 ? `(${j.duplicates} dupe)` : ""}
                          </div>
                        </div>
                      </td>
                      <td>
                        <div className="job-status-cell">
                          <span className={`badge badge-status badge-${j.status}`}>
                            {j.status === "running" && "⏳ "}
                            {j.status}
                          </span>
                          <div className="job-time-text">
                            🕒 {j.finishedAt ? `Done: ${formatJobTime(j.finishedAt)}` : `Queued: ${formatJobTime(j.createdAt)}`}
                          </div>
                          {j.error && <div className="cell-error">{j.error}</div>}
                        </div>
                      </td>
                      <td style={{ textAlign: "right" }}>
                        {j.status === "completed" && (j.newLeads > 0 || j.recordsFound > 0) && (
                          <Link
                            to={`/?platform=${encodeURIComponent(j.source)}`}
                            className="btn-view-job-leads"
                          >
                            View Leads →
                          </Link>
                        )}
                        {j.status === "completed" && j.newLeads === 0 && j.recordsFound === 0 && (
                          <span className="cell-sub text-muted">0 leads found</span>
                        )}
                        {j.status === "failed" && (
                          <button
                            onClick={() => handleRetry(j.id)}
                            disabled={retryingId === j.id}
                            className="btn btn-secondary btn-sm"
                          >
                            {retryingId === j.id ? "Retrying..." : "🔄 Retry"}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Mobile Responsive Job Cards Stream (Zero Dragging on Phone) */}
            <div className="mobile-jobs-cards">
              {jobs.map((j) => (
                <div key={j.id} className="mobile-job-card">
                  <div className="mobile-job-card-header">
                    <div className="mobile-job-id-box">
                      <span className="mobile-job-id">#{j.id}</span>
                      <span className={`badge ${
                        j.source === "justdial" ? "badge-source-justdial" :
                        j.source === "zaubacorp" ? "badge-source-zaubacorp" :
                        j.source === "google" ? "badge-source-google" :
                        j.source === "freelancer" ? "badge-source-freelancer" :
                        j.source === "upwork" ? "badge-source-upwork" :
                        j.source === "reddit" ? "badge-source-reddit" :
                        j.source === "linkedin" ? "badge-source-linkedin" :
                        "badge-source-osm"
                      }`}>
                        {j.source === "justdial" ? "📞 Justdial" :
                         j.source === "zaubacorp" ? "🏢 Zauba Corp" :
                         j.source === "google" ? "🌐 Google" :
                         j.source === "freelancer" ? "🧑‍💻 Freelancer" :
                         j.source === "upwork" ? "💼 Upwork" :
                         j.source === "reddit" ? "🔴 Reddit" :
                         j.source === "linkedin" ? "💼 LinkedIn" : "🗺️ OSM"}
                      </span>
                    </div>
                    <span className={`badge badge-status badge-${j.status}`}>
                      {j.status === "running" && "⏳ "}
                      {j.status}
                    </span>
                  </div>

                  <div className="mobile-job-target-info">
                    <div className="mobile-job-title">{j.category}</div>
                    <div className="mobile-job-location">📍 {j.city}</div>
                    <div className="mobile-job-sub">Limit: {j.resultLimit} | Type: {j.leadType}</div>
                  </div>

                  <div className="mobile-job-stats-bar">
                    <div className="stat-pill stat-pill-green">
                      <span className="stat-num">{j.newLeads}</span>
                      <span className="stat-lbl">New Leads</span>
                    </div>
                    <div className="stat-pill">
                      <span className="stat-num">{j.recordsFound}</span>
                      <span className="stat-lbl">Total Found</span>
                    </div>
                    {j.duplicates > 0 && (
                      <div className="stat-pill">
                        <span className="stat-num">{j.duplicates}</span>
                        <span className="stat-lbl">Duplicate</span>
                      </div>
                    )}
                  </div>

                  <div className="mobile-job-footer">
                    <span className="mobile-job-time">
                      🕒 {j.finishedAt ? `Done: ${formatJobTime(j.finishedAt)}` : `Queued: ${formatJobTime(j.createdAt)}`}
                    </span>
                    {j.error && (
                      <div className="cell-error" style={{ color: "#ef4444", fontSize: 12, marginTop: 4, width: "100%" }}>
                        ⚠️ {j.error}
                      </div>
                    )}
                    {j.status === "completed" && (j.newLeads > 0 || j.recordsFound > 0) && (
                      <Link
                        to={`/?platform=${encodeURIComponent(j.source)}`}
                        className="btn-mobile-job-action"
                      >
                        View Leads →
                      </Link>
                    )}
                    {j.status === "failed" && (
                      <button
                        onClick={() => handleRetry(j.id)}
                        disabled={retryingId === j.id}
                        className="btn-mobile-job-action btn-danger"
                      >
                        {retryingId === j.id ? "Retrying..." : "🔄 Retry"}
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
