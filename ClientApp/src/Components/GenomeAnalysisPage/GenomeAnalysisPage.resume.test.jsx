import React from 'react';
import {afterEach, beforeEach, expect, it, vi} from 'vitest';
import {cleanup, fireEvent, render, screen, waitFor} from '@testing-library/react';
import {MemoryRouter} from 'react-router-dom';
import GenomeAnalysisPage from './GenomeAnalysisPage';
const historyId = '1234567890abcdef';
const attach = status => ({historyId,orthoFinderJobId:'history-scan',genomes:[],status,workflowStatus:status,workflowInvocationId:'previous-invocation'});
let state, attachedState, request;
const table='HOG\tOG\tGene Tree Parent Clade\tSpecies A\tSpecies B\nN0.HOG0000001\tOG1\tn0\tXP_1\tXP_2\nN0.HOG0000002\tOG2\tn0\tXP_3\tXP_4\n';
beforeEach(() => {
    localStorage.clear();state='workflow_failed';attachedState='workflow_failed';
    request=vi.fn(async url => {
        const path=String(url);
        const data=path.includes('/attach') ? attach(attachedState)
            : path.includes('/status') ? {stage:state,detail:state,jobs:{running:state==='workflow_running'?2:0}}
            : path.includes('/orthofinder/outputs/') ? {singleCopyDatasetId:'existing-filtered-table'}
            : path.includes('/download') ? table
            : path.includes('hyphy-outputs') ? {hyphyJsonOutputs:[],hyphyMarkdownOutputs:[]}
            : {};
        return {ok:true,status:200,text:async()=>typeof data==='string'?data:JSON.stringify(data)};
    });
    vi.stubGlobal('fetch',request);
});
afterEach(()=>{cleanup();vi.unstubAllGlobals();});
async function resume() {
    render(<MemoryRouter><GenomeAnalysisPage/></MemoryRouter>);
    fireEvent.click(screen.getByRole('button',{name:'Resume Existing',exact:true}));
    fireEvent.change(screen.getByPlaceholderText('Enter a history ID or Galaxy history URL'),{target:{value:historyId}});
    fireEvent.change(screen.getByPlaceholderText('Paste the key that created this history'),{target:{value:'test-key'}});
    fireEvent.click(screen.getByRole('button',{name:'Load Existing History',exact:true}));
}
it('recovers selectable rows from an existing failed invocation without submitting jobs',async()=>{
    await resume();
    await screen.findByText('N0.HOG0000001');
    await screen.findByText('N0.HOG0000002');
    expect(screen.queryByText(/No strict 1:1/)).toBeNull();
    expect(request.mock.calls.some(([url])=>String(url).includes('/download'))).toBe(true);
    expect(request.mock.calls.some(([url])=>String(url).includes('/analyze-'))).toBe(false);
});
it('loads rows when status discovers failure after a fast running attach',async()=>{
    attachedState='workflow_running';
    await resume();
    await screen.findByText('N0.HOG0000001');
    expect(screen.queryByText(/No strict 1:1/)).toBeNull();
});
it('keeps active workflows locked without exposing another selection form',async()=>{
    attachedState=state='workflow_running';
    await resume();
    await screen.findByText('Step 4: Running Selection Analysis');
    expect(screen.queryByText('Step 3: Select Orthologous Gene Families')).toBeNull();
});
it('shows the completed viewer rather than a retry form for completed histories',async()=>{
    attachedState=state='workflow_complete';
    await resume();
    await screen.findByRole('heading',{name:'Selection results dashboard'});
    expect(screen.queryByText('Step 3: Select Orthologous Gene Families')).toBeNull();
});

for (const stage of ['orthogroups_ready', 'hyphy_ready', 'post_mafft_ready']) {
    it(`reopens selection for inactive ${stage} histories`, async () => {
        attachedState=state=stage;
        await resume();
        await screen.findByText('N0.HOG0000001');
        fireEvent.click(screen.getByRole('button',{name:'Select filtered'}));
        const submit=screen.getByRole('button',{name:/Analyze Selected/});
        expect(submit.disabled).toBe(false);
    });
}
