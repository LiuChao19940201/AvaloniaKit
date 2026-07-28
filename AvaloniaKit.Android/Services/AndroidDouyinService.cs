using Android.App;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AvaloniaKit.Services;
using System;

namespace AvaloniaKit.Android.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  AndroidDouyinService — 抖音覆盖层（Android 端）
//  · 原生 WebView 通过 AddContentView 覆盖在 Avalonia 视图之上，
//    ★ 顶部留出 topOffsetDip（状态栏安全区 + 标题栏），保留 Avalonia 标题栏
//    与统一样式返回按钮（与音乐模块一致）；DIP→px 用屏幕 density 换算，
//    与 Avalonia 的 RenderScaling 同源，两层像素对齐
//  · LoadDataWithBaseURL 加载共享 HTML（video 标签由系统 WebView 播放）
//  · 允许自动播放（MediaPlaybackRequiresUserGesture = false）
//  · 系统返回键/手势由 MainActivity 的 SubPageBackCallback 兜底（共享层
//    DouyinViewModel.GoBack 会调用 Hide 移除本覆盖层）
// ══════════════════════════════════════════════════════════════════════════════
public class AndroidDouyinService : IDouyinService
{
    // Activity 延迟访问器：注册发生在 Application.OnCreate（Activity 尚未创建），
    // Show/Hide 均在 MainActivity 就绪后由 UI 交互触发
    private readonly Func<Activity> _getActivity;
    private Activity CurrentActivity => _getActivity();
    private WebView? _webView;

    public event EventHandler? ExitRequested;

    public AndroidDouyinService(Func<Activity> activityProvider) => _getActivity = activityProvider;

    public void Show(string html, double topOffsetDip)
    {
        var activity = CurrentActivity;
        activity.RunOnUiThread(() =>
        {
            HideCore();

            var wv = new WebView(activity);
            var s = wv.Settings;
            s.JavaScriptEnabled = true;
            s.MediaPlaybackRequiresUserGesture = false;   // ★ 允许 video 自动播放
            s.DomStorageEnabled = true;
            s.MixedContentMode = MixedContentHandling.AlwaysAllow; // CDN 可能是 http
            wv.SetBackgroundColor(global::Android.Graphics.Color.Black);
            wv.SetWebViewClient(new ExitInterceptClient(this));
            // 视频源 API 需要 302 跳转，WebChromeClient 保证 video 全功能
            wv.SetWebChromeClient(new WebChromeClient());

            wv.LoadDataWithBaseURL("https://douyin.local/", html,
                "text/html", "utf-8", null);

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
            _webView.LoadUrl("about:blank");   // 停掉视频/音频
            (_webView.Parent as ViewGroup)?.RemoveView(_webView);
            _webView.Destroy();
        }
        catch { /* Activity 销毁竞态时忽略 */ }
        _webView = null;
    }

    // ── 拦截 app://exit：HTML 内返回按钮 ────────────────────────────────────
    private sealed class ExitInterceptClient : WebViewClient
    {
        private readonly AndroidDouyinService _owner;
        public ExitInterceptClient(AndroidDouyinService owner) => _owner = owner;

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            string? url = request?.Url?.ToString();
            if (url != null && url.StartsWith("app://exit", StringComparison.OrdinalIgnoreCase))
            {
                _owner.ExitRequested?.Invoke(_owner, EventArgs.Empty);
                return true;
            }
            return false;   // 其余（视频 302 等）交给 WebView 正常处理
        }
    }
}
