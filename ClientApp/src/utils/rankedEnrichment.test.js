import {describe,it,expect} from 'vitest';
import {runningScore,prepareRankedFamily,runRankedEnrichment,parseGmt,rankingsMatch} from './rankedEnrichment';
describe('ranked gene-set enrichment',()=>{
 it('accepts cross-engine floating-point noise without reusing a different gene ranking',()=>{
  const original=[{id:'A',score:3.14159265358979},{id:'B',score:1}];
  expect(rankingsMatch(original,[{id:'A',score:3.141592653589791},{id:'B',score:1}])).toBe(true);
  expect(rankingsMatch(original,[{id:'A',score:3.15},{id:'B',score:1}])).toBe(false);
  expect(rankingsMatch(original,[{id:'B',score:3.14159265358979},{id:'A',score:1}])).toBe(false);
 });
 it('places top and bottom hits on the correct side of the running score',()=>{
  expect(runningScore([4,3,2,1],[0,1],true).es).toBeCloseTo(1);
  expect(runningScore([4,3,2,1],[2,3],true).es).toBeCloseTo(-1);
  expect(runningScore([4,3,2,1],[0,1],true).points.at(-1)).toEqual({rank:4,value:0});
  expect(runningScore([1,1],[0,1])).toBeNull();expect(runningScore([1,1],[])).toBeNull();
 });
 it('uses the full valid tested background and excludes ambiguous symbol mapping',()=>{
  const genes=[['A','dup',.01],['B','dup',.1],['C','Unique',.5],['D','paused',null]].map(([orthogroupId,symbol,bustedPValue])=>({orthogroupId,symbol,bustedPValue,methodStatus:{BUSTED:bustedPValue==null?'paused':'complete'}}));
  const family=prepareRankedFamily(genes,[{id:'set',genes:['dup','Unique','A','A','not-present']}]);
  expect(family.ranked.map(g=>g.id)).toEqual(['A','B','C']);expect(family.sets[0].genes).toEqual(['C','A']);
  expect(prepareRankedFamily(genes,[],['B']).ranked.map(g=>g.id)).toEqual(['B']);
 });
 it('produces deterministic bounded permutation p-values and BH across every eligible set',()=>{
  const ranked=Array.from({length:30},(_,i)=>({id:String(i),score:30-i}));
  const sets=[{id:'high',name:'High',genes:['0','1','2','3']},{id:'low',name:'Low',genes:['26','27','28','29']},{id:'too-small',genes:['0']}];
  const options={permutations:199,minSize:3,maxSize:20,seed:42};const results=runRankedEnrichment(ranked,sets,options);
  expect(results).toEqual(runRankedEnrichment(ranked,sets,options));expect(results).toHaveLength(2);
  for(const result of results){expect(result.pValue).toBeGreaterThan(0);expect(result.pValue).toBeLessThanOrEqual(1);expect(result.qValue).toBeGreaterThanOrEqual(result.pValue);expect(Number.isFinite(result.nes)).toBe(true);}
  expect(results.find(r=>r.id==='high').es).toBeGreaterThan(0);expect(results.find(r=>r.id==='low').es).toBeLessThan(0);
  expect(runRankedEnrichment(ranked.map(g=>({...g,score:0})),sets,options)).toEqual([]);
 });
 it('rejects malformed or duplicate GMT rows',()=>{
  expect(parseGmt('set\tdescription\tA\tB\tA')[0].genes).toEqual(['A','B']);expect(()=>parseGmt('bad')).toThrow();expect(()=>parseGmt('set\td\tA\nset\td\tB')).toThrow();
 });
});
