using System;

namespace AvaloniaKit.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  IDouyinService — 抖音短视频覆盖层
//  · 抖音界面由共享层的一份内嵌 HTML 还原（视频 + 右侧操作栏 + 文案 + 手势），
//    各平台用自己的 Web 承载能力显示：
//      Browser → DOM iframe 覆盖层（srcdoc）
//      Android → 原生 WebView（AddContentView 全屏覆盖）
//      Desktop → WebView2（HWND 子窗口，Windows）
//  · HTML 内返回按钮统一触发 app://exit 导航（iframe 场景走 postMessage），
//    平台层拦截后触发 ExitRequested，由 DouyinViewModel 执行返回导航
// ══════════════════════════════════════════════════════════════════════════════
public interface IDouyinService
{
    /// <summary>显示抖音覆盖层（html = 完整页面内容）</summary>
    void Show(string html);

    /// <summary>移除覆盖层并释放资源</summary>
    void Hide();

    /// <summary>用户在 HTML 内点击返回（可能来自后台线程，需自行调度回 UI 线程）</summary>
    event EventHandler? ExitRequested;
}
