using System;

namespace AvaloniaKit.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  IDouyinService — 抖音短视频覆盖层
//  · 抖音界面由共享层的一份内嵌 HTML 还原（视频 + 右侧操作栏 + 文案 + 手势），
//    各平台用自己的 Web 承载能力显示：
//      Browser → DOM iframe 覆盖层（srcdoc）
//      Android → 原生 WebView（AddContentView 覆盖）
//      Desktop → WebView2（HWND 子窗口，Windows）
//  · ★ 覆盖层不再全屏：从 topOffsetDip（Avalonia 状态栏安全区 + 标题栏高度，
//    设备无关单位）以下开始，顶部保留与音乐模块一致的 Avalonia 标题栏与返回按钮
//  · ExitRequested 保留给平台层拦截 app://exit 等退出通道使用
// ══════════════════════════════════════════════════════════════════════════════
public interface IDouyinService
{
    /// <summary>显示抖音覆盖层（html = 完整页面内容；topOffsetDip = 顶部预留高度，DIP）</summary>
    void Show(string html, double topOffsetDip);

    /// <summary>移除覆盖层并释放资源</summary>
    void Hide();

    /// <summary>用户在 HTML 内点击返回（可能来自后台线程，需自行调度回 UI 线程）</summary>
    event EventHandler? ExitRequested;
}
