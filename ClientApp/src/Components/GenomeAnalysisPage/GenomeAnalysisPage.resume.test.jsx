import React from 'react';
import {afterEach, beforeEach, expect, it, vi} from 'vitest';
import {cleanup, fireEvent, render, screen, waitFor} from '@testing-library/react';
import {MemoryRouter} from 'react-router-dom';
import GenomeAnalysisPage from './GenomeAnalysisPage';
import * as api from '../../api/dndsApi';
vi.mock('../../api/dndsApi', async importOriginal => {
    const original = await importOriginal();
    return Object.fromEntries(Object.keys(original).map(key => [key, vi.fn()]));
});
vi.mock('../OrthologousGroupSelector/OrthologousGroupSelector', () => ({default: props => <div data-testid="selector">{props.isLoading ? 'Loading groups' : `${props.groups.length} selectable groups`}<button disabled={!props.canAnalyze || props.isAnalyzing}>Submit recovered groups</button></div>}));
vi.mock('../ToDo/AnalysisResults/AnalysisResults', () => ({default: () => <div>Completed results viewer</div>}));
const historyId = '1234567890abcdef';
const attach = status => ({historyId,orthoFinderJobId:'history-scan',genomes:[],status,workflowStatus:status,workflowInvocationId:'previous-invocation'});
beforeEach(() => {
    vi.resetAllMocks();
    api.getOrthoFinderOutputs.mockResolvedValue({singleCopyDatasetId:'existing-filtered-table'});
    api.downloadOrthoFinderOutput.mockResolvedValue('HOG\tOG\tGene Tree Parent Clade\tSpecies A\tSpecies B\nN0.HOG0000001\tOG1\tn0\tXP_1\tXP_2\nN0.HOG0000002\tOG2\tn0\tXP_3\tXP_4\n');
    api.getHyphyOutputs.mockResolvedValue({hyphyJsonOutputs:[],hyphyMarkdownOutputs:[]});
});
afterEach(cleanup);
async function resume() {
    render(<MemoryRouter><GenomeAnalysisPage/></MemoryRouter>);
    fireEvent.click(screen.getByRole('button',{name:'Resume Existing',exact:true}));
    fireEvent.change(screen.getByPlaceholderText('Enter a history ID or Galaxy history URL'),{target:{value:historyId}});
    fireEvent.change(screen.getByPlaceholderText('Paste the key that created this history'),{target:{value:'test-key'}});
    fireEvent.click(screen.getByRole('button',{name:'Load Existing History',exact:true}));
}
it('recovers selectable rows from an existing failed invocation without submitting jobs',async()=>{
    api.resumeGenomeAnalysis.mockResolvedValue(attach('workflow_failed'));
    api.inspectGenomeAnalysis.mockResolvedValue({stage:'workflow_failed',detail:'Split failed',jobs:{}});
    await resume();
    await screen.findByText('2 selectable groups');
    expect(screen.getByRole('button',{name:'Submit recovered groups'}).disabled).toBe(false);
    expect(api.downloadOrthoFinderOutput).toHaveBeenCalledWith('existing-filtered-table',expect.anything());
    expect(api.analyzeMultipleGroups).not.toHaveBeenCalled();
    expect(api.analyzeAllGroups).not.toHaveBeenCalled();
});
it('loads rows when status discovers failure after a fast running attach',async()=>{
    api.resumeGenomeAnalysis.mockResolvedValue(attach('workflow_running'));
    api.inspectGenomeAnalysis.mockResolvedValue({stage:'workflow_failed',detail:'Split failed',jobs:{}});
    await resume();
    await screen.findByText('2 selectable groups');
    expect(screen.getByRole('button',{name:'Submit recovered groups'}).disabled).toBe(false);
});
it('keeps active workflows locked without exposing another selection form',async()=>{
    api.resumeGenomeAnalysis.mockResolvedValue(attach('workflow_running'));
    api.inspectGenomeAnalysis.mockResolvedValue({stage:'workflow_running',detail:'Galaxy is running',jobs:{running:2}});
    await resume();
    await screen.findByText('Step 4: Running Selection Analysis');
    expect(screen.queryByTestId('selector')).toBeNull();
    expect(api.analyzeMultipleGroups).not.toHaveBeenCalled();
});
it('shows the completed viewer rather than a retry form for completed histories',async()=>{
    api.resumeGenomeAnalysis.mockResolvedValue(attach('workflow_complete'));
    api.inspectGenomeAnalysis.mockResolvedValue({stage:'workflow_complete',detail:'Complete',jobs:{}});
    await resume();
    await screen.findByText('Completed results viewer');
    expect(screen.queryByTestId('selector')).toBeNull();
});
