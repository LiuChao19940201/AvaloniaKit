// ══════════════════════════════════════════════════════════════════════════════
//  douyin.js — 抖音覆盖层（Browser 端）
//  · 用 iframe(srcdoc) 承载共享层传入的完整 HTML（脚本可执行、样式隔离）
//  · ★ 顶部留出 topPx（状态栏安全区 + 标题栏），保留 Avalonia 标题栏与
//    统一样式返回按钮（CSS px ≡ Avalonia DIP，无需换算）
//  · iframe 内 postMessage('douyin-exit') → 通知 C# 退出回调（保留通道）
//  · iframe 内 postMessage('douyin-msg:app://…') → 通知 C# 业务消息（关注/取关）
// ══════════════════════════════════════════════════════════════════════════════

let _overlay = null;
let _onExit = null;
let _onMessage = null;

export function douyinShow(html, topPx) {
    douyinHide();

    _overlay = document.createElement('div');
    _overlay.id = 'douyin-overlay';
    _overlay.style.cssText =
        'position:fixed;top:' + (topPx || 0) + 'px;left:0;right:0;bottom:0;' +
        'z-index:9999;background:#000;';

    const frame = document.createElement('iframe');
    frame.style.cssText = 'width:100%;height:100%;border:none;display:block;';
    frame.setAttribute('allow', 'autoplay; fullscreen');
    frame.srcdoc = html;

    _overlay.appendChild(frame);
    document.body.appendChild(_overlay);

    window.addEventListener('message', onMessage);
}

export function douyinHide() {
    window.removeEventListener('message', onMessage);
    if (_overlay) {
        _overlay.remove();   // iframe 移除后 video 自动停止
        _overlay = null;
    }
}

export function douyinSetExitCallback(cb) {
    _onExit = cb;
}

export function douyinSetMessageCallback(cb) {
    _onMessage = cb;
}

function onMessage(e) {
    if (typeof e.data !== 'string') return;
    if (e.data === 'douyin-exit' && _onExit) _onExit();
    else if (e.data.startsWith('douyin-msg:') && _onMessage)
        _onMessage(e.data.substring('douyin-msg:'.length));
}
