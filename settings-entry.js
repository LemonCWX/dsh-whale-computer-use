const BASE = 'whale-computer-use';

/** Owner-authenticated DSH UI requests may read status or open the user-owned panel. No preference setter is published. */
export function registerSettingsEntry(ctx, native) {
  for (const [endpoint, operation] of [['status', 'status'], ['open-settings', 'settings']]) {
    const method = `${BASE}/${endpoint}`;
    ctx.connection.fetch.register({
      path: `/api/${method}`, methods: ['POST'], requestBody: 'buffered',
      async fetch(request) {
        if (request.headers.get('content-type')?.split(';', 1)[0].trim() !== 'application/json') return new Response('Expected JSON.', { status: 415 });
        let body;
        try { body = await request.json(); } catch { return new Response('Invalid JSON.', { status: 400 }); }
        if (!body || body.type !== 'client-request' || typeof body.rpcId !== 'string' || body.rpcId.length > 128 || body.method !== method
          || !body.payload || typeof body.payload !== 'object' || Array.isArray(body.payload) || Object.keys(body.payload).length) {
          return new Response('Invalid settings entry request.', { status: 400 });
        }
        let result;
        try { result = { ok: true, value: await native.request(operation, {}, { signal: request.signal }) }; }
        catch (error) { result = { ok: false, error: { code: 'whale/native-unavailable', message: error instanceof Error ? error.message : String(error), details: {} } }; }
        return Response.json({ type: 'server-response', rpcId: body.rpcId, result });
      },
    });
  }
}
