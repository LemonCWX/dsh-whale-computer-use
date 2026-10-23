export const RISK_CATEGORIES = ['payment', 'deletion', 'external_message', 'account', 'permissions', 'credentials', 'software', 'system', 'custom'];
const string = { type: 'string' };
const integer = { type: 'integer' };
export const ACTION_SCHEMA = {
  type: 'object', additionalProperties: false, required: ['kind', 'description', 'risks'],
  properties: {
    kind: { type: 'string', enum: ['click', 'move', 'type', 'key', 'scroll', 'drag', 'wait'] },
    description: { type: 'string', description: 'Describe the real effect and target, including recipient / destructive / account / payment effects. Do not conceal risk.' },
    risks: { type: 'array', items: { type: 'string', enum: RISK_CATEGORIES }, description: 'ALL applicable risk categories, even if not selected in user settings. [] only for ordinary reversible actions.' },
    x: integer, y: integer, toX: integer, toY: integer,
    button: { type: 'string', enum: ['left', 'right', 'middle'] },
    count: integer, text: string, keys: string,
    dx: { ...integer, description: 'Horizontal wheel notches; positive right.' },
    dy: { ...integer, description: 'Vertical wheel notches; positive UP, negative down.' },
    ms: integer, modifiers: { type: 'array', items: { type: 'string', enum: ['ctrl', 'shift', 'alt'] } },
  },
};
export const CAPTURE_SCHEMA = { type: 'object', additionalProperties: false, properties: {} };
export const ACT_SCHEMA = {
  type: 'object', additionalProperties: false, required: ['captureId', 'title', 'actions'],
  properties: {
    captureId: { type: 'string', description: 'ID from the newest capture. Single use, expires after 120 seconds.' },
    title: { type: 'string', description: 'Purpose of this batch; include important side effects.' },
    actions: { type: 'array', items: ACTION_SCHEMA, description: '1–5 actions in order, using coordinates of the returned image. Reobserve after layout changes.' },
  },
};
export const CONTROL_SCHEMA = { type: 'object', additionalProperties: false, required: ['action'], properties: {
  action: { type: 'string', enum: ['status', 'finish', 'stop', 'settings'] },
} };
export const OUTPUT_SCHEMA = { type: 'object', additionalProperties: true, required: ['summary'], properties: {
  summary: string,
  image: { type: 'object', additionalProperties: true, properties: {
    attachmentId: string, mediaType: string, bytes: integer, width: integer, height: integer, name: string,
  } },
} };

function int(value, lo, hi, name) {
  if (!Number.isSafeInteger(value) || value < lo || value > hi) throw new Error(`${name} must be an integer in ${lo}..${hi}`);
}

/** Model JSON is untrusted; reject the WHOLE batch before any input or preview. */
export function validateBatch(args, shot, now = Date.now()) {
  if (!args || typeof args.title !== 'string' || !args.title.trim() || args.title.length > 200) throw new Error('title must contain 1..200 characters');
  if (!shot || args.captureId !== shot.captureId || now - shot.time > 120000 || now < shot.time) throw new Error('Screenshot is missing, stale, or already used; capture again.');
  if (!Array.isArray(args.actions) || args.actions.length < 1 || args.actions.length > 5) throw new Error('Use 1..5 actions per batch.');
  const permitted = new Set(Object.keys(ACTION_SCHEMA.properties));
  return args.actions.map(a => {
    if (!a || typeof a !== 'object' || Array.isArray(a) || Object.keys(a).some(k => !permitted.has(k))) throw new Error('Unknown action field.');
    if (!ACTION_SCHEMA.properties.kind.enum.includes(a.kind)) throw new Error('Unknown action kind.');
    if (typeof a.description !== 'string' || !a.description.trim() || a.description.length > 300) throw new Error('Each action needs a 1..300 character effect description.');
    if (!Array.isArray(a.risks) || a.risks.some(r => !RISK_CATEGORIES.includes(r))) throw new Error('Invalid risk categories.');
    if (['click', 'move', 'scroll', 'drag'].includes(a.kind)) {
      int(a.x, 0, shot.imageWidth - 1, 'x'); int(a.y, 0, shot.imageHeight - 1, 'y');
    }
    if (a.kind === 'drag') { int(a.toX, 0, shot.imageWidth - 1, 'toX'); int(a.toY, 0, shot.imageHeight - 1, 'toY'); }
    if (a.button !== undefined && !['left', 'right', 'middle'].includes(a.button)) throw new Error('Invalid mouse button.');
    if (a.count !== undefined) int(a.count, 1, 2, 'count');
    if (a.kind === 'type' && (typeof a.text !== 'string' || !a.text.length || a.text.length > 4000 || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/u.test(a.text))) throw new Error('Type 1..4000 characters without control codes.');
    if (a.kind === 'key' && (typeof a.keys !== 'string' || !a.keys.length || a.keys.length > 100)) throw new Error('Invalid key chord.');
    if (a.kind === 'wait') int(a.ms, 1, 5000, 'ms');
    if (a.kind === 'drag' && a.ms !== undefined) int(a.ms, 200, 3000, 'ms');
    if (a.kind === 'scroll') { int(a.dx ?? 0, -20, 20, 'dx'); int(a.dy ?? 0, -20, 20, 'dy'); }
    if (a.modifiers !== undefined && (!Array.isArray(a.modifiers) || a.modifiers.length > 3 || a.modifiers.some(k => !['ctrl','shift','alt'].includes(k)))) throw new Error('Invalid modifiers.');
    return structuredClone(a);
  });
}

export function renderResult(_args, value) {
  const { image, ...facts } = value;
  return [{ type: 'text', text: JSON.stringify(facts) }, ...(image ? [{ type: 'image', attachment: image }] : [])];
}
