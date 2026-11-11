import React from 'react';
import { expect, it, vi } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DemoResultsPage from '../../Pages/DemoResultsPage/DemoResultsPage';

it('opens real saved reports and figures without API requests, and downloads the bundled demo locally', async () => {
    const request = vi.fn(() => Promise.reject(new Error('Demo must not require an API')));
    vi.stubGlobal('fetch', request);
    const blobs = [];
    const create = vi.spyOn(URL, 'createObjectURL').mockImplementation(blob => { blobs.push(blob); return 'blob:demo-test'; });
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    try {
        render(<MemoryRouter><DemoResultsPage /></MemoryRouter>);
        await screen.findByRole('heading', {name:'BUSTED p-value distribution'}, {timeout:10000});
        expect(screen.getByText('1 parsed (100%)', {exact:false})).toBeTruthy();
        expect(screen.getByText('One-gene bat example')).toBeTruthy();
        screen.getByRole('button', {name:'Download demo results',exact:true}).click();
        expect(blobs).toHaveLength(1);
        const bundle = JSON.parse(await blobs[0].text());
        expect(bundle.demo).toBe(true);
        expect(bundle.species).toHaveLength(8);
        expect(bundle.jsonOutputs).toHaveLength(3);
        expect(bundle.markdownOutputs).toHaveLength(3);
        screen.getByRole('tab', {name:'Gene-Specific Analysis',exact:true}).click();
        await screen.findByRole('heading', {name:'Native HyPhy summaries from Galaxy'});
        for (const method of ['BUSTED','aBSREL','MEME']) {
            screen.getByRole('tab', {name:method,exact:true}).click();
            await screen.findByText(`Native HyPhy ${method} result`);
            screen.getByRole('button', {name:'JSON',exact:true}).click();
            screen.getByRole('button', {name:'Markdown',exact:true}).click();
        }
        expect(blobs).toHaveLength(7);
        expect(request).not.toHaveBeenCalled();
        expect(JSON.stringify(bundle)).not.toMatch(/\/data\/|\/Users\/|11ac94870d0bb33|[a-f0-9]{32}/);
    } finally {
        cleanup(); create.mockRestore(); revoke.mockRestore(); click.mockRestore(); vi.unstubAllGlobals();
    }
});
