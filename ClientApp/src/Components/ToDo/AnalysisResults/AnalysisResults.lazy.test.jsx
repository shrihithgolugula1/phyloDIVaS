import React from 'react';
import {expect,it,vi} from 'vitest';
import {render,screen,waitFor,cleanup} from '@testing-library/react';
import AnalysisResults from './AnalysisResults';
it('retrieves only one gene before showing a large history, and withholds genome-wide conclusions',async()=>{
 const request=vi.fn(async url=>({ok:true,status:200,text:async()=>JSON.stringify(String(url).includes('/download') ? {analysis:{info:'fixture'},input:{'number of sequences':8,'number of sites':10},'test results':{'p-value':0.5}} : {success:true,artifacts:[],speciesGenes:{}})}));
 vi.stubGlobal('fetch',request);
 const outputs=Array.from({length:100},(_,i)=>['BUSTED','aBSREL','MEME'].map(method=>({id:`lazy-${i}-${method}`,method,elementIdentifier:`N0.HOG${String(i).padStart(7,'0')}`,state:'ok'}))).flat();
 render(<AnalysisResults historyId="fixture-history" jobInfo={{state:'workflow_complete',galaxyApiKey:'dummy-test-only',hyphyJsonOutputs:outputs}}/>);
 await waitFor(()=>expect(request.mock.calls.filter(([url])=>String(url).includes('/download'))).toHaveLength(3), {timeout:5000});
 await screen.findByText(/Gene-first view/, {}, {timeout:5000});
 expect(screen.queryByText('BUSTED p-value distribution')).toBeNull();
 expect(screen.getByRole('button',{name:'Load full result family for genome-wide statistics'})).toBeTruthy();
 cleanup();
 vi.unstubAllGlobals();
});
