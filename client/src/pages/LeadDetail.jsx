import { useState, useEffect } from "react";
import { useParams, Link } from "react-router-dom";
import { api } from "../api";
function toLocalDate(isoString) {
  if (!isoString) return new Date();
  let s = String(isoString);
  if (!s.endsWith("Z") && !s.includes("+") && s.includes("T")) {
    s += "Z";
  }
  return new Date(s);
}

export default function LeadDetail() {
  const { id } = useParams();
  const [lead, setLead] = useState(null);
  const [drafts, setDrafts] = useState([]);
  const [scores, setScores] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [updating, setUpdating] = useState(false);
  const [analyzing, setAnalyzing] = useState(false);
  const [message, setMessage] = useState(null);
  const [aiAnalysis, setAiAnalysis] = useState(null);

  const fetchLead = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api(`/api/leads/${id}`);
      // Handle either direct lead object or { lead, drafts, scores } wrapper
      if (data.lead) {
        setLead(data.lead);
        setDrafts(data.drafts || []);
        setScores(data.scores || []);
      } else {
        setLead(data);
      }
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLead();
  }, [id]);

  const handleStatusChange = async (newStatus) => {
    setUpdating(true);
    setMessage(null);
    try {
      const updated = await api(`/api/leads/${id}/status`, {
        method: "PATCH",
        body: JSON.stringify({ status: newStatus }),
      });
      setLead(updated);
      setMessage(`Status updated to "${newStatus.replace("_", " ")}" successfully.`);
    } catch (err) {
      setError(err.message);
    } finally {
      setUpdating(false);
    }
  };

  const handleDoNotContact = async () => {
    if (!window.confirm("Are you sure you want to add this contact/domain to the Do Not Contact suppression list? This will block future outreach.")) {
      return;
    }
    setUpdating(true);
    setMessage(null);
    try {
      const res = await api(`/api/leads/${id}/do-not-contact`, {
        method: "POST",
      });
      setLead(res.lead);
      setMessage(res.message);
    } catch (err) {
      setError(err.message);
    } finally {
      setUpdating(false);
    }
  };

  const handleRunAiAnalysis = async () => {
    setAnalyzing(true);
    setMessage(null);
    try {
      const res = await api(`/api/leads/${id}/analyze`, {
        method: "POST",
      });
      setAiAnalysis(res.analysis);
      if (res.draft) {
        setDrafts((prev) => [res.draft, ...prev]);
      }
      if (res.leadStatus) {
        setLead((prev) => ({ ...prev, status: res.leadStatus }));
      }
      setMessage("AI analysis and outreach draft generated successfully!");
    } catch (err) {
      setError(err.message);
    } finally {
      setAnalyzing(false);
    }
  };

  if (loading) return <div className="loading-state">Loading lead #{id}...</div>;
  if (error) return <div className="alert alert-error">Error: {error}</div>;
  if (!lead) return <div className="empty-state">Lead not found.</div>;

  const latestDraft = drafts[0];

  const address = (() => {
    try {
      if (lead?.evidence && lead.evidence.length > 0) {
        for (const ev of lead.evidence) {
          if (ev.sourceRecord?.rawJson) {
            const parsed = JSON.parse(ev.sourceRecord.rawJson);
            if (parsed.address && typeof parsed.address === "string" && parsed.address.trim().length > 0) {
              return parsed.address;
            }
          }
        }
      }
    } catch (e) {}
    if (lead?.city) {
      return `${lead.city}, ${lead.state || "India"}`;
    }
    return null;
  })();

  const projectInfo = (() => {
    try {
      if (lead?.evidence && lead.evidence.length > 0) {
        for (const ev of lead.evidence) {
          if (ev.sourceRecord?.rawJson) {
            const parsed = JSON.parse(ev.sourceRecord.rawJson);
            if (parsed.source === "freelancer" || parsed.source === "upwork" || parsed.budget) {
              return {
                platform: parsed.source || ev.sourceRecord.sourceName,
                budget: parsed.budget || "Flexible",
                description: parsed.description || parsed.snippet,
                projectType: parsed.projectType || parsed.jobType,
                url: parsed.url || ev.sourceRecord.sourceUrl,
                posted: parsed.posted,
                bids: parsed.bids,
              };
            }
          }
        }
      }
    } catch (e) {}
    return null;
  })();

  return (
    <div className="lead-detail-page">
      <div className="breadcrumb">
        <Link to="/" className="btn-back">
          ← Back to All Leads
        </Link>
      </div>

      {message && <div className="alert alert-success">{message}</div>}

      <div className="detail-header-card">
        <div className="title-section">
          <div className="badge-row">
            <span className={`badge badge-priority badge-${lead.leadType}`}>
              {lead.leadType.toUpperCase()}
            </span>
            <span className={`badge badge-status badge-${lead.status}`}>
              {lead.status.replace("_", " ")}
            </span>
            <span className="score-pill">Score: {lead.score} pts</span>
          </div>
          <h1>{lead.name}</h1>
          <p className="subtext">
            Category: {lead.category || "General"} | ID: #{lead.id} | 📅 Discovered:{" "}
            {toLocalDate(lead.createdAt).toLocaleDateString("en-IN", {
              day: "numeric",
              month: "short",
              year: "numeric",
            })}{" "}
            at{" "}
            {toLocalDate(lead.createdAt).toLocaleTimeString("en-IN", {
              hour: "2-digit",
              minute: "2-digit",
            })}
          </p>
        </div>

        <div className="action-section">
          <button
            onClick={handleRunAiAnalysis}
            disabled={analyzing || lead.status === "do_not_contact"}
            className="btn btn-primary"
          >
            {analyzing ? "⚡ Analyzing Lead..." : "✨ Generate AI Draft"}
          </button>

          <div className="status-updater">
            <label htmlFor="lead-status-select">Update Status:</label>
            <select
              id="lead-status-select"
              name="leadStatus"
              value={lead.status}
              disabled={updating}
              onChange={(e) => handleStatusChange(e.target.value)}
              className="select-status"
            >
              <option value="new">New</option>
              <option value="needs_review">Needs Review</option>
              <option value="qualified">Qualified</option>
              <option value="draft_ready">Draft Ready</option>
              <option value="approved">Approved</option>
              <option value="contacted">Contacted</option>
              <option value="replied">Replied</option>
              <option value="follow_up_due">Follow-up Due</option>
              <option value="won">Won</option>
              <option value="lost">Lost</option>
              <option value="do_not_contact">Do Not Contact</option>
            </select>
          </div>

          <button
            onClick={handleDoNotContact}
            disabled={updating || lead.status === "do_not_contact"}
            className="btn btn-danger btn-sm"
          >
            🚫 Suppress (Do Not Contact)
          </button>
        </div>
      </div>

      {/* AI Analysis & Draft Section */}
      {(latestDraft || aiAnalysis) && (
        <div className="card ai-card">
          <div className="ai-card-header">
            <h3>🤖 AI Analysis & Personalized Outreach Draft</h3>
            <span className="badge badge-status badge-draft_ready">
              {latestDraft ? `Version ${latestDraft.version} (${latestDraft.status})` : "Generated"}
            </span>
          </div>

          {aiAnalysis && (
            <div className="ai-summary-box">
              <p><strong>Lead Summary:</strong> {aiAnalysis.summary}</p>
              <p>
                <strong>Recommended Service:</strong>{" "}
                <span className="badge badge-priority badge-warm">
                  {aiAnalysis.recommendedService}
                </span>
              </p>
              {aiAnalysis.missingInfo && aiAnalysis.missingInfo.length > 0 && (
                <p className="missing-info">
                  <strong>Missing Info:</strong> {aiAnalysis.missingInfo.join(", ")}
                </p>
              )}
            </div>
          )}

          <div className="draft-body-box">
            <label><strong>Draft Outreach Message (Human in the loop):</strong></label>
            <textarea
              readOnly
              value={latestDraft?.body || aiAnalysis?.draftMessage || ""}
              rows={4}
              className="draft-textarea"
            />
          </div>

          <div className="draft-actions">
            {lead.phone && (
              <a
                href={`https://wa.me/${lead.phone.replace(/\D/g, "")}?text=${encodeURIComponent(
                  latestDraft?.body || aiAnalysis?.draftMessage || ""
                )}`}
                target="_blank"
                rel="noreferrer"
                className="btn btn-primary btn-sm"
              >
                💬 Open WhatsApp with Pre-filled Draft
              </a>
            )}
            {lead.email && (
              <a
                href={`mailto:${lead.email}?subject=${encodeURIComponent(
                  latestDraft?.subject || "Website Opportunity"
                )}&body=${encodeURIComponent(
                  latestDraft?.body || aiAnalysis?.draftMessage || ""
                )}`}
                className="btn btn-secondary btn-sm"
              >
                ✉️ Open Email Client
              </a>
            )}
            <button
              onClick={() => {
                navigator.clipboard.writeText(latestDraft?.body || aiAnalysis?.draftMessage || "");
                alert("Draft copied to clipboard!");
              }}
              className="btn btn-secondary btn-sm"
            >
              📋 Copy Draft
            </button>
            <Link to="/drafts" className="btn btn-outline btn-sm">
              📬 Open Approval Queue
            </Link>
          </div>
        </div>
      )}

      {/* Freelance Project Brief Card */}
      {projectInfo && (
        <div className="card" style={{ marginBottom: 20, borderLeft: "5px solid #2563eb", background: "#f8fafc" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12, flexWrap: "wrap", gap: 10 }}>
            <h3 style={{ margin: 0, color: "#1e3a8a" }}>
              💼 {projectInfo.platform === "freelancer" ? "🧑‍💻 Freelancer.com" :
                  projectInfo.platform === "reddit" ? "🔴 Reddit Client Project" :
                  "💼 Upwork"} Client Project Brief
            </h3>
            {projectInfo.url && (
              <a
                href={projectInfo.url}
                target="_blank"
                rel="noreferrer"
                className="btn btn-primary btn-sm"
              >
                🔗 Open Post on {
                  projectInfo.platform === "freelancer" ? "Freelancer.com" :
                  projectInfo.platform === "reddit" ? "Reddit" :
                  projectInfo.platform === "linkedin" ? "LinkedIn" :
                  "Upwork"
                } →
              </a>
            )}
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 16, marginBottom: 14 }}>
            <div>
              <span className="text-muted" style={{ fontSize: "0.85rem" }}>Client Budget</span>
              <div style={{ fontSize: "1.15rem", fontWeight: 700, color: "#16a34a", marginTop: 2 }}>
                {projectInfo.budget}
              </div>
            </div>
            {projectInfo.projectType && (
              <div>
                <span className="text-muted" style={{ fontSize: "0.85rem" }}>Job Type</span>
                <div style={{ fontWeight: 600, marginTop: 2 }}>{projectInfo.projectType}</div>
              </div>
            )}
            {projectInfo.posted && (
              <div>
                <span className="text-muted" style={{ fontSize: "0.85rem" }}>Posted Time</span>
                <div style={{ fontWeight: 600, marginTop: 2 }}>{projectInfo.posted}</div>
              </div>
            )}
            {projectInfo.bids !== undefined && (
              <div>
                <span className="text-muted" style={{ fontSize: "0.85rem" }}>Proposals Received</span>
                <div style={{ fontWeight: 600, marginTop: 2 }}>{projectInfo.bids} proposals</div>
              </div>
            )}
          </div>
          {projectInfo.description && (
            <div>
              <span className="text-muted" style={{ fontSize: "0.85rem" }}>Project Requirements & Brief:</span>
              <p style={{ marginTop: 6, lineHeight: 1.6, background: "#fff", padding: 14, borderRadius: 6, border: "1px solid var(--border)", color: "#1e293b", whiteSpace: "pre-line" }}>
                {projectInfo.description}
              </p>
            </div>
          )}
        </div>
      )}

      {/* Grid of Lead Info */}
      <div className="detail-grid">
        <div className="card">
          <h3>📍 Location & Market</h3>
          <p><strong>City:</strong> {lead.city || "Not specified"}</p>
          <p><strong>State:</strong> {lead.state || "Not specified"}</p>
          <p><strong>Country:</strong> {lead.country}</p>
          {address && <p><strong>Address:</strong> {address}</p>}
          <p><strong>Dedupe Key:</strong> <code>{lead.dedupeKey}</code></p>
        </div>

        <div className="card">
          <h3>🌐 Digital Presence & Website</h3>
          <p>
            <strong>Website Status:</strong>{" "}
            <span className={`badge badge-web badge-${lead.websiteStatus}`}>
              {lead.websiteStatus}
            </span>
          </p>
          {lead.evidence?.some((e) => e.note?.includes("Enriched via Google")) && (
            <div style={{ margin: "6px 0 12px 0" }}>
              <span className="badge-enriched-google" style={{ fontSize: "0.82rem", padding: "4px 10px" }}>
                ✨ Verified & Enriched via Google
              </span>
            </div>
          )}
          <p>
            <strong>Website URL:</strong>{" "}
            {lead.websiteUrl ? (
              <a href={lead.websiteUrl} target="_blank" rel="noreferrer">
                {lead.websiteUrl}
              </a>
            ) : (
              <span className="text-muted">No website listed</span>
            )}
          </p>
          {lead.phone && (
            <p>
              <strong>Phone:</strong> {lead.phone}{" "}
              <a
                href={`https://wa.me/${lead.phone.replace(/\D/g, "")}`}
                target="_blank"
                rel="noreferrer"
                className="btn-link"
              >
                (WhatsApp Chat)
              </a>
            </p>
          )}
          {lead.email && (
            <p>
              <strong>Email:</strong> {lead.email}{" "}
              <a
                href={`mailto:${lead.email}`}
                className="btn-link"
              >
                (Send Email)
              </a>
            </p>
          )}

          {!lead.phone && !lead.email && projectInfo && (
            <div style={{ marginTop: 12, padding: "12px 14px", background: "#f0fdf4", border: "1px solid #bbf7d0", borderRadius: 8 }}>
              <div style={{ color: "#166534", fontWeight: 700, fontSize: "0.9rem", display: "flex", alignItems: "center", gap: 6, marginBottom: 6 }}>
                🔒 Platform Direct Outreach:
              </div>
              <p className="cell-sub" style={{ margin: "0 0 10px 0", color: "#15803d", fontSize: "0.82rem", lineHeight: 1.5 }}>
                Client marketplaces and hiring boards (Upwork, Freelancer, Reddit, LinkedIn) keep client direct phone/email private to prevent off-platform bypass and spam. Outreach is done directly by messaging the client or submitting a proposal on the official link:
              </p>
              {projectInfo.url && (
                <a
                  href={projectInfo.url}
                  target="_blank"
                  rel="noreferrer"
                  className="btn btn-primary btn-sm"
                  style={{ display: "inline-block" }}
                >
                  🚀 Open Client Post & Send Proposal →
                </a>
              )}
            </div>
          )}
        </div>

        <div className="card">
          <h3>⏱️ Timeline & Audit</h3>
          <p><strong>First Discovered:</strong> {toLocalDate(lead.createdAt).toLocaleString("en-IN")}</p>
          <p><strong>Last Updated:</strong> {toLocalDate(lead.updatedAt).toLocaleString("en-IN")}</p>
          {lead.followUpDueAt && (
            <p><strong>Follow-Up Due:</strong> {toLocalDate(lead.followUpDueAt).toLocaleDateString("en-IN")}</p>
          )}
        </div>
      </div>

      {/* Scoring Breakdown Reasons */}
      {scores.length > 0 && (
        <div className="card score-breakdown-card">
          <h3>📊 Score Breakdown & Evaluation Rationale</h3>
          <ul className="score-list">
            {scores.map((s) => (
              <li key={s.id} className="score-item">
                <div className="score-header-row">
                  <span className="score-pill">{s.points} Points</span>
                  <span className={`badge badge-priority badge-${s.priority}`}>
                    {s.priority.toUpperCase()}
                  </span>
                  <span className="text-muted cell-sub">Rule Version: {s.ruleVersion}</span>
                </div>
                <p className="score-reasons"><strong>Signals Found:</strong> {s.reasons}</p>
                <span className="evidence-time">Evaluated on: {toLocalDate(s.createdAt).toLocaleString("en-IN")}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* Evidence & Provenance Section */}
      <div className="evidence-section card">
        <h3>🔍 Source Evidence & Provenance</h3>
        {lead.evidence && lead.evidence.length > 0 ? (
          <ul className="evidence-list">
            {lead.evidence.map((ev) => (
              <li key={ev.id} className="evidence-item">
                <div className="evidence-source">
                  <strong>Source:</strong> {ev.sourceRecord?.sourceName || "Direct / Manual"}
                  {ev.sourceRecord?.sourceUrl && (
                    <a href={ev.sourceRecord.sourceUrl} target="_blank" rel="noreferrer" className="source-link">
                      [View Source URL]
                    </a>
                  )}
                </div>
                {ev.note && <p className="evidence-note">{ev.note}</p>}
                <span className="evidence-time">
                  Recorded on: {toLocalDate(ev.createdAt).toLocaleString("en-IN")}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-muted">No external evidence links attached to this sample lead yet.</p>
        )}
      </div>
    </div>
  );
}
