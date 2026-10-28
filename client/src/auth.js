const TOKEN_KEY = "client_hunter_jwt";

export function getToken() {
  try {
    return sessionStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

export function setToken(token) {
  try {
    if (token) {
      sessionStorage.setItem(TOKEN_KEY, token);
    } else {
      sessionStorage.removeItem(TOKEN_KEY);
    }
  } catch (e) {
    console.error("Failed to set token in sessionStorage:", e);
  }
}

export function clearToken() {
  try {
    sessionStorage.removeItem(TOKEN_KEY);
  } catch (e) {
    console.error("Failed to clear token from sessionStorage:", e);
  }
}
