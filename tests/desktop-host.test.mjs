import test from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { attachDesktop } from '../index.js';

function harness({maximum=80,act,imageWidth=400,imageHeight=300}={}) {
  const calls=[],tools=new Map(),events=new Map();let next=0;
  let settings={MaxBatches:maximum};
  const native=new EventEmitter();
  native.on('event',message=>{if(message.settings)settings=message.settings;});
  native.request=async(op,payload={},options={})=>{
    options.signal?.throwIfAborted();calls.push({op,payload});
    if(op==='capture')return {base64:'AA==',captureId:`shot-${++next}`,imageWidth:800,imageHeight:600,screenWidth:2560,screenHeight:1600,foreground:'TestSurface',settings,captureMode:payload.zoom?'region':'full',region:{x:0,y:0,width:2560,height:1600}};
    if(op==='act')return act?act(payload,options):{status:'executed',summary:'done',completed:payload.actions.length,total:payload.actions.length};
    return {summary:'finished'};
  };
  native.start=async()=>{};native.dispose=async()=>{};
  const ctx={inject(){},effect(){},systemPrompt:{section(){}},on:(key,fn)=>events.set(key,fn),tools:{register:d=>tools.set(d.name,d)},attachments:{saveImage:async()=>({attachmentId:`image-${next}`,width:imageWidth,height:imageHeight,mediaType:'image/jpeg'})}};
  attachDesktop(ctx,native);
  const agent={cancel(){},steer(){}};
  const exec={agent,signal:new AbortController().signal};
  const run=(tool,args)=>tools.get(`whale_desktop_${tool}`).execute(args,exec);
  return {calls,native,run,events,agent};
}
const batch=id=>({captureId:id,title:'harmless wait',actions:[{kind:'wait',ms:1,description:'wait',risks:[]}]});

function assertLossless(value) {
  assert.deepEqual(value,JSON.parse(JSON.stringify(value)),'Tool output must be lossless JSON, including all nested fields.');
}

test('ordinary capture has explicit recovery defaults and is lossless JSON',async()=>{
  const h=harness(),result=await h.run('capture',{});
  assertLossless(result);
  assert.equal(result.recoveredFromStale,false);assert.equal(result.staleReason,null);
});

test('region, post-action, stale recovery and control outputs remain lossless JSON',async()=>{
  const h=harness(),first=await h.run('capture',{});
  const zoom=await h.run('capture',{captureId:first.captureId,zoom:{x:0,y:0,width:20,height:20}});
  assertLossless(zoom);
  assertLossless(await h.run('act',batch(zoom.captureId)));
  assertLossless(await h.run('act',batch('used-frame')));
  assertLossless(await h.run('control',{action:'status'}));
  assertLossless(await h.run('control',{action:'finish'}));
});

test('region capture forwards the actual attachment dimensions and actions use returned image coordinates',async()=>{
  const h=harness();const first=await h.run('capture',{});
  assert.equal(first.imageWidth,400);
  const zoom={x:100,y:50,width:200,height:100,scale:2};
  const region=await h.run('capture',{captureId:first.captureId,zoom});
  assert.deepEqual(h.calls[1].payload,{captureId:first.captureId,zoom,imageWidth:400,imageHeight:300});
  assert.equal(region.captureMode,'region');
  await h.run('act',{captureId:region.captureId,title:'click enlarged target',actions:[{kind:'click',x:399,y:299,description:'target',risks:[]}]});
  const action=h.calls.find(c=>c.op==='act').payload;
  assert.equal(action.captureId,region.captureId);assert.equal(action.imageWidth,400);
  assert.equal(action.actions[0].x,399);assert.equal(action.actions[0].y,299);
});

test('native stale observation returns a new image with zero input and no replay',async()=>{
  const h=harness({act:()=>({status:'stale_capture',summary:'foreground changed',completed:0,total:1})});
  const first=await h.run('capture',{}),result=await h.run('act',batch(first.captureId));
  assert.equal(result.status,'stale_capture');assert.equal(result.completed,0);
  assert.notEqual(result.captureId,first.captureId);assert.ok(result.image);
  assert.equal(result.budget.used,0);assert.equal(h.calls.filter(c=>c.op==='act').length,1);
});

test('unknown screenshot IDs recover without sending any native action or zoom',async()=>{
  const h=harness();await h.run('capture',{});
  const action=await h.run('act',batch('old'));
  assert.equal(action.status,'stale_capture');assert.equal(action.completed,0);assert.ok(action.image);
  const region=await h.run('capture',{captureId:'old',zoom:{x:0,y:0,width:20,height:20}});
  assert.equal(region.status,'stale_capture');assert.equal(region.captureMode,'full');
  assert.ok(h.calls.every(c=>c.op==='capture'&&!c.payload.zoom));
});

test('zoom invalidated inside native capture is exposed as a fresh full-screen stale result',async()=>{
  const h=harness();const first=await h.run('capture',{}),request=h.native.request;
  h.native.request=async(op,...args)=>{
    const result=await request(op,...args);
    return op==='capture'?{...result,captureMode:'full',recoveredFromStale:true,staleReason:'physical input'}:result;
  };
  const result=await h.run('capture',{captureId:first.captureId,zoom:{x:10,y:10,width:20,height:20}});
  assert.equal(result.status,'stale_capture');assert.equal(result.completed,0);
  assert.equal(result.captureMode,'full');assert.ok(result.image);assert.equal(result.staleReason,'physical input');
});

