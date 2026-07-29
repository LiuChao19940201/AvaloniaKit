using AvaloniaKit.Services;
using CoreGraphics;
using Foundation;
using System;
using UIKit;
using WebKit;

namespace AvaloniaKit.iOS.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  IosMapService — 地图覆盖层（iOS 端）
//  · WKWebView 以子视图覆盖在 Avalonia 视图之上，顶部留出 topOffsetDip
//    （iOS point 即 DIP，无需换算），保留 Avalonia 标题栏与统一样式返回按钮
//  · LoadHtmlString 加载共享 HTML（高德 JS API 地图 + 路线规划 + TTS 播报）
//  · 拦截 app://：exit → ExitRequested；其余（voice 等）→ MessageReceived
//  · 本平台无法在 Windows 环境运行验证，实现严格对齐 Android 端行为契约
// ══════════════════════════════════════════════════════════════════════════════
public class IosMapService : IMapService
{
    // UIWindow 延迟访问器：注册发生在组合根构建时（窗口尚未创建）
    private readonly Func<UIWindow?> _getWindow;
    private WKWebView? _webView;

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? MessageReceived;

    public IosMapService(Func<UIWindow?> windowProvider)
    {
        _getWindow = windowProvider;
    }

    public void Show(string html, double topOffsetDip)
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var window = _getWindow();
            if (window is null) return;

            HideCore();

            try
            {
                var config = new WKWebViewConfiguration
                {
                    AllowsInlineMediaPlayback = true,
                    MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None,
                };

                // ★ 顶部下移：露出 Avalonia 标题栏（iOS point 与 DIP 同单位）
                var bounds = window.Bounds;
                var frame = new CGRect(
                    0, topOffsetDip,
                    bounds.Width, bounds.Height - topOffsetDip);

                var wv = new WKWebView(frame, config)
                {
                    BackgroundColor = UIColor.White,
                    Opaque = true,
                    AutoresizingMask = UIViewAutoresizing.FlexibleWidth
                                     | UIViewAutoresizing.FlexibleHeight,
                    NavigationDelegate = new AppInterceptDelegate(this),
                };

                wv.LoadHtmlString(html, new NSUrl("https://map.local/"));

                window.AddSubview(wv);
                _webView = wv;
            }
            catch { /* WebKit 不可用时静默（共享层按 HasService 显示占位） */ }
        });
    }

    public void Hide() => UIApplication.SharedApplication.InvokeOnMainThread(HideCore);

    private void HideCore()
    {
        if (_webView == null) return;
        try
        {
            _webView.LoadHtmlString("", new NSUrl("about:blank"));
            _webView.RemoveFromSuperview();
            _webView.Dispose();
        }
        catch { /* 窗口销毁竞态时忽略 */ }
        _webView = null;
    }

    // ── 拦截 app://：exit → ExitRequested；其余（voice 等）→ MessageReceived ──────
    private sealed class AppInterceptDelegate : WKNavigationDelegate
    {
        private readonly IosMapService _owner;
        public AppInterceptDelegate(IosMapService owner) => _owner = owner;

        public override void DecidePolicy(
            WKWebView webView, WKNavigationAction navigationAction,
            Action<WKNavigationActionPolicy> decisionHandler)
        {
            string? url = navigationAction.Request?.Url?.AbsoluteString;
            if (url != null && url.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
            {
                if (url.StartsWith("app://exit", StringComparison.OrdinalIgnoreCase))
                    _owner.ExitRequested?.Invoke(_owner, EventArgs.Empty);
                else
                    _owner.MessageReceived?.Invoke(_owner, url);
                decisionHandler(WKNavigationActionPolicy.Cancel);
                return;
            }
            decisionHandler(WKNavigationActionPolicy.Allow);   // 其余（高德瓦片/接口等）放行
        }
    }
}
