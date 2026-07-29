using AvaloniaKit.Services;
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace AvaloniaKit.Browser.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  BrowserDouyinService — 抖音覆盖层（Browser 端）
//  通过 douyin.js 在 DOM 上盖一层 iframe（srcdoc = 共享 HTML），
//  ★ 顶部留出 topOffsetDip（CSS px ≡ DIP），保留 Avalonia 标题栏与返回按钮。
//  依赖：Program.cs 中 await JSHost.ImportAsync("douyin", "/douyin.js")
// ══════════════════════════════════════════════════════════════════════════════
[SupportedOSPlatform("browser")]
public partial class BrowserDouyinService : IDouyinService
{
    [JSImport("douyinShow", "douyin")] private static partial void JsShow(string html, double topPx);
    [JSImport("douyinHide", "douyin")] private static partial void JsHide();

    [JSImport("douyinSetExitCallback", "douyin")]
    private static partial void JsSetExitCallback(
        [JSMarshalAs<JSType.Function>] Action onExit);

    [JSImport("douyinSetMessageCallback", "douyin")]
    private static partial void JsSetMessageCallback(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onMessage);

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? MessageReceived;

    public BrowserDouyinService()
    {
        JsSetExitCallback(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        // iframe 内 postMessage('douyin-msg:app://…') → 业务消息（关注/取关）
        JsSetMessageCallback(uri => MessageReceived?.Invoke(this, uri));
    }

    public void Show(string html, double topOffsetDip) => JsShow(html, topOffsetDip);

    public void Hide() => JsHide();
}
