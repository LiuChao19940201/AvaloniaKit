// ══════════════════════════════════════════════════════════════════════════════
//  map.js — 地图覆盖层（Browser 端）
//  · 用 iframe(srcdoc) 承载共享层传入的完整 HTML（高德 JS API 地图 + 路线 + TTS）
//  · ★ 顶部留出 topPx（状态栏安全区 + 标题栏），保留 Avalonia 标题栏与返回按钮
//    （CSS px ≡ Avalonia DIP，无需换算）
//  · iframe 内 postMessage('map-exit')        → 通知 C# 退出回调（保留通道）
//  · iframe 内 postMessage('map-msg:app://…') → 通知 C# 业务消息（切换语音包）
// ══════════════════════════════════════════════════════════════════════════════

let _overlay = null;
let _onExit = null;
let _onMessage = null;

export function mapShow(html, topPx) {
    mapHide();

    _overlay = document.createElement('div');
    _overlay.id = 'map-overlay';
    _overlay.style.cssText =
        'position:fixed;top:' + (topPx || 0) + 'px;left:0;right:0;bottom:0;' +
        'z-index:9999;background:#e6e8eb;';

    const frame = document.createElement('iframe');
    frame.style.cssText = 'width:100%;height:100%;border:none;display:block;';
    frame.setAttribute('allow', 'geolocation; autoplay; fullscreen');
    frame.srcdoc = html;

    _overlay.appendChild(frame);
    document.body.appendChild(_overlay);

    window.addEventListener('message', onMessage);
}

export function mapHide() {
    window.removeEventListener('message', onMessage);
    if (_overlay) {
        _overlay.remove();
        _overlay = null;
    }
}

export function mapSetExitCallback(cb) {
    _onExit = cb;
}

export function mapSetMessageCallback(cb) {
    _onMessage = cb;
}

function onMessage(e) {
    if (typeof e.data !== 'string') return;
    if (e.data === 'map-exit' && _onExit) _onExit();
    else if (e.data.startsWith('map-msg:') && _onMessage)
        _onMessage(e.data.substring('map-msg:'.length));
}
