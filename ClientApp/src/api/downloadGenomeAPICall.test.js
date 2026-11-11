import { afterEach, expect, it, vi } from 'vitest';
import { getGenomeFasta, getGeneFasta } from './downloadGenomeAPICall';
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers(); });
it('rejects an HTTP 200 HTML error page rather than downloading it as ZIP', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ok:true,blob:async()=>new Blob(['<!doctype html><title>Server error</title>'])}));
    const create = vi.spyOn(URL, 'createObjectURL');
    await expect(getGenomeFasta('GCF_123')).rejects.toThrow('did not return a ZIP');
    expect(create).not.toHaveBeenCalled();
});
for (const [kind, download, id] of [['Genome', getGenomeFasta, 'GCF_123'], ['Gene', getGeneFasta, '123']]) {
    it(`${kind} rejects HTTP and network failures`, async () => {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce({ ok: false, status: 503 }).mockRejectedValueOnce(new Error('Offline')));
        const create = vi.spyOn(URL, 'createObjectURL');
        await expect(download(id)).rejects.toThrow('HTTP 503');
        await expect(download(id)).rejects.toThrow('Offline');
        expect(create).not.toHaveBeenCalled();
    });
    it(`${kind} downloads a checked blob and releases URL`, async () => {
        vi.useFakeTimers();
        const blob = new Blob(['PK\u0003\u0004test-archive']);
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, blob: async () => blob }));
        vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
        const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
        let anchor;
        vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function () { anchor = this; });
        await download(id);
        expect(anchor.download).toBe(`${kind}-${id}.zip`);
        expect(anchor.isConnected).toBe(false);
        expect(revoke).not.toHaveBeenCalled();
        vi.runAllTimers(); expect(revoke).toHaveBeenCalledWith('blob:test');
    });
}
