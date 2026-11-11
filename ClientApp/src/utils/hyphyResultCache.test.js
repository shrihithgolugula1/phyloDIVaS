import { afterEach, expect, it, vi } from "vitest";
import { cacheResultSummary, clearResultCaches, readResultSummary } from "./hyphyResultCache";
afterEach(() => vi.unstubAllGlobals());
it("clears summary/raw/markdown on key switch and logout; late promises cannot repopulate", async () => {
    vi.stubGlobal("indexedDB", { deleteDatabase: vi.fn(() => ({})) });
    const caches = [new Map(), new Map(), new Map()];
    let finish;
    const old = new Promise(resolve => { finish = resolve; });
    caches.forEach(cache => cache.set("same-dataset", old));
    clearResultCaches(...caches);
    caches.forEach(cache => expect(cache.size).toBe(0));
    caches.forEach(cache => cache.set("same-dataset", Promise.resolve("dummy-b")));
    finish("dummy-a");
    await old;
    for (const cache of caches) expect(await cache.get("same-dataset")).toBe("dummy-b");
    clearResultCaches(...caches);
    caches.forEach(cache => expect(cache.size).toBe(0));
});
it("never reads legacy summaries or persists late old-account results", async () => {
    const open = vi.fn();
    vi.stubGlobal("indexedDB", { open, deleteDatabase: vi.fn(() => ({})) });
    await cacheResultSummary("dataset", { account: "dummy-a" });
    expect(await readResultSummary("dataset")).toBeNull();
    clearResultCaches(new Map());
    await cacheResultSummary("dataset", { account: "dummy-a-late" });
    expect(await readResultSummary("dataset")).toBeNull();
    expect(open).not.toHaveBeenCalled();
});
it("clears memory when browser storage is disabled", () => {
    vi.stubGlobal("indexedDB", { deleteDatabase() { throw new Error("disabled"); } });
    const cache = new Map([["dataset", "private-summary"]]);
    expect(() => clearResultCaches(cache)).not.toThrow();
    expect(cache.size).toBe(0);
});
