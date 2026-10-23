import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import vm from 'node:vm';
import { registerSettingsEntry } from '../settings-entry.js';
const root = new URL('../', import.meta.url);

test('the installed bundle exposes a discoverable DSH settings section', async () => {
  const manifest=JSON.parse(await fs.readFile(new URL('package.json',root),'utf8'));
  assert.ok(manifest.dsh.client, 'The bundle has no DSH client companion, so no settings entry can appear.');
  assert.ok(manifest.dsh.client.inject.includes('@deepseek-ai/dsh-client-ui-settings'));
  assert.ok(manifest.exports['./client']);
  const source=await fs.readFile(new URL(manifest.exports['./client'],root),'utf8');
  let module;
  const React={createElement:(tag,props,...children)=>({tag,props:props||{},children}),useState:value=>[value,()=>{}],useEffect:()=>{}};
  vm.runInNewContext(source,{window:{__ModuleLoader__:{load:m=>{module=m;}}},AbortController});
  assert.equal(module.id,manifest.name);
  const client=module.factory(name=>{assert.equal(name,'react');return React;});
  const calls=[],rows=[];
  const ctx={
    effect:fn=>fn(),
    locale:{register:()=>()=>{},bind:()=>key=>key},
    connection:{rpc:{call:async(...args)=>{calls.push(args);return{ok:true,value:{summary:'opened'}};}}},
    slots:{inject:(_slot,fn)=>fn(),register:(options,Component)=>{rows.push({options,Component});return()=>{};}},
  };
  client.apply(ctx);
  const section=rows.find(r=>r.options.name==='settings.section');
  assert.ok(section,'No entry in the actual DSH Settings navigation.');
  const injected=section.options.inject?.()||{};
  const tree=section.Component({t:key=>key,...injected});
  const all=[];const walk=v=>{if(v&&typeof v==='object'){all.push(v);v.children?.flat(Infinity).forEach(walk);}};walk(tree);
  const button=all.find(n=>n.tag==='button'&&n.props['data-whale-action']==='open-settings');
  assert.ok(button,'The settings section must contain a direct user button.');
  await button.props.onClick();
  assert.equal(calls.length,1);
  assert.equal(calls[0][0],'/api');
  assert.equal(calls[0][1],'whale-computer-use/open-settings');
  assert.deepEqual(JSON.parse(JSON.stringify(calls[0][2])),{});
});

function routesWith(native) {
  const routes=[];
  registerSettingsEntry({connection:{fetch:{register:r=>routes.push(r)}}},native);
  return routes;
}
function request(method,payload={}) {
  return new Request(`http://localhost/api/${method}`,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({type:'client-request',rpcId:'settings-1',method,payload})});
}
test('the UI button reaches native settings without model or input execution',async()=>{
  const calls=[];
  const routes=routesWith({request:async(...args)=>{calls.push(args);return{summary:'opened'};}});
  const route=routes.find(r=>r.path==='/api/whale-computer-use/open-settings');
  assert.deepEqual(route.methods,['POST']);
  const response=await route.fetch(request('whale-computer-use/open-settings'));
  const body=await response.json();
  assert.equal(body.rpcId,'settings-1');assert.equal(body.result.ok,true);
  assert.equal(calls.length,1);assert.equal(calls[0][0],'settings');
});
test('settings UI publishes no permission setter and refuses hidden payloads',async()=>{
  let calls=0;const routes=routesWith({request:async()=>{calls++;}});
  assert.deepEqual(routes.map(r=>r.path),['/api/whale-computer-use/status','/api/whale-computer-use/open-settings']);
  const route=routes[1];
  for(const req of [request('whale-computer-use/open-settings',{AutoSensitive:true}),request('whale-computer-use/change-settings')])assert.equal((await route.fetch(req)).status,400);
  assert.equal(calls,0);
});
test('unavailable helper produces an explicit visible UI failure',async()=>{
  const routes=routesWith({request:async()=>{throw new Error('Helper exited.');}});
  const body=await(await routes[1].fetch(request('whale-computer-use/open-settings'))).json();
  assert.equal(body.result.ok,false);assert.equal(body.result.error.message,'Helper exited.');
});
