function getProxyConfig() {
  const proxyUrl = process.env.PROXY_URL || process.env.HTTP_PROXY || process.env.HTTPS_PROXY;
  if (!proxyUrl || typeof proxyUrl !== "string" || !proxyUrl.trim()) return null;

  try {
    const raw = proxyUrl.trim();
    const url = raw.includes("://") ? new URL(raw) : new URL(`http://${raw}`);
    const server = `${url.protocol}//${url.hostname}:${url.port || (url.protocol === "https:" ? 443 : 80)}`;
    let auth = null;
    if (url.username || url.password) {
      auth = {
        username: decodeURIComponent(url.username || ""),
        password: decodeURIComponent(url.password || ""),
      };
    }
    return { server, auth };
  } catch (err) {
    console.error("[proxy-helper] Failed to parse proxy URL:", err.message);
    return null;
  }
}

function getProxyArgs() {
  const config = getProxyConfig();
  if (!config) return [];
  return [`--proxy-server=${config.server}`];
}

async function attachProxyAuth(page) {
  const config = getProxyConfig();
  if (config && config.auth && page && typeof page.authenticate === "function") {
    try {
      await page.authenticate(config.auth);
    } catch (err) {
      console.error("[proxy-helper] Failed to authenticate proxy:", err.message);
    }
  }
}

module.exports = {
  getProxyConfig,
  getProxyArgs,
  attachProxyAuth,
};
