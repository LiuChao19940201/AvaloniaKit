using AvaloniaKit.Services;
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace AvaloniaKit.Browser.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  BrowserMapService — 地图覆盖层（Browser 端）
//  通过 map.js 在 DOM 上盖一层 iframe（srcdoc = 共享 HTML），
//  ★ 顶部留出 topOffsetDip（CSS px ≡ DIP），保留 Avalonia 标题栏与返回按钮。
//  依赖：Program.cs 中 await JSHost.ImportAsync("map", "/map.js")
// ══════════════════════════════════════════════════════════════════════════════
[SupportedOSPlatform("browser")]
public partial class BrowserMapService : IMapService
{
    [JSImport("mapShow", "map")] private static partial void JsShow(string html, double topPx);
    [JSImport("mapHide", "map")] private static partial void JsHide();

    [JSImport("mapSetExitCallback", "map")]
    private static partial void JsSetExitCallback(
        [JSMarshalAs<JSType.Function>] Action onExit);

    [JSImport("mapSetMessageCallback", "map")]
    private static partial void JsSetMessageCallback(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onMessage);

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? MessageReceived;

    public BrowserMapService()
    {
        JsSetExitCallback(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        // iframe 内 postMessage('map-msg:app://…') → 业务消息（切换语音包）
        JsSetMessageCallback(uri => MessageReceived?.Invoke(this, uri));
    }

    public void Show(string html, double topOffsetDip) => JsShow(html, topOffsetDip);

    public void Hide() => JsHide();
}
