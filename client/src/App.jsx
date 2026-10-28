import { useState, useEffect } from "react";
import { BrowserRouter, Routes, Route, Link, Navigate, useLocation } from "react-router-dom";
import LeadList from "./pages/LeadList";
import LeadDetail from "./pages/LeadDetail";
import SearchJobs from "./pages/SearchJobs";
import ApprovalQueue from "./pages/ApprovalQueue";
import Login from "./pages/Login";
import { api } from "./api";
import { getToken, clearToken } from "./auth";

function ProtectedRoute({ children }) {
  const token = getToken();
  if (!token) {
    return <Navigate to="/login" replace />;
  }
  return children;
}

function AppLayout({ health, children }) {
  const location = useLocation();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  // Close mobile drawer on route change
  useEffect(() => {
    setMobileMenuOpen(false);
  }, [location.pathname]);

  const handleLogout = () => {
    clearToken();
    window.location.href = "/login";
  };

  const getPageTitle = () => {
    if (location.pathname === "/") return { title: "Leads CRM", sub: "Data-First Pipeline & Lead Database" };
    if (location.pathname.startsWith("/search")) return { title: "Scraper & Job Engine", sub: "Discover Businesses Without Websites" };
    if (location.pathname.startsWith("/drafts")) return { title: "Approval Queue", sub: "Human-in-the-Loop Outreach Dispatch" };
    if (location.pathname.startsWith("/leads/")) return { title: "Lead Profile", sub: "Audit & Opportunity Intelligence" };
    return { title: "AI Client Hunter", sub: "High-Performance B2B Engine" };
  };

  const { title, sub } = getPageTitle();

  const isNavActive = (path) => {
    if (path === "/" && location.pathname === "/") return true;
    if (path !== "/" && location.pathname.startsWith(path)) return true;
    return false;
  };

  return (
    <div className="layout-root">
      {/* Mobile Header Bar */}
      <header className="mobile-header">
        <button
          className="mobile-menu-btn"
          onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
          aria-label="Toggle Navigation"
        >
          {mobileMenuOpen ? "✕" : "☰"}
        </button>
        <Link to="/" className="mobile-brand">
          <span className="brand-badge-icon">🏹</span>
          <span className="mobile-brand-title">Client Hunter</span>
        </Link>
        <div style={{ display: "flex", gap: "6px" }}>
          <Link to="/search" className="btn-mobile-action">
            + Scrape
          </Link>
          <button
            type="button"
            onClick={handleLogout}
            className="btn-mobile-action"
            style={{ background: "#dc2626", borderColor: "#ef4444" }}
            title="Logout"
          >
            Logout
          </button>
        </div>
      </header>

      {/* Mobile Drawer Overlay */}
      {mobileMenuOpen && (
        <div
          className="mobile-drawer-overlay"
          onClick={() => setMobileMenuOpen(false)}
        />
      )}

      {/* Left Sidebar (Desktop + Mobile Drawer) */}
      <aside className={`app-sidebar ${mobileMenuOpen ? "mobile-drawer-open" : ""}`}>
        <div className="sidebar-brand-box">
          <div className="brand-logo-mark">
            <span className="logo-symbol">🏹</span>
          </div>
          <div className="brand-title-wrap">
            <span className="brand-main-text">HUNTER</span>
            <span className="brand-sub-badge">AI CRM v1.2</span>
          </div>
        </div>

        <div className="sidebar-section-label">MAIN NAVIGATION</div>

        <nav className="sidebar-nav">
          <Link
            to="/"
            className={`sidebar-nav-item ${isNavActive("/") ? "active" : ""}`}
          >
            <span className="nav-item-icon">📋</span>
            <span className="nav-item-label">Leads CRM</span>
            {isNavActive("/") && <span className="active-dot" />}
          </Link>

          <Link
            to="/search"
            className={`sidebar-nav-item ${isNavActive("/search") ? "active" : ""}`}
          >
            <span className="nav-item-icon">🔍</span>
            <span className="nav-item-label">Search & Scrape</span>
            {isNavActive("/search") && <span className="active-dot" />}
          </Link>

          <Link
            to="/drafts"
            className={`sidebar-nav-item ${isNavActive("/drafts") ? "active" : ""}`}
          >
            <span className="nav-item-icon">📬</span>
            <span className="nav-item-label">Approval Queue</span>
            {isNavActive("/drafts") && <span className="active-dot" />}
          </Link>

          <button
            type="button"
            onClick={handleLogout}
            className="sidebar-nav-item btn-logout"
            style={{
              width: "100%",
              background: "transparent",
              border: "none",
              color: "inherit",
              font: "inherit",
              textAlign: "left",
              cursor: "pointer",
              marginTop: "8px"
            }}
          >
            <span className="nav-item-icon">🚪</span>
            <span className="nav-item-label">Logout</span>
          </button>
        </nav>

        <div className="sidebar-section-label">SOURCES CONNECTED</div>
        <div className="sidebar-sources-list">
          <span className="source-pill">🌐 Google</span>
          <span className="source-pill">📞 Justdial</span>
          <span className="source-pill">💼 Upwork</span>
          <span className="source-pill">🧑‍💻 Freelancer</span>
          <span className="source-pill">🔴 Reddit</span>
          <span className="source-pill">🏢 Zauba Corp</span>
          <span className="source-pill">💼 LinkedIn</span>
          <span className="source-pill">🗺️ OpenStreetMap</span>
        </div>

        <div className="sidebar-footer">
          {health && (
            <div className="system-health-box">
              <div className="health-row">
                <span className={`status-dot ${health.database === "ok" ? "dot-online" : "dot-offline"}`} />
                <span className="health-db-status">DB: {health.database?.toUpperCase()}</span>
                <span className="health-phase">Phase {health.phase}</span>
              </div>
              <div className="health-host-text">Local SQL Server • 0 Paid API</div>
            </div>
          )}
          <div className="osm-attribution">
            Data via OSM & Public Engines
          </div>
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="main-wrapper">
        {/* Desktop Top Header Bar */}
        <header className="topbar">
          <div className="topbar-left">
            <h1 className="topbar-title">{title}</h1>
            <span className="topbar-subtitle">{sub}</span>
          </div>

          <div className="topbar-right">
            <Link to="/search" className="btn btn-topbar-primary">
              <span className="btn-icon">+</span> Run Scraper
            </Link>
            <button
              type="button"
              onClick={handleLogout}
              className="btn btn-secondary btn-sm"
              title="Sign out of Client Hunter"
            >
              🚪 Logout
            </button>
            {health && (
              <div className="topbar-status-pill">
                <span className={`status-dot ${health.database === "ok" ? "dot-online" : "dot-offline"}`} />
                <span>Engine Active</span>
              </div>
            )}
          </div>
        </header>

        {/* Page Content View */}
        <main className="content-container">
          {children}
        </main>

        {/* Desktop Footer */}
        <footer className="app-footer">
          <p>
            AI Client Hunter • High Performance Local-First CRM • Data from{" "}
            <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">
              OpenStreetMap (ODbL)
            </a>{" "}
            • Human-in-the-loop workflow
          </p>
        </footer>
      </div>

      {/* Mobile Sticky Bottom Navigation (3-Thumb Navigation) */}
      <nav className="mobile-bottom-nav">
        <Link
          to="/"
          className={`mobile-nav-tab ${isNavActive("/") ? "active" : ""}`}
        >
          <span className="mobile-tab-icon">📋</span>
          <span className="mobile-tab-label">CRM</span>
        </Link>
        <Link
          to="/search"
          className={`mobile-nav-tab ${isNavActive("/search") ? "active" : ""}`}
        >
          <span className="mobile-tab-icon">🔍</span>
          <span className="mobile-tab-label">Scraper</span>
        </Link>
        <Link
          to="/drafts"
          className={`mobile-nav-tab ${isNavActive("/drafts") ? "active" : ""}`}
        >
          <span className="mobile-tab-icon">📬</span>
          <span className="mobile-tab-label">Queue</span>
        </Link>
      </nav>
    </div>
  );
}

export default function App() {
  const [health, setHealth] = useState(null);

  useEffect(() => {
    api("/api/health")
      .then(setHealth)
      .catch((err) => console.error("Health check error:", err));
  }, []);

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route
          path="/*"
          element={
            <ProtectedRoute>
              <AppLayout health={health}>
                <Routes>
                  <Route path="/" element={<LeadList />} />
                  <Route path="/leads/:id" element={<LeadDetail />} />
                  <Route path="/drafts" element={<ApprovalQueue />} />
                  <Route path="/search" element={<SearchJobs />} />
                  <Route path="/search-jobs" element={<SearchJobs />} />
                  <Route path="/jobs" element={<SearchJobs />} />
                  <Route path="*" element={<Navigate to="/" replace />} />
                </Routes>
              </AppLayout>
            </ProtectedRoute>
          }
        />
      </Routes>
    </BrowserRouter>
  );
}
