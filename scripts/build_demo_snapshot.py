"""Build a credential-free demo from a fully retrieved, validated Galaxy cache.

Reads private cache inputs; writes only explicitly selected scientific fields.
Never substitutes older results for an unavailable method.
"""
import argparse,collections,csv,json,pathlib,re,sqlite3

def build(inventory_path,cache_path,membership_path,headers_path,old_path):
    inventory=json.loads(pathlib.Path(inventory_path).read_text());records=inventory['records']
    old=json.loads(pathlib.Path(old_path).read_text());annotations={g['orthogroup']:g for g in old['genes']}
    with pathlib.Path(membership_path).open() as stream:
        membership=list(csv.DictReader(stream,delimiter='\t'))
    headers={}
    wanted={v for row in membership for v in list(row.values())[3:]}
    with pathlib.Path(headers_path).open() as stream:
      for line in stream:
        if not line.startswith('>'):continue
        accession=line[1:].split()[0]
        if accession not in wanted:continue
        fields=dict(re.findall(r'\[([^=\]]+)=([^\]]+)\]',line));headers[accession]=fields
    with sqlite3.connect(cache_path) as db:
        summaries={r[0]:json.loads(r[1]) for r in db.execute('select id,summary from results')}
    missing=[r for r in records if r['state']=='ok' and r['id'] not in summaries]
    if missing:raise ValueError(f'{len(missing)} successful Galaxy records have not been retrieved/validated. Publication refused.')
    by_gene=collections.defaultdict(dict)
    for r in records:by_gene[r['gene']][r['method']]=r
    requested=len(membership)
    expected={row['HOG'] for row in membership}
    if len(expected)!=requested:raise ValueError('Duplicate membership identifiers')
    for method in ['busted','absrel','meme']:
        rows=[r for r in records if r['method']==method]
        if len(rows)!=requested or {r['gene'] for r in rows}!=expected:raise ValueError('Method membership sets do not match')
        if dict(collections.Counter(r['state'] for r in rows))!=inventory['coverage'][method]:raise ValueError('Method coverage does not reconcile')
    genes=[];exceptions=[]
    for row in membership:
        gene=row['HOG'];methods=by_gene[gene]
        states={m:r['state'] for m,r in methods.items()}
        if any(v!='ok' for v in states.values()):exceptions.append({'orthogroup':gene,'methods':states})
        if not any(v=='ok' for v in states.values()):continue
        g={k:v for k,v in annotations.get(gene,{}).items() if k in ['orthogroup','consensus_bat_symbol','protein_name','mapping_confidence','members_with_headers','symbol_support']}
        members=[headers.get(acc,{}) for acc in list(row.values())[3:]];symbols=collections.Counter(h.get('gene','') for h in members if h.get('gene') and not h['gene'].startswith('LOC'));symbol=symbols.most_common(1)[0][0] if symbols else ''
        representative=next((h for h in members if h.get('gene')==symbol),next((h for h in members if h),{}))
        g.update(orthogroup=gene,consensus_bat_symbol=symbol or g.get('consensus_bat_symbol',''),protein_name=representative.get('protein') or g.get('protein_name',''),mapping_confidence='concordant >=2 bat annotations' if len(symbols)==1 and symbols[symbol]>=2 else 'discordant or insufficient annotation support',members_with_headers=sum(bool(h) for h in members),symbol_support=symbols.get(symbol,0))
        g.update(BUSTED_p=None,BUSTED_q_BH=None,MEME_tested_sites=None,MEME_sites_raw01=None,MEME_min_p=None,aBSREL_tested=None,aBSREL_branches_within_gene005=None,aBSREL_min_corrected_p=None)
        for method,r in methods.items():
            g[method+'_status']='valid' if r['state']=='ok' else r['state']
            if r['state']!='ok':continue
            s=summaries[r['id']]
            if method=='busted':g['BUSTED_p']=s['p']
            elif method=='meme':g.update(MEME_tested_sites=s['tested'],MEME_sites_raw01=s['selected'],MEME_min_p=s['minimum'])
            else:g.update(aBSREL_tested=s['tested'],aBSREL_branches_within_gene005=s['selected'],aBSREL_min_corrected_p=s['minimum'])
        genes.append(g)
    valid=sorted([g for g in genes if g['BUSTED_p'] is not None],key=lambda g:g['BUSTED_p']);minimum=1
    for index in range(len(valid)-1,-1,-1):
        minimum=min(minimum,valid[index]['BUSTED_p']*len(valid)/(index+1));valid[index]['BUSTED_q_BH']=minimum
    active={'new','queued','running','waiting','upload','setting_metadata'}
    return dict(schemaVersion=2,savedAt=inventory['capturedAt'],finished=not any(r['state'] in active for r in records),requested=requested,species=old['species'],genes=genes,methods=inventory['coverage'],exceptions=exceptions)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for name in ['inventory','cache','membership','headers','old','output']:p.add_argument('--'+name,required=True)
    a=p.parse_args();data=build(a.inventory,a.cache,a.membership,a.headers,a.old);target=pathlib.Path(a.output);tmp=target.with_suffix('.tmp');tmp.write_text(json.dumps(data,separators=(',',':'))+'\n');tmp.replace(target)
    print(json.dumps({'genes':len(data['genes']),'allThree':sum(all(g[m+'_status']=='valid' for m in ['busted','absrel','meme']) for g in data['genes']),'exceptions':len(data['exceptions']),'finished':data['finished']}))
