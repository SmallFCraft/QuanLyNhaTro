// Cầu nối JS -> C# qua window.chrome.webview.postMessage với requestId.
window.api = (() => {
  let seq = 0;
  const pending = new Map();

  window.chrome?.webview?.addEventListener('message', ev => {
    const resp = ev.data;
    if (resp && resp.requestId && pending.has(resp.requestId)) {
      pending.get(resp.requestId)(resp);
      pending.delete(resp.requestId);
    }
  });

  function call(action, data) {
    const requestId = 'r' + Date.now().toString(36) + '-' + (++seq);
    return new Promise(resolve => {
      pending.set(requestId, resolve);
      window.chrome.webview.postMessage({ requestId, action, data: data ?? {} });
      setTimeout(() => {
        if (pending.has(requestId)) {
          pending.delete(requestId);
          resolve({ requestId, success: false, message: 'Hết thời gian chờ server.', data: null });
        }
      }, 20000);
    });
  }

  return { call };
})();
