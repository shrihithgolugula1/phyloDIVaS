import pathlib,tempfile,json,sqlite3,unittest
from build_demo_snapshot import build
class DemoImportTests(unittest.TestCase):
 def test_finished_with_exceptions_preserves_successes_and_never_fills_from_old_run(self):
  with tempfile.TemporaryDirectory() as temp:
   p=pathlib.Path(temp)
   rows=[]
   for gene,states in [('N0.HOG0000001',{'busted':'ok','absrel':'ok','meme':'error'}),('N0.HOG0000002',{'busted':'paused','absrel':'paused','meme':'paused'})]:
    rows.extend({'gene':gene,'method':m,'state':s,'id':gene+m} for m,s in states.items())
   (p/'inventory.json').write_text(json.dumps({'records':rows,'coverage':{'busted':{'ok':1,'paused':1},'absrel':{'ok':1,'paused':1},'meme':{'error':1,'paused':1}},'capturedAt':'fixture'}))
   (p/'members.tsv').write_text('HOG\tOG\tGene Tree Parent Clade\tA\nN0.HOG0000001\tOG1\tN0\tXP_1.1\nN0.HOG0000002\tOG2\tN0\tXP_2.1\n')
   (p/'headers.txt').write_text('>XP_1.1 [gene=ABC1] [protein=Example protein]\n')
   (p/'old.json').write_text(json.dumps({'species':['A'],'genes':[{'orthogroup':'N0.HOG0000001','MEME_sites_raw01':99,'BUSTED_p':0}]}))
   db=sqlite3.connect(p/'cache.sqlite');db.execute('create table results(id text,summary text)')
   db.execute('insert into results values(?,?)',('N0.HOG0000001busted',json.dumps({'p':.5})))
   db.execute('insert into results values(?,?)',('N0.HOG0000001absrel',json.dumps({'tested':1,'selected':0,'minimum':1})));db.commit();db.close()
   result=build(p/'inventory.json',p/'cache.sqlite',p/'members.tsv',p/'headers.txt',p/'old.json')
   self.assertTrue(result['finished']);self.assertEqual(len(result['genes']),1);self.assertEqual(len(result['exceptions']),2)
   self.assertEqual(result['genes'][0]['BUSTED_p'],.5);self.assertEqual(result['genes'][0]['BUSTED_q_BH'],.5)
   self.assertIsNone(result['genes'][0]['MEME_sites_raw01']);self.assertEqual(result['genes'][0]['meme_status'],'error')
   rows[0]['id']='not-retrieved';(p/'inventory.json').write_text(json.dumps({'records':rows,'coverage':result['methods'],'capturedAt':'fixture'}))
   with self.assertRaisesRegex(ValueError,'Publication refused'):build(p/'inventory.json',p/'cache.sqlite',p/'members.tsv',p/'headers.txt',p/'old.json')
if __name__=='__main__':unittest.main()
