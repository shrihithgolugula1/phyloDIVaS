// Legacy summaries used only dataset IDs, without an account boundary.
// Never consult or repopulate that store, including from late old-account responses.
// The viewer retains its session memory cache.
export async function readResultSummary() { return null; }
export async function cacheResultSummary() { }

export function clearResultCaches(...caches) {
    for (const cache of caches) cache.clear();
    try {
        const request = indexedDB.deleteDatabase("phylodivas-derived-results-v1");
        request.onerror = () => {};
        request.onblocked = () => {};
    } catch { /* Disabled storage must not prevent result retrieval. */ }
}
