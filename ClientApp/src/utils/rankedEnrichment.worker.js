import {runRankedEnrichment} from './rankedEnrichment';
self.onmessage=event=>{
 try {const {ranked,sets,options}=event.data;const results=runRankedEnrichment(ranked,sets,options,(done,total)=>{if(done%10===0||done===total)self.postMessage({progress:{done,total}});});self.postMessage({results});}
 catch(error){self.postMessage({error:error.message});}
};
