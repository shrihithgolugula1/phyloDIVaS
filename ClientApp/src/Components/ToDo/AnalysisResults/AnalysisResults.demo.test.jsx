import React from 'react';
import {expect,it,vi} from 'vitest';
import {render,screen,cleanup,fireEvent} from '@testing-library/react';
import {MemoryRouter} from 'react-router-dom';
import DemoResultsPage from '../../Pages/DemoResultsPage/DemoResultsPage';
it('loads OrthoFinder and genome-wide results with native reports and local downloads',async()=>{
 const original=globalThis.fetch;
 const request=vi.fn(url=>{if(!String(url).startsWith('/demo/bats/'))throw new Error('Demo must not request remote APIs');return original(url);});
 vi.stubGlobal('fetch',request);const blobs=[];
 const create=vi.spyOn(URL,'createObjectURL').mockImplementation(blob=>{blobs.push(blob);return 'blob:demo-test';});
 const revoke=vi.spyOn(URL,'revokeObjectURL').mockImplementation(()=>{});
 const click=vi.spyOn(HTMLAnchorElement.prototype,'click').mockImplementation(()=>{});
 try {
  render(<MemoryRouter><DemoResultsPage/></MemoryRouter>);
  await screen.findByRole('heading',{name:'OrthoFinder results'},{timeout:15000});
  expect(screen.getByRole('heading',{name:'Genome-wide selection results'})).toBeTruthy();
  expect(screen.getByText('3,177',{exact:true})).toBeTruthy();expect(screen.getByText('288',{exact:true})).toBeTruthy();
  const table=screen.getByRole('table',{name:'OrthoFinder orthogroup membership'});expect(table.querySelectorAll('tbody tr')).toHaveLength(25);
  expect(screen.getByRole('link',{name:'Download species tree (Newick)'}).getAttribute('href')).toBe('/demo/bats/species-tree.nwk');
  expect(screen.getByRole('link',{name:'Download raw N0 table'}).getAttribute('download')).not.toBeNull();
  const tree=screen.getByRole('img',{name:/OrthoFinder species phylogeny/});expect(tree.querySelectorAll('image')).toHaveLength(8);
  fireEvent.change(screen.getByRole('searchbox',{name:'Find orthogroup or accession'}),{target:{value:'N0.HOG0004322'}});expect(table.querySelectorAll('tbody tr')).toHaveLength(1);
  screen.getByRole('button',{name:'Download genome-wide results table'}).click();expect((await blobs[0].text()).split('\n')).toHaveLength(3178);
  screen.getByRole('button',{name:'Open ABCD3 reports'}).click();await screen.findByText('Native HyPhy BUSTED result',{}, {timeout:10000});
  for(const method of ['BUSTED','aBSREL','MEME']) {
   screen.getByRole('button',{name:method,exact:true}).click();await screen.findByText(`Native HyPhy ${method} result`);
   screen.getByRole('button',{name:'JSON',exact:true}).click();screen.getByRole('button',{name:'Markdown',exact:true}).click();
  }
  expect(blobs).toHaveLength(7);expect(JSON.parse(await blobs[1].text()).input['number of sequences']).toBe(8);
  expect(request.mock.calls.every(([url])=>String(url).startsWith('/demo/bats/'))).toBe(true);
 } finally {cleanup();create.mockRestore();revoke.mockRestore();click.mockRestore();vi.unstubAllGlobals();}
});
