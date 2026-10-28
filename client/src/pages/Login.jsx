import { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api";
import { getToken, setToken } from "../auth";

export default function Login() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (getToken()) {
      navigate("/", { replace: true });
    }
  }, [navigate]);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError(null);

    if (!email.trim() || !password) {
      setError("Please enter both email and password.");
      return;
    }

    setSubmitting(true);
    try {
      const res = await api("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ email: email.trim(), password }),
      });

      if (res?.token) {
        setToken(res.token);
        navigate("/", { replace: true });
      } else {
        setError("Invalid response from server. No token received.");
      }
    } catch (err) {
      setError(err.message || "Login failed. Please check your credentials.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="login-page-root">
      <div className="login-card">
        <div className="login-header">
          <div className="brand-logo-mark" style={{ margin: "0 auto 12px" }}>
            <span className="logo-symbol">🏹</span>
          </div>
          <h2>AI Client Hunter</h2>
          <p className="login-subtitle">Sign in to access your B2B Client Engine</p>
        </div>

        {error && (
          <div className="login-error-alert" role="alert">
            <span className="error-icon">⚠️</span>
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="login-form">
          <div className="form-group">
            <label htmlFor="login-email">Email Address</label>
            <input
              id="login-email"
              type="email"
              className="text-input"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="admin@example.com"
              required
              autoFocus
              autoComplete="username"
            />
          </div>

          <div className="form-group" style={{ marginTop: 16 }}>
            <label htmlFor="login-password">Password</label>
            <input
              id="login-password"
              type="password"
              className="text-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••••••"
              required
              autoComplete="current-password"
            />
          </div>

          <button
            type="submit"
            className="btn btn-primary"
            disabled={submitting}
            style={{ width: "100%", marginTop: 24, padding: "12px 16px" }}
          >
            {submitting ? "Signing in..." : "Sign In ➔"}
          </button>
        </form>

        <div className="login-footer-hint">
          <span>Protected by JWT & Rate Limiting • Enterprise Single Tenant</span>
        </div>
      </div>
    </div>
  );
}
