import { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";

export default function ApprovalQueue() {
  const [drafts, setDrafts] = useState([]);
  const [stats, setStats] = useState(null);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState("draft"); // draft, approved, sent, rejected, all
  const [searchQuery, setSearchQuery] = useState("");
  const [editedBodies, setEditedBodies] = useState({});
  const [actionMessage, setActionMessage] = useState(null);
  const [actionError, setActionError] = useState(null);
  const [savingId, setSavingId] = useState(null);

  const loadData = async () => {
    try {
      setLoading(true);
      const [draftsData, statsData] = await Promise.all([
        api("/api/drafts"),
        api("/api/drafts/stats"),
      ]);
      setDrafts(draftsData || []);
      setStats(statsData || null);

      // Initialize edited bodies dictionary
      const bodies = {};
      (draftsData || []).forEach((d) => {
        bodies[d.id] = d.body;
      });
      setEditedBodies(bodies);
    } catch (err) {
      console.error("Failed to load drafts:", err);
      setActionError("Failed to load drafts. Please ensure backend is running.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const handleBodyChange = (id, newText) => {
    setEditedBodies((prev) => ({ ...prev, [id]: newText }));
  };

  const handleSaveEdit = async (draft) => {
    try {
      setSavingId(draft.id);
      setActionMessage(null);
      setActionError(null);

      const updatedBody = editedBodies[draft.id] ?? draft.body;
      const res = await api(`/api/drafts/${draft.id}`, {
        method: "PUT",
        body: JSON.stringify({
          body: updatedBody,
          subject: draft.subject,
          channel: draft.channel,
        }),
      });

      const wasApproved = draft.status === "approved";
      setActionMessage(
        wasApproved
          ? `Draft #${draft.id} updated! Guardrail active: status reset to "draft" for re-approval.`
          : `Draft #${draft.id} changes saved successfully.`
      );

      // Refresh list to reflect updated data
      await loadData();
    } catch (err) {
      setActionError(`Error updating draft: ${err.message}`);
    } finally {
      setSavingId(null);
    }
  };

  const handleApprove = async (id) => {
    try {
      setSavingId(id);
      setActionMessage(null);
      setActionError(null);

      await api(`/api/drafts/${id}/approve`, { method: "POST" });
      setActionMessage(`Draft #${id} approved! Ready for human outreach.`);
      await loadData();
    } catch (err) {
      setActionError(`Approval failed: ${err.message}`);
    } finally {
      setSavingId(null);
    }
  };

  const handleMarkSent = async (id) => {
    try {
      setSavingId(id);
      setActionMessage(null);
      setActionError(null);

      const res = await api(`/api/drafts/${id}/mark-sent`, { method: "POST" });
      setActionMessage(res.message || `Draft #${id} marked as sent. Activity logged.`);
      await loadData();
    } catch (err) {
      setActionError(`Could not mark sent: ${err.message}`);
    } finally {
      setSavingId(null);
    }
  };

  const handleReject = async (id) => {
    if (!window.confirm("Are you sure you want to discard this message draft?")) return;
    try {
      setSavingId(id);
      setActionMessage(null);
      setActionError(null);

      await api(`/api/drafts/${id}/reject`, { method: "POST" });
      setActionMessage(`Draft #${id} discarded.`);
      await loadData();
    } catch (err) {
      setActionError(`Discard failed: ${err.message}`);
    } finally {
      setSavingId(null);
    }
  };

  const getCleanPhone = (phone) => {
    if (!phone) return "";
    return phone.replace(/[^0-9]/g, "");
  };

  const filteredDrafts = drafts.filter((d) => {
    const matchesTab = activeTab === "all" ? true : d.status === activeTab;
    const matchesSearch =
      !searchQuery ||
      (d.lead?.name || "").toLowerCase().includes(searchQuery.toLowerCase()) ||
      (d.lead?.city || "").toLowerCase().includes(searchQuery.toLowerCase()) ||
      (d.channel || "").toLowerCase().includes(searchQuery.toLowerCase()) ||
      (d.body || "").toLowerCase().includes(searchQuery.toLowerCase());
    return matchesTab && matchesSearch;
  });

  return (
    <div className="approval-queue-page">
      {/* Page Header */}
      <div className="page-header">
        <div>
          <h2>📬 Outreach Approval Queue</h2>
          <p className="subtext">
            Human-in-the-loop review gate. Verify, edit, and approve drafts before sending manually.
          </p>
        </div>
        <button className="btn btn-secondary btn-sm" onClick={loadData} disabled={loading}>
          🔄 Refresh
        </button>
      </div>

      {/* Daily Rate-Limiter Tracker Banner */}
      {stats && (
        <div className={`daily-tracker-card ${stats.isLimitExceeded ? "tracker-limit-warning" : ""}`}>
          <div className="tracker-header">
            <div className="tracker-title-group">
              <span className="tracker-icon">🛡️</span>
              <div>
                <strong>Daily Outreach Safety Tracker</strong>
                <p className="tracker-subtext">
                  Pacing outreach protects sender reputation and prevents messaging limits.
                </p>
              </div>
            </div>
            <div className="tracker-counter">
              <span className="counter-current">{stats.sentToday}</span>
              <span className="counter-sep">/</span>
              <span className="counter-limit">{stats.dailyLimit}</span>
              <span className="counter-label">sent today</span>
            </div>
          </div>

          {/* Visual Progress Bar */}
          <div className="progress-bar-bg">
            <div
              className={`progress-bar-fill ${
                stats.sentToday >= stats.dailyLimit ? "fill-warning" : "fill-normal"
              }`}
              style={{
                width: `${Math.min(100, Math.round((stats.sentToday / stats.dailyLimit) * 100))}%`,
              }}
            ></div>
          </div>

          <div className="tracker-badges-row">
            <span className="tracker-pill">
              ⏳ <strong>{stats.pendingApproval}</strong> Pending Approval
            </span>
            <span className="tracker-pill">
              🚀 <strong>{stats.approvedReady}</strong> Ready to Send
            </span>
            <span className="tracker-pill">
              ✅ <strong>{stats.sentToday}</strong> Completed Today
            </span>
          </div>

          {stats.isLimitExceeded && (
            <div className="soft-limit-notice">
              ⚠️ <strong>Daily soft limit of 20 reached.</strong> It is recommended to pace outreach and resume sending tomorrow to maintain optimum response rates and avoid automated spam filters.
            </div>
          )}
        </div>
      )}

      {/* Action Notifications */}
      {actionMessage && <div className="alert alert-success">{actionMessage}</div>}
      {actionError && <div className="alert alert-error">{actionError}</div>}

      {/* Filter Tabs and Quick Search Bar */}
      <div className="queue-controls">
        <div className="tab-group">
          <button
            className={`tab-btn ${activeTab === "draft" ? "tab-active" : ""}`}
            onClick={() => setActiveTab("draft")}
          >
            Pending Review
            <span className="tab-count">
              {drafts.filter((d) => d.status === "draft").length}
            </span>
          </button>
          <button
            className={`tab-btn ${activeTab === "approved" ? "tab-active" : ""}`}
            onClick={() => setActiveTab("approved")}
          >
            Ready to Send
            <span className="tab-count">
              {drafts.filter((d) => d.status === "approved").length}
            </span>
          </button>
          <button
            className={`tab-btn ${activeTab === "sent" ? "tab-active" : ""}`}
            onClick={() => setActiveTab("sent")}
          >
            Sent
            <span className="tab-count">
              {drafts.filter((d) => d.status === "sent").length}
            </span>
          </button>
          <button
            className={`tab-btn ${activeTab === "rejected" ? "tab-active" : ""}`}
            onClick={() => setActiveTab("rejected")}
          >
            Discarded
            <span className="tab-count">
              {drafts.filter((d) => d.status === "rejected").length}
            </span>
          </button>
          <button
            className={`tab-btn ${activeTab === "all" ? "tab-active" : ""}`}
            onClick={() => setActiveTab("all")}
          >
            All Drafts
            <span className="tab-count">{drafts.length}</span>
          </button>
        </div>

        <div className="search-box">
          <input
            id="queue-search-input"
            name="q"
            type="text"
            className="input-search"
            placeholder="Search by business name, city, message..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            aria-label="Search outreach queue"
          />
        </div>
      </div>

      {/* Drafts List */}
      {loading ? (
        <div className="loading-state">Loading outreach queue...</div>
      ) : filteredDrafts.length === 0 ? (
        <div className="card empty-state">
          <p>No outreach drafts found in this tab.</p>
          {activeTab === "draft" && (
            <p className="subtext">
              Go to <Link to="/" className="btn-link">Leads CRM</Link>, pick a lead, and click <strong>"Generate AI Outreach Draft"</strong> to populate this queue.
            </p>
          )}
        </div>
      ) : (
        <div className="draft-cards-list">
          {filteredDrafts.map((draft) => {
            const currentBody = editedBodies[draft.id] ?? draft.body;
            const isDirty = currentBody !== draft.body;
            const phoneDigits = getCleanPhone(draft.lead?.phone);
            const waUrl = phoneDigits
              ? `https://wa.me/${phoneDigits}?text=${encodeURIComponent(currentBody)}`
              : null;
            const mailtoUrl = draft.lead?.email
              ? `mailto:${draft.lead.email}?subject=${encodeURIComponent(
                  draft.subject || "Website Opportunity"
                )}&body=${encodeURIComponent(currentBody)}`
              : null;

            return (
              <div
                key={draft.id}
                className={`card draft-card draft-card-${draft.status}`}
              >
                {/* Draft Card Header */}
                <div className="draft-card-header">
                  <div className="lead-identity">
                    <Link
                      to={`/leads/${draft.leadId}`}
                      className="draft-lead-title"
                      title="View lead profile"
                    >
                      {draft.lead?.name || `Lead #${draft.leadId}`}
                    </Link>
                    <div className="draft-lead-meta">
                      {draft.lead?.category && (
                        <span className="meta-tag">{draft.lead.category}</span>
                      )}
                      {draft.lead?.city && (
                        <span className="meta-tag">📍 {draft.lead.city}</span>
                      )}
                      {draft.lead?.phone && (
                        <span className="meta-tag">📞 {draft.lead.phone}</span>
                      )}
                      {draft.lead?.email && (
                        <span className="meta-tag">✉️ {draft.lead.email}</span>
                      )}
                      {draft.lead?.score !== undefined && (
                        <span className="score-pill">Score: {draft.lead.score}</span>
                      )}
                    </div>
                  </div>

                  <div className="draft-status-group">
                    <span className="badge badge-channel">
                      {draft.channel === "whatsapp" ? "💬 WhatsApp" : "✉️ Email"}
                    </span>
                    <span className={`badge badge-draft-${draft.status}`}>
                      {draft.status.toUpperCase()}
                    </span>
                  </div>
                </div>

                {/* Subject Line if present */}
                {draft.subject && (
                  <div className="draft-subject-row">
                    <strong>Subject:</strong> <span>{draft.subject}</span>
                  </div>
                )}

                {/* Editable Draft Body */}
                <div className="draft-body-container">
                  <label htmlFor={`draft-body-${draft.id}`} className="body-label">
                    Message Body (Review and customize before approving):
                  </label>
                  <textarea
                    id={`draft-body-${draft.id}`}
                    name={`draftBody_${draft.id}`}
                    rows={6}
                    className="draft-textarea"
                    value={currentBody}
                    onChange={(e) => handleBodyChange(draft.id, e.target.value)}
                    disabled={draft.status === "sent" || draft.status === "rejected"}
                  />
                  {isDirty && (
                    <span className="unsaved-badge">
                      ⚠️ You have unsaved edits in this text.
                    </span>
                  )}
                </div>

                {/* Actions Row */}
                <div className="draft-actions-bar">
                  <div className="left-actions">
                    {/* Save Edits button */}
                    {isDirty && (
                      <button
                        className="btn btn-secondary btn-sm"
                        onClick={() => handleSaveEdit(draft)}
                        disabled={savingId === draft.id}
                      >
                        💾 Save Changes
                      </button>
                    )}

                    {/* Discard Draft button */}
                    {draft.status !== "rejected" && draft.status !== "sent" && (
                      <button
                        className="btn btn-secondary btn-sm text-danger-btn"
                        onClick={() => handleReject(draft.id)}
                        disabled={savingId === draft.id}
                      >
                        🗑️ Discard Draft
                      </button>
                    )}
                  </div>

                  <div className="right-actions">
                    {/* Status: DRAFT */}
                    {draft.status === "draft" && (
                      <button
                        className="btn btn-primary"
                        onClick={() => handleApprove(draft.id)}
                        disabled={savingId === draft.id}
                      >
                        ✅ Approve Draft
                      </button>
                    )}

                    {/* Status: APPROVED (Ready to Send) */}
                    {draft.status === "approved" && (
                      <div className="dispatch-action-group">
                        {waUrl && (
                          <a
                            href={waUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="btn btn-whatsapp"
                          >
                            💬 Open WhatsApp
                          </a>
                        )}
                        {mailtoUrl && (
                          <a href={mailtoUrl} className="btn btn-secondary">
                            ✉️ Open Email
                          </a>
                        )}
                        {!waUrl && !mailtoUrl && (
                          <span className="subtext">No phone/email to launch chat</span>
                        )}

                        <button
                          className="btn btn-mark-sent"
                          onClick={() => handleMarkSent(draft.id)}
                          disabled={savingId === draft.id}
                          title="Click after you have manually sent the message"
                        >
                          ✓ Confirm Sent
                        </button>
                      </div>
                    )}

                    {/* Status: SENT */}
                    {draft.status === "sent" && (
                      <div className="sent-indicator">
                        <span className="text-good">
                          ✓ Sent to lead & activity logged in CRM
                        </span>
                      </div>
                    )}

                    {/* Status: REJECTED */}
                    {draft.status === "rejected" && (
                      <span className="cell-sub">Draft was discarded.</span>
                    )}
                  </div>
                </div>

                {/* Footer notes */}
                <div className="draft-card-footer">
                  <span className="timestamp-note">
                    Created: {new Date(draft.createdAt).toLocaleString()}
                    {draft.approvedAt &&
                      ` • Approved: ${new Date(draft.approvedAt).toLocaleString()} by ${
                        draft.approvedBy || "User"
                      }`}
                  </span>
                  <Link to={`/leads/${draft.leadId}`} className="lead-link-text">
                    Open Lead Profile →
                  </Link>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
