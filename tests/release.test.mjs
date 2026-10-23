import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { NativeBridge } from '../bridge.js';

test('package version, actual bridge executable and native reported version agree',async()=>{
  const root=new URL('../',import.meta.url);
  const manifest=JSON.parse(await fs.readFile(new URL('package.json',root),'utf8'));
  const bridge=new NativeBridge();
  assert.equal(path.basename(bridge.executable),`WhaleComputer-${manifest.version}.exe`);
  const binary=await fs.readFile(bridge.executable);
  assert.equal(binary.subarray(0,2).toString(),'MZ');
  const source=await fs.readFile(new URL('native/Program.cs',root),'utf8');
  assert.match(source,new RegExp(`const string Version="${manifest.version.replaceAll('.','\\.')}"`));
  for(const name of ['build.ps1','install.ps1']) {
    const script=await fs.readFile(new URL(name,root),'utf8');
    assert.match(script,/WhaleComputer-\$packageVersion\.exe/);
  }
});
