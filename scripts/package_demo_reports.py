"""Package validated tool reports into small, lazily loaded static demo batches.

Input SQLite databases are local/private. Only sanitized scientific report fields
and separate tool Markdown are exported; credentials and Galaxy IDs are excluded.
"""
import argparse,collections,gzip,hashlib,json,pathlib,re,sqlite3

METHODS={'busted':'BUSTED','absrel':'aBSREL','meme':'MEME'}
def key(value):return re.sub('[^a-z0-9]','',str(value).lower())
def branches(native):
    result=[]
    for partition,rows in native.get('branch attributes',{}).items():
        for name,attributes in rows.items():
            a={key(k):v for k,v in attributes.items()};p=a.get('correctedpvalue')
            result.append(dict(partition=partition,branch=a.get('originalname',name),correctedPValue=p,uncorrectedPValue=a.get('uncorrectedpvalue'),lrt=a.get('lrt'),rateClasses=a.get('rateclasses'),baselineOmega=a.get('baselinemg94xrevomegaratio'),tested=p is not None or str(native.get('tested',{}).get(partition,{}).get(name,'')).lower() in ['test','foreground','tested','1']))
    return result

def sites(native):
    mle=native['MLE'];names=[re.sub('<[^>]+>','',h[0]).lower() for h in mle['headers']]
    p=names.index('p-value');lrt=names.index('lrt') if 'lrt' in names else None;beta=names.index('&beta;+') if '&beta;+' in names else None
    return [dict(partition=partition,site=i+1,pValue=row[p],lrt=row[lrt] if lrt is not None else None,betaPlus=row[beta] if beta is not None else None) for partition,rows in mle['content'].items() for i,row in enumerate(rows)]

def package(snapshot_path,inventory_path,cache_path,report_path,gene_sets_path,target):
    snapshot=json.loads(pathlib.Path(snapshot_path).read_text());inventory=json.loads(pathlib.Path(inventory_path).read_text());target=pathlib.Path(target);folder=target/'reports';folder.mkdir(parents=True,exist_ok=True)
    records={(r['gene'],r['method']):r for r in inventory['records']};expected={k for k,r in records.items() if r['state']=='ok'}
    db=sqlite3.connect(report_path);present=set(db.execute('select gene,method from reports'))
    if expected!=present:raise ValueError(f'Incomplete report import: {len(expected-present)} missing, {len(present-expected)} unexpected')
    summaries=sqlite3.connect(cache_path);reference={r[0]:json.loads(r[1]) for r in summaries.execute('select id,summary from results')}
    index={};batch={};files=[]
    def flush():
        if not batch:return
        text=json.dumps(batch,separators=(',',':'),allow_nan=False)
        if re.search(r'/Users/|/data/|\b[a-f0-9]{32}\b',text):raise ValueError('Private paths or identifiers remain in reports')
        data=gzip.compress(text.encode(),compresslevel=9,mtime=0);digest=hashlib.sha256(data).hexdigest()[:16];name=f'batch-{len(files):04d}-{digest}.json.gz';files.append(name)
        (folder/name).write_bytes(data)
        for gene in batch:index[gene]='/demo/bats/reports/'+name
        batch.clear()
    for gene in snapshot['genes']:
        id=gene['orthogroup'];report={'gene':id,'methods':{}}
        for method,display in METHODS.items():
            record=records[(id,method)]
            if record['state']!='ok':continue
            row=db.execute('select native,markdown from reports where gene=? and method=?',(id,method)).fetchone();native=json.loads(gzip.decompress(row[0]));summary=reference[record['id']]
            if native['input']['number of sequences']!=len(snapshot['species']):raise ValueError('Species coverage mismatch')
            gene['alignment_codons']=native['input']['number of sites']
            if method=='busted':
                if native['test results']['p-value']!=summary['p']:raise ValueError('BUSTED p-value mismatch')
            elif method=='absrel':
                values=branches(native);tested=[v for v in values if v['tested']]
                if sum(v['correctedPValue'] is not None and v['correctedPValue']<=.05 for v in tested)!=summary['selected']:raise ValueError('aBSREL selected-branch mismatch')
                gene['aBSREL_branches']=values
            else:
                values=sites(native)
                if len(values)!=summary['tested'] or sum(v['pValue']<=.1 for v in values)!=summary['selected']:raise ValueError('MEME site result mismatch')
                gene['MEME_site_hits']=[v for v in values if v['pValue']<=.1]
            native['phyloDIVaS']={'representation':'visualization subset','note':'Auxiliary arrays omitted; scientific values are unchanged. Original tool Markdown is supplied separately.'}
            report['methods'][display]={'nativeJson':native,'markdownText':row[1]}
        batch[id]=report
        if len(batch)==32:flush()
    flush();snapshot['schemaVersion']=3;snapshot['successfulGeneCount']=sum(any(g[m+'_status']=='valid' for m in METHODS) for g in snapshot['genes']);snapshot['reportCount']=len(present)
    for p in folder.glob('batch-*.json.gz'):
        if p.name not in files:p.unlink()
    (target/'genome-summary.json').write_text(json.dumps(snapshot,separators=(',',':'),allow_nan=False)+'\n')
    (target/'report-index.json').write_text(json.dumps(index,separators=(',',':'))+'\n')
    (target/'gene-sets.json').write_bytes(pathlib.Path(gene_sets_path).read_bytes())
    print(json.dumps({'genes':len(snapshot['genes']),'successfulGenes':snapshot['successfulGeneCount'],'toolReports':len(present),'batches':len(files),'compressedBytes':sum((folder/f).stat().st_size for f in files)}))
if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ['snapshot','inventory','cache','reports','gene-sets','output']:parser.add_argument('--'+name,required=True)
    args=parser.parse_args();package(args.snapshot,args.inventory,args.cache,args.reports,args.gene_sets,args.output)
