/* 启动兜底：任何未捕获的脚本错误 / 资源加载失败，直接显示在窗口上，
 * 避免出现「双击快捷方式只有一片空白、无从排查」的情况。
 * 保持零依赖、经典脚本（非 module），并在 index.html 中先于主脚本加载。 */
(function () {
  'use strict';

  function appRendered() {
    var r = document.getElementById('root');
    return !!r && r.childElementCount > 0;
  }

  function show(kind, detail) {
    if (document.getElementById('startup-error')) return;
    var d = document.createElement('div');
    d.id = 'startup-error';
    d.style.cssText =
      'position:fixed;inset:0;z-index:2147483647;background:#fff;color:#b91c1c;' +
      'font:14px/1.8 Consolas,monospace;padding:32px;overflow:auto;white-space:pre-wrap;';
    d.textContent =
      'RockCore 启动失败（' + kind + '）\n\n' + detail +
      '\n\n—— 处理建议 ——\n' +
      '1. 确认已安装「Microsoft Edge WebView2 运行时」（安装包会自动安装，若被安全软件拦截请放行后重装）；\n' +
      '2. 删除本机数据后重试：%APPDATA%\\com.rockcore.desktop 整个文件夹删除后重新打开；\n' +
      '3. 仍不行请把本屏截图反馈。';
    document.body.appendChild(d);
    var t = document.querySelector('title');
    if (t) t.textContent = 'RockCore 启动失败';
  }

  // 资源加载失败（js/css 404 等）与脚本错误
  window.addEventListener(
    'error',
    function (e) {
      if (e.target && e.target !== window && (e.target.src || e.target.href)) {
        show('资源加载失败', (e.target.src || e.target.href) + '\n（' + (e.target.tagName || '') + ' 未能加载）');
      } else {
        show(
          '脚本错误',
          (e.message || '未知错误') +
            (e.filename ? '\n' + e.filename + ':' + (e.lineno || 0) + ':' + (e.colno || 0) : '') +
            (e.error && e.error.stack ? '\n' + String(e.error.stack).slice(0, 800) : '')
        );
      }
    },
    true
  );

  // 未处理的 Promise 异常：仅在应用尚未渲染出任何界面时才拦截，
  // 应用已正常显示后的异步错误交给应用自身的提示逻辑。
  window.addEventListener('unhandledrejection', function (e) {
    if (appRendered()) return;
    var r = e.reason;
    show('未处理的启动异常', String((r && (r.stack || r.message)) || r).slice(0, 1200));
  });
})();
