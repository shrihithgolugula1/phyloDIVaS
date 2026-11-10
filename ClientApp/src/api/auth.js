import { API_BASES, apiUrl } from "./config";

const base = API_BASES.login;

export function getValidIdToken() {
  const token = sessionStorage.getItem("idToken");
  if (!token) return null;

  const parts = token.split(".");
  if (parts.length !== 3) {
    sessionStorage.removeItem("accessToken");
    sessionStorage.removeItem("idToken");
    sessionStorage.removeItem("refreshToken");
    return null;
  }

  try {
    const payload = JSON.parse(atob(parts[1].replace(/-/g, "+").replace(/_/g, "/")));
    if (payload.exp && payload.exp * 1000 <= Date.now()) {
      sessionStorage.removeItem("accessToken");
      sessionStorage.removeItem("idToken");
      sessionStorage.removeItem("refreshToken");
      return null;
    }
  } catch {
    sessionStorage.removeItem("accessToken");
    sessionStorage.removeItem("idToken");
    sessionStorage.removeItem("refreshToken");
    return null;
  }

  return token;
}

export async function login(username, password) {
  const r = await fetch(apiUrl(base, "/api/auth/login"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password })
  });
  if (!r.ok) {
    const payload = await r.json().catch(() => null);
    throw new Error(payload?.error?.message || "Login failed. Please try again.");
  }
  return r.json(); // { accessToken, idToken, refreshToken, expiresIn }
}

export async function me(token) {
  const r = await fetch(apiUrl(base, "/api/auth/me"), {
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!r.ok) throw new Error("Your session could not be verified.");
  return r.json();
}

export async function refresh(refreshToken) {
  const r = await fetch(apiUrl(base, "/api/auth/refresh"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken })
  });
  if (!r.ok) throw new Error("Refresh failed");
  return r.json();
}

export async function logout(accessToken, refreshToken) {
  const r = await fetch(apiUrl(base, "/api/auth/logout"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ accessToken, refreshToken })
  });
  if (!r.ok) throw new Error("Logout failed");
}
