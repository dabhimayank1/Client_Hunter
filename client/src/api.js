import { getToken, clearToken } from "./auth";

const BASE = import.meta.env.VITE_API_BASE_URL || "";

export async function api(path, options = {}) {
  const token = getToken();
  const headers = {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(options.headers || {}),
  };

  const res = await fetch(`${BASE}${path}`, {
    ...options,
    headers,
  });

  if (res.status === 401) {
    clearToken();
    if (window.location.pathname !== "/login") {
      window.location.href = "/login";
    }
  }

  if (!res.ok) {
    const errorText = await res.text();
    let message = errorText;
    try {
      const parsed = JSON.parse(errorText);
      message = parsed?.error || parsed?.message || errorText;
    } catch {
      // not JSON
    }
    throw new Error(message || `HTTP ${res.status}`);
  }

  return res.status === 204 ? null : res.json();
}

export async function downloadLeadsCsv(priority, status, city, category, platform, dateRange, sortBy) {
  const params = new URLSearchParams();
  if (priority) params.set("priority", priority);
  if (status) params.set("status", status);
  if (city) params.set("city", city);
  if (category) params.set("category", category);
  if (platform) params.set("platform", platform);
  if (dateRange) params.set("dateRange", dateRange);
  if (sortBy) params.set("sortBy", sortBy);

  const qs = params.toString();
  const token = getToken();
  const headers = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const res = await fetch(`${BASE}/api/leads/export${qs ? `?${qs}` : ""}`, {
    headers,
  });

  if (res.status === 401) {
    clearToken();
    if (window.location.pathname !== "/login") {
      window.location.href = "/login";
    }
    throw new Error("Session expired. Please log in again.");
  }

  if (!res.ok) {
    const errText = await res.text();
    throw new Error(errText || `Failed to export CSV (HTTP ${res.status})`);
  }

  const blob = await res.blob();
  const downloadUrl = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = downloadUrl;
  a.download = "leads.csv";
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.URL.revokeObjectURL(downloadUrl);
}

export const exportLeadsCsv = downloadLeadsCsv;
