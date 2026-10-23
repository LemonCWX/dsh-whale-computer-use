import { spawn } from 'node:child_process';
import { EventEmitter } from 'node:events';
import { StringDecoder } from 'node:string_decoder';
import { fileURLToPath } from 'node:url';

/** Private stdio transport: no listening port, browser endpoint, or model-controlled executable. */
export class NativeBridge extends EventEmitter {
  constructor({ executable = fileURLToPath(new URL('./native/WhaleComputer-1.1.0.exe', import.meta.url)), settingsPath, testSurface } = {}) {
    super(); this.executable = executable; this.settingsPath = settingsPath; this.testSurface = testSurface;
    this.pending = new Map(); this.nextId = 0; this.child = null; this.starting = null; this.closed = false;
  }
  async start() {
    if (this.closed) throw new Error('Plugin is disposed.');
    if (this.starting) return this.starting;
    if (this.child) return;
    this.starting = new Promise((resolve, reject) => {
      const argv = [...(this.settingsPath ? ['--settings', this.settingsPath] : []), ...(this.testSurface ? ['--test-surface', this.testSurface] : [])];
      const child = spawn(this.executable, argv, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
      this.child = child;
      const decoder = new StringDecoder('utf8'); let buffer = ''; let ready = false;
      const timer = setTimeout(() => { reject(new Error('Native helper startup timed out.')); child.kill(); }, 10000);
      child.stdout.on('data', bytes => {
        buffer += decoder.write(bytes);
        if (buffer.length > 32 * 1024 * 1024) { child.kill(); return; }
        let p;
        while ((p = buffer.indexOf('\n')) !== -1) {
          const line = buffer.slice(0, p); buffer = buffer.slice(p + 1); if (!line.trim()) continue;
          let msg;
          try { msg = JSON.parse(line); } catch { child.kill(); return; }
          if (msg.event === 'ready') { ready = true; clearTimeout(timer); resolve(msg); }
          if (msg.event) { this.emit('event', msg); continue; }
          const pending = this.pending.get(msg.id);
          if (!pending) continue;
          this.pending.delete(msg.id); pending.cleanup();
          if (msg.ok) pending.resolve(msg.result); else pending.reject(new Error(msg.error || 'Native helper error'));
        }
      });
      let stderr = '';
      child.stderr.on('data', c => { stderr = (stderr + c.toString()).slice(-3000); });
      const lost = error => {
        clearTimeout(timer); if (this.child === child) this.child = null;
        if (ready) this.closed = true; // User exit or a crash must not silently re-arm desktop control.
        const failure = error || new Error(`Native helper exited${stderr ? ': ' + stderr.trim() : ''}`);
        if (!ready) reject(failure);
        for (const p of this.pending.values()) { p.cleanup(); p.reject(failure); }
        this.pending.clear(); this.emit('event', { event: 'disconnected' });
      };
      child.once('error', lost); child.once('exit', () => lost());
      child.stdin.on('error', () => { /* Process exit owns rejection of outstanding calls. */ });
    }).finally(() => { this.starting = null; });
    return this.starting;
  }
  async request(op, payload = {}, { signal, timeout = 15000 } = {}) {
    signal?.throwIfAborted(); await this.start(); signal?.throwIfAborted();
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      const cancel = () => {
        if (!this.pending.delete(id)) return;
        cleanup();
        this.child?.stdin.write(JSON.stringify({ id: ++this.nextId, op: 'cancel' }) + '\n');
        reject(signal?.reason instanceof Error ? signal.reason : new Error('Desktop operation cancelled.'));
      };
      const timer = setTimeout(() => {
        if (!this.pending.delete(id)) return;
        cleanup(); this.child?.stdin.write(JSON.stringify({ id: ++this.nextId, op: 'stop' }) + '\n');
        reject(new Error(`Native ${op} timed out; desktop control stopped. Do not retry an uncertain input.`));
      }, timeout);
      const cleanup = () => { clearTimeout(timer); signal?.removeEventListener('abort', cancel); };
      this.pending.set(id, { resolve, reject, cleanup }); signal?.addEventListener('abort', cancel, { once: true });
      this.child.stdin.write(JSON.stringify({ ...payload, id, op }) + '\n');
      if (signal?.aborted) cancel();
    });
  }
  async dispose() {
    this.closed = true;
    const child = this.child;
    if (!child) return;
    child.stdin.write(JSON.stringify({ id: ++this.nextId, op: 'shutdown' }) + '\n');
    await new Promise(resolve => {
      const timer = setTimeout(() => { child.kill(); resolve(); }, 2500);
      child.once('exit', () => { clearTimeout(timer); resolve(); });
    });
  }
}
