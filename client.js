window.__ModuleLoader__.load({
  id: '@local/dsh-whale-computer-use',
  factory(require) {
    const React = require('react');
    const h = React.createElement;
    const NS = 'whaleComputerUse.settings';
    const dictionaries = {
      zh: { nav: '鲸鱼娘桌面操控', title: '鲸鱼娘桌面操控', description: '设置哪些操作属于敏感操作，以及是否允许自动执行。', open: '打开设置', opening: '正在打开…', opened: '设置窗口已打开。保存后可返回这里刷新状态。', refresh: '刷新状态', modeOn: '敏感操作：允许自动执行', modeOff: '敏感操作：等待用户手动完成', categoryCount: '已勾选的敏感类别数', shortcuts: 'Ctrl+Alt+Esc 急停 · Ctrl+Shift+I 插话', manual: '设置由你在本机窗口中修改，无需向模型发送消息。', failed: '无法打开本机设置。请检查助手是否运行；退出过助手时，重启 DSH 后重试。' },
      en: { nav: 'Whale Computer Use', title: 'Whale Computer Use', description: 'Choose which actions are sensitive and whether to execute them automatically.', open: 'Open settings', opening: 'Opening…', opened: 'The native settings window is open. Refresh the status after saving.', refresh: 'Refresh status', modeOn: 'Sensitive actions: automatic', modeOff: 'Sensitive actions: manual', categoryCount: 'Selected sensitive categories', shortcuts: 'Ctrl+Alt+Esc: stop · Ctrl+Shift+I: interject', manual: 'Change preferences in the local window. No model request is needed.', failed: 'Could not open local settings. Check the helper; restart DSH if you exited the helper.' },
    };
    const css = '.whale-settings{max-width:760px;display:flex;flex-direction:column;gap:14px;color:var(--dsw-alias-label-primary)}.whale-settings h2{margin:0;font-size:18px;font-weight:600}.whale-settings p{margin:0;line-height:1.6;font-size:13px}.whale-settings .secondary{color:var(--dsw-alias-label-tertiary)}.whale-settings .actions{display:flex;gap:10px;flex-wrap:wrap}.whale-settings button{font:inherit;cursor:pointer;border:.5px solid var(--dsw-alias-border-l2);border-radius:8px;padding:8px 14px;color:var(--dsw-alias-label-primary);background:var(--dsw-alias-bg-l1)}.whale-settings button:disabled{opacity:.5;cursor:wait}.whale-settings button:focus-visible{outline:2px solid var(--dsw-alias-state-business-primary);outline-offset:2px}';
    function SettingsSection({ t, request }) {
      const [busy, setBusy] = React.useState(false);
      const [status, setStatus] = React.useState(null);
      const [notice, setNotice] = React.useState('');
      const [error, setError] = React.useState('');
      React.useEffect(() => {
        const controller = new AbortController();
        request('status', controller.signal).then(setStatus).catch(e => { if (!controller.signal.aborted) setError(e.message); });
        return () => controller.abort();
      }, [request]);
      const run = async operation => {
        setBusy(true); setError(''); setNotice('');
        try { const result = await request(operation); if (operation === 'status') setStatus(result); else setNotice(t('opened')); }
        catch (e) { setError(`${t('failed')} ${e.message}`); }
        finally { setBusy(false); }
      };
      return h('section', { className: 'whale-settings' },
        h('style', null, css),
        h('h2', null, t('title')),
        h('p', null, t('description')),
        status?.settings && h('p', null, t(status.settings.AutoSensitive ? 'modeOn' : 'modeOff'), ' · ', t('categoryCount'), ': ', status.settings.SensitiveCategories.length),
        h('div', { className: 'actions' },
          h('button', { type: 'button', disabled: busy, 'data-whale-action': 'open-settings', onClick: () => run('open-settings') }, t(busy ? 'opening' : 'open')),
          h('button', { type: 'button', disabled: busy, onClick: () => run('status') }, t('refresh'))),
        h('p', { className: 'secondary' }, t('manual')),
        h('p', { className: 'secondary' }, t('shortcuts')),
        notice && h('p', { role: 'status' }, notice),
        error && h('p', { role: 'alert' }, error));
    }
    return {
      inject: ['slots', 'locale', 'connection'],
      apply(ctx) {
        ctx.effect(() => ctx.locale.register(NS, dictionaries));
        const t = ctx.locale.bind(NS);
        const request = async (operation, signal) => {
          const result = await ctx.connection.rpc.call('/api', `whale-computer-use/${operation}`, {}, signal);
          if (!result.ok) throw new Error(result.error.message);
          return result.value;
        };
        ctx.slots.inject('settings.section', () => ctx.slots.register({
          name: 'settings.section', id: 'whale-computer-use', order: 35,
          label: () => t('nav'), locale: NS, inject: () => ({ request }),
        }, SettingsSection));
      },
    };
  },
});
