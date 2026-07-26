// ══════════════════════════════════════════════════════════════════════════════
//  douyin.js — 抖音覆盖层（Browser 端）
//  · 用全屏 iframe(srcdoc) 承载共享层传入的完整 HTML（脚本可执行、样式隔离）
//  · iframe 内返回按钮 postMessage('douyin-exit') → 通知 C# 退出回调
// ══════════════════════════════════════════════════════════════════════════════

let _overlay = null;
let _onExit = null;

export function douyinShow(html) {
    douyinHide();

    _overlay = document.createElement('div');
    _overlay.id = 'douyin-overlay';
    _overlay.style.cssText =
        'position:fixed;inset:0;z-index:9999;background:#000;';

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

function onMessage(e) {
    if (e.data === 'douyin-exit' && _onExit) _onExit();
}
