using Android.App;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AvaloniaKit.Services;
using System;

namespace AvaloniaKit.Android.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  AndroidMapService — 地图覆盖层（Android 端）
//  · 原生 WebView 通过 AddContentView 覆盖在 Avalonia 视图之上，
//    ★ 顶部留出 topOffsetDip（状态栏安全区 + 标题栏），保留 Avalonia 标题栏与返回按钮；
//    DIP→px 用屏幕 density 换算，与 Avalonia RenderScaling 同源
//  · LoadDataWithBaseURL 加载共享 HTML（高德 JS API 地图 + 路线规划 + TTS 播报）
//  · 开启 Geolocation（定位需 App 具备定位权限，缺失时页面内定位失败仅提示，不影响查路线）
//  · 系统返回键/手势由 MainActivity 的 SubPageBackCallback 兜底（MapViewModel.GoBack 调 Hide）
// ══════════════════════════════════════════════════════════════════════════════
public class AndroidMapService : IMapService
{
    // Activity 延迟访问器：注册发生在 Application.OnCreate（Activity 尚未创建）
    private readonly Func<Activity> _getActivity;
    private Activity CurrentActivity => _getActivity();
    private WebView? _webView;

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? MessageReceived;

    public AndroidMapService(Func<Activity> activityProvider) => _getActivity = activityProvider;

    public void Show(string html, double topOffsetDip)
    {
        var activity = CurrentActivity;
        activity.RunOnUiThread(() =>
        {
            HideCore();

            var wv = new WebView(activity);
            var s = wv.Settings;
            s.JavaScriptEnabled = true;
            s.DomStorageEnabled = true;
            s.SetGeolocationEnabled(true);                       // ★ 允许 H5 定位
            s.MediaPlaybackRequiresUserGesture = false;
            s.MixedContentMode = MixedContentHandling.AlwaysAllow;
            wv.SetBackgroundColor(global::Android.Graphics.Color.White);
            wv.SetWebViewClient(new AppInterceptClient(this));
            wv.SetWebChromeClient(new GeoChromeClient());        // ★ 授予定位权限提示

            wv.LoadDataWithBaseURL("https://map.local/", html, "text/html", "utf-8", null);

            // ★ 顶部下移：露出 Avalonia 标题栏（DIP × density = 物理像素）
            float density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            var lp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
            {
                TopMargin = (int)Math.Round(topOffsetDip * density),
            };
            activity.AddContentView(wv, lp);
            _webView = wv;
        });
    }

    public void Hide() => CurrentActivity.RunOnUiThread(HideCore);

    private void HideCore()
    {
        if (_webView == null) return;
        try
        {
            _webView.LoadUrl("about:blank");
            (_webView.Parent as ViewGroup)?.RemoveView(_webView);
            _webView.Destroy();
        }
        catch { /* Activity 销毁竞态时忽略 */ }
        _webView = null;
    }

    // ── 拦截 app://：exit → ExitRequested；其余（voice 等）→ MessageReceived ──────
    private sealed class AppInterceptClient : WebViewClient
    {
        private readonly AndroidMapService _owner;
        public AppInterceptClient(AndroidMapService owner) => _owner = owner;

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            string? url = request?.Url?.ToString();
            if (url != null && url.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
            {
                if (url.StartsWith("app://exit", StringComparison.OrdinalIgnoreCase))
                    _owner.ExitRequested?.Invoke(_owner, EventArgs.Empty);
                else
                    _owner.MessageReceived?.Invoke(_owner, url);
                return true;
            }
            return false;   // 其余（高德瓦片/接口等）交给 WebView 正常处理
        }
    }

    // 授予 H5 定位权限（App 无系统定位权限时定位仍会失败，但不阻塞地图）
    private sealed class GeoChromeClient : WebChromeClient
    {
        public override void OnGeolocationPermissionsShowPrompt(
            string? origin, GeolocationPermissions.ICallback? callback)
            => callback?.Invoke(origin, true, false);
    }
}
