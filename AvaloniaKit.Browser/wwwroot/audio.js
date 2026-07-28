// ══════════════════════════════════════════════════════════════════════════════
//  audio.js  （修复版）
//  放置于 Browser 项目的 wwwroot/audio.js
//
//  修复点：
//  1. audioSetCallbacks 新增第4个参数 onCanPlay（canplaythrough 回调）
//  2. audio.oncanplaythrough → 调用 onCanPlay，通知 C# 播放已就绪
//  3. audio.onerror 回调传递详细错误信息
//  4. 进度轮询改为使用 ontimeupdate 事件，更精准且省电
// ══════════════════════════════════════════════════════════════════════════════

let _audio = null;
let _onProgress = null;
let _onEnded    = null;
let _onError    = null;
let _onCanPlay  = null;   // ★ 新增

function ensureAudio() {
    if (_audio) return;
    _audio = new Audio();
    // ★ 不设 crossOrigin：网易 CDN 不返回 CORS 头，设了 anonymous 会导致
    //   浏览器拒绝加载音频（MediaError）；默认 no-cors 模式可正常播放并跟随 302

    // ★ 用 ontimeupdate 替代 setInterval，更高效
    _audio.ontimeupdate = () => {
        if (_onProgress && _audio.duration) {
            _onProgress(
                _audio.currentTime * 1000,
                _audio.duration * 1000
            );
        }
    };

    _audio.onended = () => {
        if (_onEnded) _onEnded();
    };

    _audio.onerror = () => {
        const err = _audio.error;
        const msg = err
            ? `MediaError code=${err.code} message=${err.message}`
            : "Unknown audio error";
        if (_onError) _onError(msg);
    };

    // ★ canplaythrough：缓冲足够，可以流畅播放
    _audio.oncanplaythrough = () => {
        if (_onCanPlay) _onCanPlay();
    };
}

// ─── 导出函数（供 C# JSImport 调用）────────────────────────────────────────

export function audioPlay(url) {
    ensureAudio();
    _audio.src = url;
    _audio.load();
    _audio.play().catch(e => {
        // autoplay policy 拦截时，记录错误
        if (_onError) _onError("AutoPlay blocked: " + e.message);
    });
}

export function audioPause() {
    if (_audio) _audio.pause();
}

export function audioResume() {
    if (_audio) _audio.play().catch(() => {});
}

export function audioStop() {
    if (!_audio) return;
    _audio.pause();
    _audio.src = "";
    _audio.load();
}

export function audioSeek(ms) {
    if (_audio && _audio.duration)
        _audio.currentTime = ms / 1000;
}

export function audioSetVolume(v) {
    if (_audio) _audio.volume = Math.max(0, Math.min(1, v));
}

export function audioGetCurrentMs() {
    return _audio ? _audio.currentTime * 1000 : 0;
}

export function audioGetDurationMs() {
    return (_audio && _audio.duration && !isNaN(_audio.duration))
        ? _audio.duration * 1000 : 0;
}

export function audioIsPlaying() {
    return _audio ? !_audio.paused && !_audio.ended : false;
}

// ★ 修复：新增第4个参数 onCanPlay
export function audioSetCallbacks(onProgress, onEnded, onError, onCanPlay) {
    _onProgress = onProgress;
    _onEnded    = onEnded;
    _onError    = onError;
    _onCanPlay  = onCanPlay;   // ★
    ensureAudio();
}

// ─── 设备反馈（BrowserDeviceService 用，与移动端音效/震动三端一致）────────────

let _beepCtx = null;

// WebAudio 振荡器合成短促提示音（对齐 Android ToneGenerator PropBeep）
export function deviceBeep() {
    try {
        _beepCtx = _beepCtx || new (window.AudioContext || window.webkitAudioContext)();
        const osc  = _beepCtx.createOscillator();
        const gain = _beepCtx.createGain();
        osc.type = "sine";
        osc.frequency.value = 880;
        gain.gain.setValueAtTime(0.15, _beepCtx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.001, _beepCtx.currentTime + 0.12);
        osc.connect(gain).connect(_beepCtx.destination);
        osc.start();
        osc.stop(_beepCtx.currentTime + 0.12);
    } catch (e) { /* 音频上下文被策略拦截时静默 */ }
}

// navigator.vibrate：移动端浏览器有效，桌面浏览器静默无效
export function deviceVibrate(ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) { }
}

