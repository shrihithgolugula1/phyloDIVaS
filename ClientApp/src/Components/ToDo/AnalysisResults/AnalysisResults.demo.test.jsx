import React from 'react';
import {expect,it,vi} from 'vitest';
import {render,screen,cleanup,fireEvent} from '@testing-library/react';
import {MemoryRouter} from 'react-router-dom';
import DemoResultsPage from '../../Pages/DemoResultsPage/DemoResultsPage';
it('uses the live dashboard for every demo gene, original reports, figures and local downloads',async()=>{
 const original=globalThis.fetch;const request=vi.fn(url=>{if(!String(url).startsWith('/demo/bats/'))throw new Error('Demo must not request remote APIs');return original(url);});
 vi.stubGlobal('fetch',request);const blobs=[];
 const create=vi.spyOn(URL,'createObjectURL').mockImplementation(blob=>{blobs.push(blob);return 'blob:demo-test';});
 const revoke=vi.spyOn(URL,'revokeObjectURL').mockImplementation(()=>{}),click=vi.spyOn(HTMLAnchorElement.prototype,'click').mockImplementation(()=>{});
 try {
  render(<MemoryRouter><DemoResultsPage/></MemoryRouter>);
  await screen.findByRole('heading',{name:'OrthoFinder results'},{timeout:15000});
  await screen.findByRole('heading',{name:'Selection results dashboard'},{timeout:30000});
  expect(screen.getByText('Genes requested',{exact:true}).parentElement.textContent).toContain('9,978');
  const table=screen.getByRole('table',{name:'OrthoFinder orthogroup membership'});expect(table.querySelectorAll('tbody tr')).toHaveLength(25);
  expect(screen.queryByText('groups submitted in the historical run',{exact:true})).toBeNull();expect(screen.queryByRole('combobox',{name:'Orthogroup table'})).toBeNull();
  const disclosure=screen.getByText('1:1 ortholog table (9,978 orthogroups)',{exact:true});disclosure.click();expect(table.checkVisibility()).toBe(false);disclosure.click();
  expect(screen.getByRole('link',{name:'Download raw N0 table'}).getAttribute('download')).not.toBeNull();
  expect(screen.getByRole('img',{name:/OrthoFinder species phylogeny/}).querySelectorAll('image')).toHaveLength(8);
  expect(screen.getByRole('combobox',{name:'Gene-count scale'}).value).toBe('log');
  expect(screen.getByRole('img',{name:'Top BUSTED genes ranked by selection evidence'})).toBeTruthy();
  expect(screen.getByRole('button',{name:'Calculate enrichment'}).disabled).toBe(false);
  screen.getByRole('button',{name:'Download table',exact:true}).click();expect((await blobs[0].text()).split('\n')).toHaveLength(9979);
  fireEvent.change(screen.getByRole('searchbox',{name:'Gene, alias, or accession'}),{target:{value:'ABCD3'}});
  screen.getByRole('button',{name:'View detailed report for ABCD3 (N0.HOG0004322)'}).click();
  await screen.findByRole('heading',{name:'Native HyPhy summaries from Galaxy'},{timeout:15000});
  expect(await screen.findByText('BUSTED summary',{exact:true})).toBeTruthy();expect(screen.getByText('aBSREL summary',{exact:true})).toBeTruthy();expect(screen.getByText('MEME summary',{exact:true})).toBeTruthy();
  for(const method of ['BUSTED','aBSREL','MEME']) {
   screen.getByRole('tab',{name:method,exact:true}).click();await screen.findByText(`Native HyPhy ${method} result`);
   if(method==='MEME')expect(screen.getByRole('img',{name:'MEME Manhattan plot by codon position'})).toBeTruthy();
   if(method==='aBSREL')expect(screen.getByRole('img',{name:'aBSREL gene tree with selected branches highlighted'})).toBeTruthy();
   screen.getByRole('button',{name:'Report data (JSON)',exact:true}).click();screen.getByRole('button',{name:'Markdown',exact:true}).click();
  }
  expect(blobs).toHaveLength(7);expect(JSON.parse(await blobs[1].text()).input['number of sequences']).toBe(8);
  expect(await blobs[2].text()).not.toContain('Generated teaching summary');
  fireEvent.change(screen.getByRole('searchbox',{name:'Find a gene'}),{target:{value:'EIF4G2'}});
  screen.getByRole('option',{name:/EIF4G2/}).click();await screen.findByRole('heading',{name:'EIF4G2',exact:true},{timeout:15000});
  screen.getByRole('tab',{name:'MEME',exact:true}).click();expect(await screen.findByRole('img',{name:'MEME Manhattan plot by codon position'})).toBeTruthy();
  expect(request.mock.calls.filter(([url])=>String(url).includes('/reports/batch-')).length).toBeLessThanOrEqual(2);
  expect(request.mock.calls.every(([url])=>String(url).startsWith('/demo/bats/'))).toBe(true);
 } finally {cleanup();create.mockRestore();revoke.mockRestore();click.mockRestore();vi.unstubAllGlobals();}
},60000);
