import { useState, useEffect } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, getExportUrl } from "../api";

function formatDateTime(isoString) {
  if (!isoString) return { date: "—", time: "", isToday: false, relative: "" };
  let s = String(isoString);
  if (!s.endsWith("Z") && !s.includes("+") && s.includes("T")) {
    s += "Z";
  }
  const d = new Date(s);
  const now = new Date();
  const diffMs = now - d;
  const diffMinutes = Math.floor(diffMs / (1000 * 60));
  const diffHours = Math.floor(diffMs / (1000 * 60 * 60));

  const isToday = d.toDateString() === now.toDateString();

  let relative = "";
  if (diffMinutes < 1) relative = "Just now";
  else if (diffMinutes < 60) relative = `${diffMinutes}m ago`;
  else if (diffHours < 24 && isToday) relative = `${diffHours}h ago`;

  const dateStr = d.toLocaleDateString("en-IN", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });

  const timeStr = d.toLocaleTimeString("en-IN", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });

  return { date: dateStr, time: timeStr, isToday, relative };
}

function cleanPhoneForWhatsApp(phone) {
  if (!phone) return null;
  const digits = String(phone).replace(/\D/g, "");
  if (digits.length === 10) return "91" + digits;
  if (digits.length > 10) return digits;
  return null;
}

