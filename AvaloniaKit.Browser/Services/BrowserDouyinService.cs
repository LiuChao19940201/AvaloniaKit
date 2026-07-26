using AvaloniaKit.Services;
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace AvaloniaKit.Browser.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  BrowserDouyinService — 抖音覆盖层（Browser 端）
//  通过 douyin.js 在 DOM 上盖一层全屏 iframe（srcdoc = 共享 HTML）。
//  依赖：Program.cs 中 await JSHost.ImportAsync("douyin", "/douyin.js")
// ══════════════════════════════════════════════════════════════════════════════
[SupportedOSPlatform("browser")]
public partial class BrowserDouyinService : IDouyinService
{
    [JSImport("douyinShow", "douyin")] private static partial void JsShow(string html);
    [JSImport("douyinHide", "douyin")] private static partial void JsHide();

    [JSImport("douyinSetExitCallback", "douyin")]
    private static partial void JsSetExitCallback(
        [JSMarshalAs<JSType.Function>] Action onExit);

    public event EventHandler? ExitRequested;

    public BrowserDouyinService()
    {
        JsSetExitCallback(() => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    public void Show(string html) => JsShow(html);

    public void Hide() => JsHide();
}
