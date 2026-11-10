import { API_BASES, apiUrl } from "./config";

const base = apiUrl(API_BASES.searchHistory, "/api/SearchHistory");

function localKey(userId) {
  return `phylodivas.searchHistory.${userId || "guest"}`;
}

function readLocalHistory(userId) {
  try {
    return JSON.parse(localStorage.getItem(localKey(userId)) || "[]");
  } catch {
    return [];
  }
}

function writeLocalHistory(userId, history) {
  localStorage.setItem(localKey(userId), JSON.stringify(history));
}


async function getSearchHistory(userId){
  try {
    const response = await fetch(`${base}/search/getSearchHistory`,{
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(userId)
    });
    if (!response.ok) throw new Error("History service unavailable");
    const history = await response.json();
    if (Array.isArray(history)) return history;
  } catch (error) {
    console.warn("Using browser-local search history:", error.message);
  }
  return readLocalHistory(userId);
}

export {getSearchHistory, saveSearch, deleteSearch};

async function saveSearch(userId, searchType, queryText, resolvedTaxId, resolvedGeneId, resolvedAssemblyId = "-1"){
  try {
    const response = await fetch(`${base}/search/AddSearch?searchType=${encodeURIComponent(searchType)}&queryText=${encodeURIComponent(queryText)}&resolvedTaxId=${resolvedTaxId}&resolvedGeneId=${resolvedGeneId}&resolvedAssemblyId=${encodeURIComponent(resolvedAssemblyId)}`,{
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(userId)
    });
    if (!response.ok) throw new Error("History service unavailable");
    const responseText = await response.text();
    if (/exception|connectionstring|npgsql/i.test(responseText)) {
      throw new Error("History database unavailable");
    }
    return responseText;
  } catch (error) {
    console.warn("Saving search in browser-local history:", error.message);
    const history = readLocalHistory(userId);
    const nextId = history.reduce((max, item) => Math.max(max, Number(item.search_id) || 0), 0) + 1;
    history.push({
      search_id: nextId,
      search_type: searchType,
      query_text: queryText,
      resolved_tax_id: resolvedTaxId === -1 ? null : resolvedTaxId,
      resolved_gene_id: resolvedGeneId === -1 ? null : resolvedGeneId,
      resolved_assembly_id: resolvedAssemblyId === "-1" ? null : resolvedAssemblyId,
      searched_at: new Date().toISOString()
    });
    writeLocalHistory(userId, history);
    return "Search saved locally";
  }
}

// export default saveSearch;


async function deleteSearch(userId,searchId){
  try {
    const response = await fetch(`${base}/search/DeleteSearch?userId=${encodeURIComponent(userId)}&searchHistoryId=${encodeURIComponent(searchId)}`, {
      method: "DELETE",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) throw new Error("History service unavailable");
    writeLocalHistory(userId, readLocalHistory(userId).filter(item => item.search_id !== searchId));
    return await response.text();
  } catch (error) {
    console.warn("Deleting search from browser-local history:", error.message);
    writeLocalHistory(userId, readLocalHistory(userId).filter(item => item.search_id !== searchId));
    return "Search deleted locally";
  }
}
