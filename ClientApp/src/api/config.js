const trimTrailingSlash = (value) => value.replace(/\/+$/, "");

export const API_BASES = {
  analysis: trimTrailingSlash(import.meta.env.VITE_ANALYSIS_API_URL || "http://localhost:7040"),
  search: trimTrailingSlash(import.meta.env.VITE_SEARCH_API_URL || "https://localhost:7039"),
  searchHistory: trimTrailingSlash(import.meta.env.VITE_SEARCH_HISTORY_API_URL || "https://localhost:7227"),
  registration: trimTrailingSlash(import.meta.env.VITE_REGISTRATION_API_URL || "https://localhost:7072"),
  account: trimTrailingSlash(import.meta.env.VITE_ACCOUNT_API_URL || "https://localhost:7017"),
  login: trimTrailingSlash(import.meta.env.VITE_LOGIN_API_URL || "https://localhost:7149"),
  feedback: trimTrailingSlash(import.meta.env.VITE_FEEDBACK_API_URL || "https://localhost:7183"),
};

export function apiUrl(base, path) {
  return `${trimTrailingSlash(base)}/${path.replace(/^\/+/, "")}`;
}
