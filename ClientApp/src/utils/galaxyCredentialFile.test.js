import { expect, it } from 'vitest';
import { parseGalaxyCredentialFile } from './galaxyCredentialFile';
const key='0'.repeat(32), id='1'.repeat(32);
it('reads labelled plain text without positional guessing',()=>expect(parseGalaxyCredentialFile(`History ID: ${id}\nKey: ${key}`)).toEqual({key,historyId:id}));
it('accepts the explicitly labelled RTF form',()=>expect(parseGalaxyCredentialFile(`{\\rtf1\\ansi Key: ${key}\\par Completed Analysis ID (fixture): ${id}}`)).toEqual({key,historyId:id}));
it('rejects unlabelled or oversized input without echoing it',()=>{
 expect(()=>parseGalaxyCredentialFile(`${key} ${id}`)).toThrow('labelled');
 expect(()=>parseGalaxyCredentialFile('x'.repeat(65537))).toThrow('small');
});
