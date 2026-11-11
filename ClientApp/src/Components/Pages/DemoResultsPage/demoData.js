export function parseMembership(text) {
    const lines = text.trim().split(/\r?\n/);
    const header = lines.shift().split('\t');
    if (header[0] !== 'HOG' || header.length < 4) throw new Error('Invalid saved orthogroup table.');
    const species = header.slice(3).map(name => name.replace(/_proteome_filtered$/i, '').replaceAll('_', ' ').replace(/^[a-z]/, c => c.toUpperCase()));
    const rows = lines.map(line => line.split('\t'));
    if (rows.some(row => row.length !== header.length || !/^N\d+\.HOG\d+$/.test(row[0]))) throw new Error('Incomplete saved membership rows.');
    return {species, rows};
}

export function parseTree(text) {
    const tokens = text.match(/\(|\)|,|:|;|[^\s(),:;]+/g) || [];
    let cursor = 0;
    const node = () => {
        const result = {children: [], name: '', length: 0};
        if (tokens[cursor] === '(') {
            cursor++;
            do { result.children.push(node()); } while (tokens[cursor++] === ',');
        }
        if (tokens[cursor] && ![':',',',')',';'].includes(tokens[cursor])) result.name = tokens[cursor++];
        if (tokens[cursor] === ':') { cursor++; result.length = Number(tokens[cursor++]); }
        if (!Number.isFinite(result.length) || result.length < 0) throw new Error('Invalid tree branch length.');
        return result;
    };
    const root = node();
    if (tokens[cursor] !== ';' || cursor !== tokens.length - 1) throw new Error('Invalid saved Newick tree.');
    return root;
}

export function layoutTree(root) {
    const nodes = [];
    let leaf = 0;
    const walk = (node, distance, parent = null) => {
        const current = {...node, distance, parent}; nodes.push(current);
        current.children = node.children.map(child => walk(child, distance + child.length, current));
        current.y = current.children.length ? current.children.reduce((sum, child) => sum + child.y, 0) / current.children.length : 40 + leaf++ * 62;
        return current;
    };
    walk(root, 0);
    const maximum = Math.max(...nodes.map(n => n.distance));
    nodes.forEach(n => { n.x = 30 + (maximum ? n.distance / maximum * 480 : 0); });
    return {nodes, maximum, height: leaf * 62 + 75};
}

export async function readDemoAsset(path, text = false) {
    if (!path.startsWith('/demo/bats/')) throw new Error('Invalid demo asset.');
    const response = await fetch(path, {cache: 'no-store'});
    if (!response.ok) throw new Error('A saved demo file could not be loaded. Refresh to retry.');
    return text ? response.text() : response.json();
}

const reportCache=new Map();
export async function readDemoReport(index,gene) {
    const path=index[gene];
    if(!path)return {gene,methods:{}};
    if(!/^\/demo\/bats\/reports\/batch-[a-z0-9-]+\.json\.gz$/.test(path))throw Error('Invalid saved report path.');
    if(!reportCache.has(path)) {
        const promise=fetch(path).then(async response=>{
            if(!response.ok)throw Error('The saved gene report could not be loaded.');
            const bytes=new Uint8Array(await response.arrayBuffer());
            const text=bytes[0]===31&&bytes[1]===139?await new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'))).text():new TextDecoder().decode(bytes);
            return JSON.parse(text);
        }).catch(error=>{reportCache.delete(path);throw error;});
        reportCache.set(path,promise);if(reportCache.size>8)reportCache.delete(reportCache.keys().next().value);
    }
    const batch=await reportCache.get(path),report=batch[gene];
    if(!report||report.gene!==gene)throw Error('The saved report does not match this orthogroup.');return report;
}

export function buildSavedAnalysis(snapshot,membership,geneSets,reportIndex) {
    const byId=new Map(snapshot.genes.map(g=>[g.orthogroup,g]));
    const exceptions=new Map((snapshot.exceptions||[]).map(g=>[g.orthogroup,g.methods]));
    const cards=[],groups=[];
    const definitions=[['BUSTED','busted'],['aBSREL','absrel'],['MEME','meme']];
    membership.rows.forEach(row=>{
        const id=row[0],g=byId.get(id)||{};
        const speciesGenes=Object.fromEntries(membership.species.map((species,i)=>[species,row[i+3]]));
        groups.push({groupId:id,symbol:g.consensus_bat_symbol||null,groupName:g.protein_name||id,speciesGenes});
        definitions.forEach(([method,key])=>{
            const status=g[key+'_status']||(exceptions.get(id)?.[key]||'missing'),state=status==='valid'?'ok':status;
            let summary=null;
            if(state==='ok') {
                if(method==='aBSREL'&&!Array.isArray(g.aBSREL_branches))throw Error('The saved branch inventory is incomplete.');
                if(method==='MEME'&&!Array.isArray(g.MEME_site_hits))throw Error('The saved site inventory is incomplete.');
                summary={method,resultAvailable:true,sequences:snapshot.species.length,sites:g.alignment_codons,
                    pValue:method==='BUSTED'?g.BUSTED_p:null,
                    absrelBranchResults:g.aBSREL_branches||[],memeSiteResults:g.MEME_site_hits||[],
                    selectedBranches:g.aBSREL_branches_within_gene005||0,selectedSites:g.MEME_sites_raw01||0,
                    callLabel:'Saved tool result',conclusion:'Open the detailed report for the original tool summary and complete site or branch results.'};
            }
            const json={id:`saved-${id}-${method}`,method,elementIdentifier:id,state,summaryOnly:true};
            cards.push({method,elementIdentifier:id,json,summary});
        });
    });
    return {cards,groups,geneSets,loadReport:gene=>readDemoReport(reportIndex,gene),savedAt:snapshot.savedAt,
        summaryThresholdCaps:{absrelP:.05,memeP:.1},source:'Saved genome-wide bat analysis'};
}
