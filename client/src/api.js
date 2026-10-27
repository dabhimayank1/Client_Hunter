const BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000";

export async function api(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(options.headers || {}),
    },
  });

  if (!res.ok) {
    const errorText = await res.text();
    throw new Error(errorText || `HTTP ${res.status}`);
  }

  return res.status === 204 ? null : res.json();
}

export function getExportUrl(priority, status, city, category, platform, dateRange, sortBy) {
  const params = new URLSearchParams();
  if (priority) params.set("priority", priority);
  if (status) params.set("status", status);
  if (city) params.set("city", city);
  if (category) params.set("category", category);
  if (platform) params.set("platform", platform);
  if (dateRange) params.set("dateRange", dateRange);
  if (sortBy) params.set("sortBy", sortBy);
  const qs = params.toString();
  return `${BASE}/api/leads/export${qs ? `?${qs}` : ""}`;
}

