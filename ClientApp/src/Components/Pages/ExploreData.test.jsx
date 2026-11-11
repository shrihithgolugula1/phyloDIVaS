import React from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import GenomeSearchPage from './GenomeSearchPage/GenomeSearchPage';
import GeneSearchPage from './GeneSearchPage/GeneSearchPage';
const deferred = () => {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
};
const response = data => ({ ok: true, text: async () => JSON.stringify(data), json: async () => data });
const records = (kind, name) => response(kind === 'genome' ? { suggestions: [{ scientific: name, accession: 'GCF_1' }] } : { data: [{ geneName: name, geneId: '123' }] });
const search = value => {
    fireEvent.change(screen.getByRole('textbox'), { target: { value } });
    fireEvent.submit(screen.getByRole('textbox').closest('form'));
};
const settle = async callback => { await act(async () => { callback(); }); };
beforeEach(() => { vi.resetAllMocks(); sessionStorage.clear(); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
for (const kind of ['genome', 'gene']) {
    const mount = () => render(<MemoryRouter initialEntries={['/GeneSearch?taxon=human']}>{kind === 'genome' ? <GenomeSearchPage /> : <GeneSearchPage />}</MemoryRouter>);
    const setup = () => {
        const request = vi.fn().mockResolvedValue(response({ suggestions: [] }));
        vi.stubGlobal('fetch', request);
        return request;
    };
    it.each(['success', 'failure'])(`${kind} ignores stale %s without clearing current loading`, async outcome => {
        const request = setup(), old = deferred(), current = deferred();
        request.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise);
        mount(); search('old'); search('current');
        await settle(() => outcome === 'success' ? old.resolve(records(kind, 'Old result')) : old.reject(new Error('Old error')));
        expect(screen.getByRole('status')).toBeTruthy();
        expect(screen.queryByRole('alert')).toBeNull();
        expect(screen.queryByText('Old result')).toBeNull();
        await settle(() => current.resolve(records(kind, 'Current result')));
        expect(screen.getByText('Current result')).toBeTruthy();
    });
    it(`${kind} preserves newer results when old response arrives last`, async () => {
        const request = setup(), old = deferred(), current = deferred();
        request.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise);
        mount(); search('old'); search('current');
        await settle(() => current.resolve(records(kind, 'Current result')));
        await settle(() => old.resolve(records(kind, 'Old result')));
        expect(screen.getByText('Current result')).toBeTruthy();
        expect(screen.queryByText('Old result')).toBeNull();
    });
    it(`${kind} shows download errors while preserving results and clears on retry`, async () => {
        const request = setup();
        request.mockResolvedValueOnce(records(kind, 'Download result'));
        mount(); search('query'); await screen.findByText('Download result');
        request.mockResolvedValueOnce({ ok: false, status: 503 });
        fireEvent.click(screen.getByRole('button', { name: 'Download data' }));
        await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('HTTP 503'));
        request.mockRejectedValueOnce(new Error('Offline'));
        fireEvent.click(screen.getByRole('button', { name: 'Download data' }));
        expect(screen.queryByRole('alert')).toBeNull();
        await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Offline'));
        expect(screen.getByText('Download result')).toBeTruthy();
    });
}
it('refetches genes when organism changes with the same query', async () => {
    const old = deferred(), current = deferred();
    const request = vi.fn().mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise);
    vi.stubGlobal('fetch', request);
    const Switch = () => { const navigate = useNavigate(); return <button onClick={() => navigate('/GeneSearch?taxon=mouse')}>Switch</button>; };
    render(<MemoryRouter initialEntries={['/GeneSearch?taxon=human']}><GeneSearchPage /><Switch /></MemoryRouter>);
    search('TP53'); fireEvent.click(screen.getByText('Switch'));
    expect(request.mock.calls[1][0]).toContain('taxon=mouse');
    await settle(() => current.resolve(records('gene', 'Mouse gene')));
    await settle(() => old.resolve(records('gene', 'Human gene')));
    expect(screen.getByText('Mouse gene')).toBeTruthy();
    expect(screen.queryByText('Human gene')).toBeNull();
});
it('keeps exact human primary and puts related species in a closed expandable control', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ suggestions: [
        { scientific: 'Homo sapiens', common: 'human', accession: 'GCF_1' },
        { scientific: 'Pan troglodytes', common: 'chimpanzee', accession: 'GCF_2' },
    ] })));
    render(<MemoryRouter><GenomeSearchPage /></MemoryRouter>);
    search('human');
    const summary = await screen.findByText('Related results (1 assembly)');
    const details = summary.closest('details');
    expect(details.open).toBe(false);
    expect(screen.getByText('Homo sapiens').closest('details')).toBeNull();
    expect(screen.getByText('Pan troglodytes').closest('details')).toBe(details);
    fireEvent.click(summary);
    expect(details.open).toBe(true);
});