export default function LeadList() {
  const [searchParams, setSearchParams] = useSearchParams();

  const [leads, setLeads] = useState([]);
  const [total, setTotal] = useState(0);
  const [meta, setMeta] = useState({ cities: [], categories: [], platforms: [], todayCount: 0 });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  // Filters
  const [priority, setPriority] = useState(searchParams.get("priority") || "");
  const [status, setStatus] = useState(searchParams.get("status") || "");
  const [city, setCity] = useState(searchParams.get("city") || "");
  const [category, setCategory] = useState(searchParams.get("category") || "");
  const [platform, setPlatform] = useState(searchParams.get("platform") || "");
  const [dateRange, setDateRange] = useState(searchParams.get("dateRange") || "");
  const [sortBy, setSortBy] = useState(searchParams.get("sortBy") || "newest");
  const [search, setSearch] = useState(searchParams.get("q") || "");

  const [activeTab, setActiveTab] = useState("all");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);

  // Fetch filter metadata
  const fetchMeta = async () => {
    try {
      const data = await api("/api/leads/meta");
      if (data) setMeta(data);
    } catch (err) {
      console.warn("Could not fetch leads meta:", err);
    }
  };

  const fetchLeads = async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({
        page,
        pageSize,
        sortBy,
      });
      if (priority) params.set("priority", priority);
      if (status) params.set("status", status);
      if (city) params.set("city", city);
      if (category) params.set("category", category);
      if (platform) params.set("platform", platform);
      if (dateRange) params.set("dateRange", dateRange);
      if (search) params.set("q", search);

      const res = await api(`/api/leads?${params.toString()}`);
      setLeads(res.items || []);
      setTotal(res.total || 0);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchMeta();
  }, []);

  useEffect(() => {
    setPriority(searchParams.get("priority") || "");
    setStatus(searchParams.get("status") || "");
    setCity(searchParams.get("city") || "");
    setCategory(searchParams.get("category") || "");
    setPlatform(searchParams.get("platform") || "");
    setDateRange(searchParams.get("dateRange") || "");
    setSortBy(searchParams.get("sortBy") || "newest");
    setSearch(searchParams.get("q") || "");
    setPage(1);
  }, [searchParams]);

  useEffect(() => {
    fetchLeads();
  }, [page, pageSize, priority, status, city, category, platform, dateRange, sortBy]);

  const handleSearchSubmit = (e) => {
    e.preventDefault();
    setPage(1);
    fetchLeads();
  };

  const handleTabChange = (tab) => {
    setActiveTab(tab);
    setPage(1);
    if (tab === "all") {
      setDateRange("");
      setPriority("");
      setPlatform("");
      setSearchParams({});
    } else if (tab === "today") {
      setDateRange("today");
      setPriority("");
    } else if (tab === "potential") {
      setDateRange("");
      setPriority("potential");
    } else if (tab === "hot") {
      setDateRange("");
      setPriority("hot");
    }
  };

  const clearAllFilters = () => {
    setSearch("");
    setCity("");
    setCategory("");
    setPlatform("");
    setDateRange("");
    setPriority("");
    setStatus("");
    setSortBy("newest");
    setActiveTab("all");
    setPage(1);
    setSearchParams({});
  };

  const hasActiveFilters = Boolean(
    search || city || category || platform || dateRange || priority || status || sortBy !== "newest"
  );

  const totalPages = Math.ceil(total / pageSize) || 1;

  const renderPlatformBadge = (plat) => {
    return (
      <span className={`badge ${
        plat === "justdial" ? "badge-source-justdial" :
        plat === "zaubacorp" ? "badge-source-zaubacorp" :
        plat === "freelancer" ? "badge-source-freelancer" :
        plat === "upwork" ? "badge-source-upwork" :
        plat === "reddit" ? "badge-source-reddit" :
        plat === "linkedin" ? "badge-source-linkedin" :
        plat === "osm" ? "badge-source-osm" :
        "badge-source-google"
      }`}>
        {plat === "justdial" ? "📞 Justdial" :
         plat === "zaubacorp" ? "🏢 Zauba Corp" :
         plat === "freelancer" ? "🧑‍💻 Freelancer" :
         plat === "upwork" ? "💼 Upwork" :
         plat === "reddit" ? "🔴 Reddit" :
         plat === "linkedin" ? "💼 LinkedIn" :
         plat === "osm" ? "🗺️ OSM" :
         "🌐 Google Web"}
      </span>
    );
  };

  return (
    <div className="lead-list-page">
      {/* Top Stat Overview Cards (Design 2 & 1 Inspiration) */}
      <div className="crm-stats-grid">
        <div
          className={`stat-kpi-card ${activeTab === "all" ? "active-kpi" : ""}`}
          onClick={() => handleTabChange("all")}
          title="Click to view all leads"
        >
          <div className="stat-kpi-icon">📋</div>
          <div className="stat-kpi-info">
            <span className="stat-kpi-label">TOTAL LEADS</span>
            <span className="stat-kpi-value">{total}</span>
          </div>
          <div className="stat-kpi-sub">Entire Database</div>
        </div>

        <div
          className={`stat-kpi-card ${activeTab === "potential" ? "active-kpi" : ""}`}
          onClick={() => handleTabChange("potential")}
          title="Click to view businesses without websites"
        >
          <div className="stat-kpi-icon stat-icon-green">🌱</div>
          <div className="stat-kpi-info">
            <span className="stat-kpi-label">NO WEBSITE (POTENTIAL)</span>
            <span className="stat-kpi-value">High</span>
          </div>
          <div className="stat-kpi-sub">Ready For Pitch</div>
        </div>

        <div
          className={`stat-kpi-card ${activeTab === "hot" ? "active-kpi" : ""}`}
          onClick={() => handleTabChange("hot")}
          title="Click to view live client briefs"
        >
          <div className="stat-kpi-icon stat-icon-orange">🔥</div>
          <div className="stat-kpi-info">
            <span className="stat-kpi-label">HOT BRIEFS</span>
            <span className="stat-kpi-value">Active</span>
          </div>
          <div className="stat-kpi-sub">Upwork / Freelancer / Reddit</div>
        </div>

        <div
          className={`stat-kpi-card ${activeTab === "today" ? "active-kpi" : ""}`}
          onClick={() => handleTabChange("today")}
          title="Click to view today's discovered leads"
        >
          <div className="stat-kpi-icon stat-icon-blue">✨</div>
          <div className="stat-kpi-info">
            <span className="stat-kpi-label">DISCOVERED TODAY</span>
            <span className="stat-kpi-value">{meta.todayCount || 0}</span>
          </div>
          <div className="stat-kpi-sub">Fresh In Pipeline</div>
        </div>
      </div>

      {/* Quick View Filter Tabs */}
      <div className="crm-tabs-row">
        <div className="crm-tabs">
          <button
            className={`crm-tab ${activeTab === "all" ? "active" : ""}`}
            onClick={() => handleTabChange("all")}
          >
            📋 All Leads <span className="crm-tab-badge">{total}</span>
          </button>
          <button
            className={`crm-tab ${activeTab === "today" ? "active" : ""}`}
            onClick={() => handleTabChange("today")}
          >
            ✨ Today <span className="crm-tab-badge">{meta.todayCount}</span>
          </button>
          <button
            className={`crm-tab ${activeTab === "potential" ? "active" : ""}`}
            onClick={() => handleTabChange("potential")}
          >
            🌱 No Website
          </button>
          <button
            className={`crm-tab ${activeTab === "hot" ? "active" : ""}`}
            onClick={() => handleTabChange("hot")}
          >
            🔥 Hot Projects
          </button>
        </div>

        <div className="crm-export-actions">
          <a
            href={getExportUrl(priority, status, city, category, platform, dateRange, sortBy)}
            className="btn btn-secondary btn-sm"
            target="_blank"
            rel="noreferrer"
          >
            📥 Export CSV
          </a>
        </div>
      </div>

      {/* Filter and Search Bar */}
      <div className="filter-bar">
        <form onSubmit={handleSearchSubmit} className="search-form">
          <div className="search-input-wrap">
            <span className="search-icon">🔍</span>
            <input
              id="lead-search-input"
              name="q"
              type="text"
              placeholder="Search business name, phone, city, category..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="input-search"
              aria-label="Search business name, phone, city, category"
            />
          </div>
          <button type="submit" className="btn btn-primary">
            Search
          </button>
          <button
            type="button"
            className="btn btn-secondary btn-toggle-filters"
            onClick={() => setMobileFiltersOpen(!mobileFiltersOpen)}
          >
            {mobileFiltersOpen ? "Hide Filters ▴" : "Filters ▾"}
          </button>
        </form>

        <div className={`filter-controls-wrap ${mobileFiltersOpen ? "mobile-filters-expanded" : ""}`}>
          <div className="filter-group">
            <label htmlFor="filter-sort">Sort:</label>
            <select
              id="filter-sort"
              name="sortBy"
              value={sortBy}
              onChange={(e) => {
                setSortBy(e.target.value);
                setPage(1);
              }}
              className="select-filter"
            >
              <option value="newest">🕒 Newest First</option>
              <option value="score">⭐ Highest Score</option>
              <option value="name">🔤 Name (A-Z)</option>
              <option value="oldest">⏳ Oldest First</option>
            </select>
          </div>

          <div className="filter-group">
            <label htmlFor="filter-city">City:</label>
            <select
              id="filter-city"
              name="city"
              value={city}
              onChange={(e) => {
                setCity(e.target.value);
                setPage(1);
              }}
              className="select-filter"
            >
              <option value="">All Cities</option>
              {meta.cities?.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>

          <div className="filter-group">
            <label htmlFor="filter-category">Category:</label>
            <select
              id="filter-category"
              name="category"
              value={category}
              onChange={(e) => {
                setCategory(e.target.value);
                setPage(1);
              }}
              className="select-filter"
            >
              <option value="">All Categories</option>
              {meta.categories?.map((cat) => (
                <option key={cat} value={cat}>
                  {cat}
                </option>
              ))}
            </select>
          </div>

          <div className="filter-group">
            <label htmlFor="filter-platform">Platform:</label>
            <select
              id="filter-platform"
              name="platform"
              value={platform}
              onChange={(e) => {
                const val = e.target.value;
                setPlatform(val);
                setPage(1);
                const p = new URLSearchParams(searchParams);
                if (val) p.set("platform", val);
                else p.delete("platform");
                setSearchParams(p);
              }}
              className="select-filter"
            >
              <option value="">All Platforms</option>
              <option value="justdial">📞 Justdial</option>
              <option value="zaubacorp">🏢 Zauba Corp</option>
              <option value="freelancer">🧑‍💻 Freelancer</option>
              <option value="upwork">💼 Upwork</option>
              <option value="reddit">🔴 Reddit</option>
              <option value="google">🌐 Google Web</option>
              <option value="osm">🗺️ OpenStreetMap</option>
              <option value="linkedin">💼 LinkedIn</option>
            </select>
          </div>

          <div className="filter-group">
            <label htmlFor="filter-priority">Priority:</label>
            <select
              id="filter-priority"
              name="priority"
              value={priority}
              onChange={(e) => {
                setPriority(e.target.value);
                setPage(1);
              }}
              className="select-filter"
            >
              <option value="">All Priorities</option>
              <option value="hot">🔥 Hot</option>
              <option value="warm">⚡ Warm</option>
              <option value="potential">🌱 Potential</option>
            </select>
          </div>

          <div className="filter-group">
            <label htmlFor="filter-status">Status:</label>
            <select
              id="filter-status"
              name="status"
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setPage(1);
              }}
              className="select-filter"
            >
              <option value="">All Statuses</option>
              <option value="new">New</option>
              <option value="needs_review">Needs Review</option>
              <option value="qualified">Qualified</option>
              <option value="approved">Approved</option>
              <option value="contacted">Contacted</option>
              <option value="replied">Replied</option>
              <option value="won">Won</option>
              <option value="lost">Lost</option>
            </select>
          </div>

          <div className="filter-group">
            <label htmlFor="filter-pagesize">Rows:</label>
            <select
              id="filter-pagesize"
              name="pageSize"
              value={pageSize}
              onChange={(e) => {
                setPageSize(Number(e.target.value));
                setPage(1);
              }}
              className="select-filter"
            >
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </div>

          {hasActiveFilters && (
            <button
              onClick={clearAllFilters}
              className="btn btn-secondary btn-sm"
              title="Reset all filters"
            >
              ✖ Reset Filters
            </button>
          )}
        </div>
      </div>

      {error && <div className="alert alert-error">Error loading leads: {error}</div>}

      {/* Main Leads View: Desktop Table + Mobile Cards Stream */}
      <div className="leads-display-wrapper">
        {loading ? (
          <div className="loading-state">
            <div className="loading-spinner" />
            <p>Fetching leads from database...</p>
          </div>
        ) : leads.length === 0 ? (
          <div className="empty-state">
            <div className="empty-icon">📭</div>
            <p>No leads match the current filters.</p>
            {hasActiveFilters && (
              <button
                onClick={clearAllFilters}
                className="btn btn-primary btn-sm"
                style={{ marginTop: 12 }}
              >
                Clear Filters
              </button>
            )}
          </div>
        ) : (
          <>
            {/* Desktop Design 2 High-Density Table */}
            <div className="table-card desktop-table-view">
              <table className="leads-table">
                <thead>
                  <tr>
                    <th>📅 Fetched Date & Time</th>
                    <th>Score</th>
                    <th>Business / Project Name</th>
                    <th>Platform</th>
                    <th>Category</th>
                    <th>Location</th>
                    <th>Priority</th>
                    <th>Website Status</th>
                    <th>CRM Status</th>
                    <th>Quick Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {leads.map((lead) => {
                    const { date, time, isToday, relative } = formatDateTime(lead.createdAt);
                    const waPhone = cleanPhoneForWhatsApp(lead.phone);
                    return (
                      <tr key={lead.id}>
                        <td>
                          <div className="lead-date-cell">
                            <div className="lead-date-main">
                              {date}
                              {isToday && <span className="badge-today-mini">Today</span>}
                            </div>
                            <div className="lead-time-sub">
                              🕒 {time} {relative ? `(${relative})` : ""}
                            </div>
                          </div>
                        </td>
                        <td>
                          <span className="score-pill">{lead.score}</span>
                        </td>
                        <td>
                          <Link to={`/leads/${lead.id}`} className="lead-name-link">
                            <strong>{lead.name}</strong>
                          </Link>
                          {lead.phone && (
                            <div className="cell-sub cell-phone-sub">
                              📞 {lead.phone}
                            </div>
                          )}
                          {lead.email && <div className="cell-sub cell-email-sub">✉️ {lead.email}</div>}
                          {lead.websiteUrl && (
                            <div className="cell-sub">
                              <a
                                href={lead.websiteUrl}
                                target="_blank"
                                rel="noreferrer"
                                className="external-link-sub"
                                title={lead.websiteUrl}
                              >
                                🌐 {lead.websiteUrl.replace(/^https?:\/\/(www\.)?/, "").replace(/\/$/, "").substring(0, 28)} ↗
                              </a>
                            </div>
                          )}
                          {lead.evidence?.some((e) => e.note?.includes("Enriched via Google")) && (
                            <div style={{ marginTop: 3 }}>
                              <span className="badge-enriched-google" title="Phone, Email, or Website verified via Google Search & Maps">
                                ✨ Google Enriched
                              </span>
                            </div>
                          )}
                          {!lead.phone && !lead.email && !lead.websiteUrl && (lead.city === "Remote" || lead.dedupeKey?.includes("freelancer") || lead.dedupeKey?.includes("upwork") || lead.dedupeKey?.includes("reddit") || lead.dedupeKey?.includes("linkedin") || lead.dedupeKey?.includes("zaubacorp")) && (
                            <div className="cell-sub" style={{ marginTop: 3 }}>
                              <span className={`badge ${
                                lead.dedupeKey?.includes("zaubacorp") ? "badge-source-zaubacorp" :
                                lead.dedupeKey?.includes("freelancer") ? "badge-source-freelancer" :
                                lead.dedupeKey?.includes("upwork") ? "badge-source-upwork" :
                                lead.dedupeKey?.includes("reddit") ? "badge-source-reddit" :
                                "badge-source-linkedin"
                              }`} style={{ fontSize: "0.72rem", padding: "1px 6px" }}>
                                🔗 Via {
                                  lead.dedupeKey?.includes("zaubacorp") ? "CIN Company Registry" :
                                  lead.dedupeKey?.includes("freelancer") ? "Freelancer Bid" :
                                  lead.dedupeKey?.includes("upwork") ? "Upwork Proposal" :
                                  lead.dedupeKey?.includes("reddit") ? "Reddit DM / Post" :
                                  "LinkedIn Application"
                                }
                              </span>
                            </div>
                          )}
                        </td>
                        <td>{renderPlatformBadge(lead.platform)}</td>
                        <td>
                          <span className="category-text">{lead.category || "—"}</span>
                        </td>
                        <td>
                          <span className="location-text">
                            {lead.city === "Remote" || lead.country?.includes("Remote")
                              ? "🌐 Remote (Global)"
                              : lead.city ? `${lead.city}, ${lead.state || "IN"}` : "India"}
                          </span>
                        </td>
                        <td>
                          <span className={`badge badge-priority badge-${lead.leadType}`}>
                            {lead.leadType}
                          </span>
                        </td>
                        <td>
                          <span className={`badge badge-web ${
                            lead.city === "Remote" || lead.websiteStatus === "platform_job"
                              ? "badge-source-upwork"
                              : `badge-${lead.websiteStatus}`
                          }`}>
                            {lead.city === "Remote" || lead.websiteStatus === "platform_job"
                              ? "💼 Platform Job"
                              : lead.websiteStatus === "not_found"
                              ? "🚫 No Website"
                              : lead.websiteStatus}
                          </span>
                        </td>
                        <td>
                          <span className={`badge badge-status badge-${lead.status}`}>
                            {lead.status.replace("_", " ")}
                          </span>
                        </td>
                        <td>
                          <div className="table-actions-cell">
                            {waPhone && (
                              <a
                                href={`https://wa.me/${waPhone}`}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="action-btn-mini btn-wa-mini"
                                title="Chat on WhatsApp"
                              >
                                💬
                              </a>
                            )}
                            {lead.phone && (
                              <a
                                href={`tel:${lead.phone}`}
                                className="action-btn-mini btn-call-mini"
                                title="Direct Call"
                              >
                                📞
                              </a>
                            )}
                            <Link
                              to={`/leads/${lead.id}`}
                              className="action-btn-mini btn-detail-mini"
                              title="View Lead Profile"
                            >
                              View →
                            </Link>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* Mobile Responsive Cards Stream ("mobile display sportive") */}
            <div className="mobile-cards-view">
              {leads.map((lead) => {
                const { date, time, isToday, relative } = formatDateTime(lead.createdAt);
                const waPhone = cleanPhoneForWhatsApp(lead.phone);
                return (
                  <div key={lead.id} className="mobile-lead-card">
                    <div className="mobile-card-header">
                      <div className="mobile-card-title-box">
                        <Link to={`/leads/${lead.id}`} className="mobile-lead-name">
                          {lead.name}
                        </Link>
                        <div className="mobile-lead-location">
                          📍 {lead.city === "Remote" || lead.country?.includes("Remote")
                            ? "Remote (Global)"
                            : lead.city ? `${lead.city}, ${lead.state || "IN"}` : "India"}
                        </div>
                      </div>
                      <span className="score-pill">{lead.score}</span>
                    </div>

                    <div className="mobile-badges-cluster">
                      {renderPlatformBadge(lead.platform)}
                      {lead.category && (
                        <span className="badge badge-category-sub">{lead.category}</span>
                      )}
                      <span className={`badge badge-web ${
                        lead.city === "Remote" || lead.websiteStatus === "platform_job"
                          ? "badge-source-upwork"
                          : `badge-${lead.websiteStatus}`
                      }`}>
                        {lead.city === "Remote" || lead.websiteStatus === "platform_job"
                          ? "💼 Platform Job"
                          : lead.websiteStatus === "not_found"
                          ? "🚫 No Website"
                          : lead.websiteStatus}
                      </span>
                      <span className={`badge badge-priority badge-${lead.leadType}`}>
                        {lead.leadType}
                      </span>
                    </div>

                    {(lead.phone || lead.email) && (
                      <div className="mobile-contact-bar">
                        {lead.phone && (
                          <span className="mobile-phone-text">📞 {lead.phone}</span>
                        )}
                        {lead.email && (
                          <span className="mobile-email-text">✉️ {lead.email}</span>
                        )}
                      </div>
                    )}

                    <div className="mobile-card-footer">
                      <span className="mobile-time-stamp">
                        🕒 {date} {time} {isToday && <strong style={{ color: "#10b981" }}>• Today</strong>}
                      </span>

                      <div className="mobile-actions-bar">
                        {waPhone && (
                          <a
                            href={`https://wa.me/${waPhone}`}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="mobile-action-btn mobile-wa-btn"
                          >
                            💬 WhatsApp
                          </a>
                        )}
                        {lead.phone && (
                          <a
                            href={`tel:${lead.phone}`}
                            className="mobile-action-btn mobile-call-btn"
                          >
                            📞 Call
                          </a>
                        )}
                        <Link
                          to={`/leads/${lead.id}`}
                          className="mobile-action-btn mobile-detail-btn"
                        >
                          View Profile →
                        </Link>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        )}
      </div>

      {/* Pagination Controls */}
      {totalPages > 1 && (
        <div className="pagination">
          <button
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page === 1}
            className="btn btn-secondary btn-sm"
          >
            ← Previous
          </button>
          <span className="page-indicator">
            Page {page} of {totalPages} ({total} leads)
          </span>
          <button
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page === totalPages}
            className="btn btn-secondary btn-sm"
          >
            Next →
          </button>
        </div>
      )}
    </div>
  );
}
