using System;

namespace AvaloniaKit.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  IMapService — 地图覆盖层（仿高德）
//  · 地图界面由共享层一份内嵌 HTML 还原（高德 JS API 地图 + 路线规划 + 语音播报），
//    各平台用自己的 Web 承载能力显示：
//      Browser → DOM iframe 覆盖层（srcdoc，配套 map.js）
//      Android → 原生 WebView（AddContentView 覆盖）
//      iOS     → WKWebView（AddSubview 覆盖）
//      Desktop → WebView2（HWND 子窗口，Windows）
//  · 覆盖层从 topOffsetDip（状态栏安全区 + 标题栏高度，DIP）以下开始，
//    顶部保留 Avalonia 标题栏与统一返回按钮（与抖音同一布局契约）
//  · ExitRequested：HTML 内触发退出（app://exit）
//  · MessageReceived：HTML 内业务消息（app://voice?id=… 切换语音包），平台层拦截
//    app:// 导航（Desktop/Android/iOS）或 postMessage（Browser iframe）后上报
// ══════════════════════════════════════════════════════════════════════════════
public interface IMapService
{
    /// <summary>显示地图覆盖层（html = 完整页面内容；topOffsetDip = 顶部预留高度，DIP）</summary>
    void Show(string html, double topOffsetDip);

    /// <summary>移除覆盖层并释放资源</summary>
    void Hide();

    /// <summary>用户在 HTML 内触发退出（可能来自后台线程，需自行调度回 UI 线程）</summary>
    event EventHandler? ExitRequested;

    /// <summary>HTML 内业务消息（完整 app:// URL，如 app://voice?id=…；可能来自后台线程）</summary>
    event EventHandler<string>? MessageReceived;
}
