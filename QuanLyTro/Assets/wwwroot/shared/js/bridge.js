/**
 * shared/js/bridge.js — WebView2 <-> .NET TCP Bridge (requestId)
 * Giữ nguyên hợp đồng WebMessageReceived của SmallFCraft/QuanLyNhaTro-LTM.
 */
(function (global) {
  const isWebView2 = Boolean(global.chrome && global.chrome.webview);
  const pending = new Map();
  if (isWebView2) {
    global.chrome.webview.addEventListener("message", function (event) {
      const res = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
      if (res && res.requestId && pending.has(res.requestId)) {
        const entry = pending.get(res.requestId);
        clearTimeout(entry.timer);
        pending.delete(res.requestId);
        if (res.success === false) entry.reject(new Error(res.message || "Lỗi server"));
        else entry.resolve(res);
      }
    });
  }
  function invoke(action, data, timeoutMs) {
    const requestId = (global.crypto && global.crypto.randomUUID)
      ? global.crypto.randomUUID()
      : "req-" + Date.now() + "-" + Math.random().toString(36).slice(2, 8);
    // NGOẠI LỆ: khi chạy trực tiếp qua HTTP (static preview) không có WebView2,
    // không được giả response thành công trộn requests với responses — đó là nguồn
    // lỗi "xxx is not iterable". Nơi nào dùng app thật thì WebView2 chuyển tiếp.
    if (!isWebView2) return Promise.reject(new Error('Không chạy trong WebView2 — hãy mở qua ứng dụng.'));
    return new Promise(function (resolve, reject) {
      const timer = setTimeout(function () { pending.delete(requestId); reject(new Error("Timeout: " + action)); }, timeoutMs || 15000);
      pending.set(requestId, { resolve, reject, timer });
      global.chrome.webview.postMessage({ requestId, action, data: data || {} });
    });
  }
  global.QuanLyTroBridge = { isWebView2, invoke };
})(window);
