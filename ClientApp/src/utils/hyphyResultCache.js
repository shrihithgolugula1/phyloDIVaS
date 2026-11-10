// Cache derived results only. Credentials and request headers never enter this store.
let connection;
const database = () => connection ||= new Promise((resolve, reject) => {
    const request = indexedDB.open("phylodivas-derived-results-v1", 1);
    request.onupgradeneeded = () => request.result.createObjectStore("summaries");
    request.onsuccess = () => {
        request.result.onversionchange = () => { request.result.close(); connection = null; };
        resolve(request.result);
    };
    request.onerror = () => { connection = null; reject(request.error); };
});
export async function readResultSummary(key) {
    let db;
    try {
        db = await database();
        return await new Promise((resolve, reject) => {
            const request = db.transaction("summaries").objectStore("summaries").get(key);
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(request.error);
        });
    } catch { return null; }
}
export async function cacheResultSummary(key, value) {
    let db;
    try {
        db = await database();
        await new Promise((resolve, reject) => {
            const transaction = db.transaction("summaries", "readwrite");
            transaction.objectStore("summaries").put(value, key);
            transaction.oncomplete = resolve;
            transaction.onerror = () => reject(transaction.error);
        });
    } catch { /* A full or disabled browser cache must not prevent retrieval. */ }
}
