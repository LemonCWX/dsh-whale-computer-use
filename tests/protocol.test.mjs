import test from 'node:test';
import assert from 'node:assert/strict';
import { validateBatch, renderResult } from '../protocol.js';
const shot = { captureId: 'a', time: 1000, imageWidth: 1920, imageHeight: 1080 };
const good = () => ({ captureId: 'a', title: '测试区域中输入文字', actions: [{ kind: 'click', x: 1919, y: 1079, description: '聚焦测试文本框', risks: [] }] });
test('accepts exact image boundaries without mutating model arguments', () => {
  const a = good(); const b = validateBatch(a, shot, 2000); b[0].x = 0; assert.equal(a.actions[0].x, 1919);
});
test('rejects stale, missing and mismatched observations', () => {
  for (const s of [null, { ...shot, captureId: 'b' }, { ...shot, time: -200000 }, { ...shot, time: 3000 }]) assert.throws(() => validateBatch(good(), s, 2000));
});
test('rejects offscreen, fractional and nonfinite coordinates', () => {
  for (const x of [-1, 1920, 0.5, NaN, Infinity, '20']) { const a = good(); a.actions[0].x = x; assert.throws(() => validateBatch(a, shot, 2000)); }
});
test('rejects malformed tail before any action is sent', () => {
  const a = good(); a.actions.push({ kind: 'key', keys: '', description: '第二步', risks: [] }); assert.throws(() => validateBatch(a, shot, 2000));
});
test('requires risk declarations and disallows model permission fields', () => {
  const a = good(); delete a.actions[0].risks; assert.throws(() => validateBatch(a, shot, 2000));
  a.actions[0].risks = ['made_up']; assert.throws(() => validateBatch(a, shot, 2000));
  a.actions[0].risks = []; a.actions[0].autoSensitive = true; assert.throws(() => validateBatch(a, shot, 2000));
});
test('bounds text, batch, drag duration and scroll size', () => {
  const cases = [[], Array(6).fill(good().actions[0]), [{kind:'type',text:'a'.repeat(4001)}], [{kind:'type',text:'a\0b'}], [{kind:'scroll',x:1,y:1,dy:21}], [{kind:'drag',x:1,y:1,toX:5,toY:5,ms:0}]];
  for (const actions of cases) assert.throws(() => validateBatch({ ...good(), actions: actions.map(a => ({description:'test',risks:[],...a})) }, shot, 2000));
});
test('renders durable image and exact completion status without embedding base64 in prose', () => {
  const image = {attachmentId:'sample',mediaType:'image/jpeg',bytes:1,width:1,height:1};
  const blocks=renderResult({}, {summary:'人工等待超时',status:'manual_timeout',completed:0,image});
  assert.equal(JSON.parse(blocks[0].text).completed,0); assert.equal(JSON.parse(blocks[0].text).image,undefined); assert.equal(blocks[1].attachment,image);
});
