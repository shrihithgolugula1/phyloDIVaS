import React from 'react';
import {expect,it} from 'vitest';
import {render,screen,cleanup} from '@testing-library/react';
import {MemeManhattan,AbsrelGeneTree,parseGeneTree} from './GeneResultFigures';
import {summarizeHyphyResult} from '../../../utils/hyphyResults';
import MarkdownReport from './MarkdownReport';

it('plots zero MEME probabilities as strong evidence and separates partitions',()=>{
 try {
  render(<MemeManhattan geneId="test" threshold={.1} sites={[{partition:'0',site:1,pValue:0},{partition:'0',site:2,pValue:1},{partition:'1',site:1,pValue:.04}]}/>);
  const chart=screen.getByRole('img',{name:'MEME Manhattan plot by codon position'}),points=chart.querySelectorAll('circle');
  expect(points.length).toBe(2);expect(Number(points[0].getAttribute('cy'))).toBeLessThan(Number(points[1].getAttribute('cy')));
  expect(points[0].getAttribute('fill')).toBe('#c52c38');expect(screen.getByRole('combobox',{name:'Alignment partition'}).children).toHaveLength(2);
 } finally{cleanup();}
});
it('maps native aBSREL branch results to scaled gene-tree edges without clipping labels',async()=>{
 try {
  const example=await fetch('/demo/bats/N0.HOG0004322.json').then(r=>r.json());const summary=summarizeHyphyResult(example.methods.aBSREL.nativeJson,'aBSREL');
  render(<AbsrelGeneTree gene={{orthogroupId:example.gene,speciesGenes:example.speciesGenes}} summary={summary} threshold={.05}/>);
  const tree=screen.getByRole('img',{name:'aBSREL gene tree with selected branches highlighted'});
  expect(tree.querySelectorAll('line[stroke="#dc243b"]')).toHaveLength(summary.selectedBranches);
  expect(tree.textContent).toContain('Rhinolophus ferrumequinum');
  expect(parseGeneTree("('Species one':.2,Species_two:.3)Root;").children[0].name).toBe('Species one');
  expect(()=>parseGeneTree('(A:NaN,B:.3);')).toThrow();expect(()=>parseGeneTree('')).toThrow();
 }finally{cleanup();}
});
it('renders original tool Markdown as safe readable text and tables',()=>{
 try {
  const {container}=render(<MarkdownReport text={'Results\n-------\n\n| Test | p-value |\n| --- | --- |\n| BUSTED | 0.01 |\n\n<script>window.bad=true</script>'}/>);
  expect(screen.getByRole('heading',{name:'Results'})).toBeTruthy();expect(screen.getByRole('cell',{name:'0.01'})).toBeTruthy();expect(container.querySelector('script')).toBeNull();
 }finally{cleanup();}
});

it('calculates ranked enrichment off the UI thread using the full annotated family',async()=>{
 const {default:RankedEnrichment}=await import('./RankedEnrichment');
 try {
  const genes=Array.from({length:40},(_,i)=>({id:String(i),orthogroupId:'G'+i,symbol:'S'+i,bustedPValue:Math.pow(2,-40+i),methodStatus:{BUSTED:'complete'}}));
  render(<RankedEnrichment genes={genes} geneSets={{source:'Exact fixture mappings',annotatedIds:genes.map(g=>g.orthogroupId),sets:[{id:'test-set',name:'Fixture high-ranked genes',genes:genes.slice(0,20).map(g=>g.orthogroupId)}]}}/>);
  screen.getByRole('button',{name:'Calculate enrichment'}).click();
  expect(await screen.findByRole('img',{name:'Gene-set enrichment running-score plot'},{timeout:30000})).toBeTruthy();
  expect(screen.getByRole('combobox',{name:'Gene set'}).textContent).toContain('Fixture high-ranked genes');
  expect(screen.getByRole('button',{name:'Download enrichment results'})).toBeTruthy();
 }finally{cleanup();}
});