test('expired observation recovers without executing old coordinates',async t=>{
  const h=harness();const first=await h.run('capture',{});const now=Date.now();
  t.mock.method(Date,'now',()=>now+121000);
  const result=await h.run('act',batch(first.captureId));
  assert.equal(result.status,'stale_capture');assert.ok(result.image);
  assert.equal(h.calls.filter(c=>c.op==='act').length,0);
});

test('malformed tails and model-supplied limit changes fail before recovery or execution',async()=>{
  const h=harness();const bad=batch('old');bad.actions.push({kind:'key',keys:'',description:'bad',risks:[]});
  await assert.rejects(()=>h.run('act',bad));
  await assert.rejects(()=>h.run('act',{...batch('old'),MaxBatches:1000}));
  assert.equal(h.calls.length,0);
});

test('user-selected batch cap finishes with progress and remains latched through finish calls',async()=>{
  const h=harness({maximum:2});const first=await h.run('capture',{});
  const one=await h.run('act',batch(first.captureId));
  assert.equal(one.budget.remaining,1);
  const two=await h.run('act',batch(one.captureId));
  assert.equal(two.limitReached,true);assert.equal(two.budget.completedActions,2);
  assert.equal(h.calls.at(-1).op,'finish');
  await h.run('control',{action:'finish'});
  const denied=await h.run('act',batch(two.captureId));
  assert.equal(denied.status,'batch_limit');assert.equal(denied.completed,0);
  assert.equal(h.calls.filter(c=>c.op==='act').length,2);
  h.events.get('agent/status')({agent:h.agent,status:'idle'});
  const fresh=await h.run('capture',{});await h.run('act',batch(fresh.captureId));
  assert.equal(h.calls.filter(c=>c.op==='act').length,3);
});

test('stale recovery does not consume the user batch budget',async()=>{
  let attempts=0;
  const h=harness({maximum:1,act:()=>++attempts===1?{status:'stale_capture',summary:'changed',completed:0,total:1}:{status:'executed',summary:'done',completed:1,total:1}});
  const first=await h.run('capture',{}),stale=await h.run('act',batch(first.captureId));
  assert.equal(stale.budget.used,0);
  const done=await h.run('act',batch(stale.captureId));
  assert.equal(done.limitReached,true);assert.equal(done.budget.used,1);
});

test('user interruption reports actual partial completion and a new screenshot; stopped calls never recapture',async()=>{
  const h=harness({act:()=>({status:'interrupted',summary:'user takeover',completed:1,total:2})});
  const first=await h.run('capture',{}),args=batch(first.captureId);args.actions.push({...args.actions[0]});
  const interrupted=await h.run('act',args);
  assert.ok(interrupted.image);assert.equal(interrupted.completed,1);assert.equal(interrupted.budget.completedActions,1);
  const stopped=harness({act:()=>({status:'stopped',summary:'stopped',completed:0,total:1})});
  const frame=await stopped.run('capture',{}),result=await stopped.run('act',batch(frame.captureId));
  assert.equal(result.status,'stopped');assert.equal(result.image,undefined);
  assert.deepEqual(stopped.calls.map(c=>c.op),['capture','act']);
});

test('changes to native user settings invalidate the old shot and update the budget',async()=>{
  const h=harness();const first=await h.run('capture',{});
  h.native.emit('event',{event:'settings-changed',settings:{MaxBatches:1}});
  const result=await h.run('act',batch(first.captureId));
  assert.equal(result.status,'stale_capture');assert.equal(h.calls.filter(c=>c.op==='act').length,0);
  assert.equal(result.budget.maximum,1);
});

test('failed recapture preserves zero completion and explicitly reports the observation error',async()=>{
  const h=harness();await h.run('capture',{});
  const request=h.native.request;
  h.native.request=(op,...args)=>{if(op==='capture')throw new Error('User dialog is open.');return request(op,...args);};
  const result=await h.run('act',batch('expired'));
  assert.equal(result.status,'stale_capture');assert.equal(result.completed,0);
  assert.equal(result.image,undefined);assert.match(result.observationError,/dialog/);
  assert.equal(h.calls.filter(c=>c.op==='act').length,0);
});

test('aborted tasks do not obtain a new image or execute native input',async()=>{
  const h=harness(),controller=new AbortController();controller.abort();
  // Exercise the registered tool with an already aborted execution context.
  const ctx={agent:h.agent,signal:controller.signal};
  // The public wrapper's exclusive guard must run before capture validation.
  const tools=new Map();attachDesktop({inject(){},effect(){},on(){},systemPrompt:{section(){}},tools:{register:d=>tools.set(d.name,d)},attachments:{}},h.native);
  await assert.rejects(()=>tools.get('whale_desktop_capture').execute({},ctx));
  assert.equal(h.calls.length,0);
});
