using Android.App;
using Android.Speech.Tts;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AvaloniaKit.Services;
using Java.Interop;
using System;

namespace AvaloniaKit.Android.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  AndroidMapService — 地图覆盖层（Android 端）
//  · 原生 WebView 通过 AddContentView 覆盖在 Avalonia 视图之上，
//    ★ 顶部留出 topOffsetDip（状态栏安全区 + 标题栏），保留 Avalonia 标题栏与返回按钮；
//    DIP→px 用屏幕 density 换算，与 Avalonia RenderScaling 同源
//  · LoadDataWithBaseURL 加载共享 HTML（高德 JS API 地图 + 路线规划 + TTS 播报）
//  · 开启 Geolocation；Show 时运行时请求定位权限（Android 6+），H5 定位才能拿到真实 GPS
//  · ★ 原生 TTS 桥（window.NativeTts）：Android WebView 不支持 Web Speech API，
//    导航语音经 TextToSpeech 播报（页面侧自动优先走桥，其余端仍用 speechSynthesis）
//  · 系统返回键/手势由 MainActivity 的 SubPageBackCallback 兜底（MapViewModel.GoBack 调 Hide）
// ══════════════════════════════════════════════════════════════════════════════
public class AndroidMapService : IMapService
{
    // Activity 延迟访问器：注册发生在 Application.OnCreate（Activity 尚未创建）
    private readonly Func<Activity> _getActivity;
    private Activity CurrentActivity => _getActivity();
    private WebView? _webView;
    private NativeTtsBridge? _tts;

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? MessageReceived;

    public AndroidMapService(Func<Activity> activityProvider) => _getActivity = activityProvider;

    public void Show(string html, double topOffsetDip)
    {
        var activity = CurrentActivity;
        activity.RunOnUiThread(() =>
        {
            HideCore();

            // ★ 运行时请求定位权限（Android 6+）：WebView 内 H5 定位拿真实 GPS 的前提
            if (OperatingSystem.IsAndroidVersionAtLeast(23) &&
                activity.CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation)
                    != global::Android.Content.PM.Permission.Granted)
            {
                activity.RequestPermissions(new[]
                {
                    global::Android.Manifest.Permission.AccessFineLocation,
                    global::Android.Manifest.Permission.AccessCoarseLocation,
                }, 9001);
            }

            var wv = new WebView(activity);
            // ★ Debug 构建开启 WebView 远程调试（chrome://inspect 排查页面），Release 关闭
#if DEBUG
            try { WebView.SetWebContentsDebuggingEnabled(true); } catch { }
#endif
            var s = wv.Settings;
            s.JavaScriptEnabled = true;
            s.DomStorageEnabled = true;
            s.SetGeolocationEnabled(true);                       // ★ 允许 H5 定位
            s.MediaPlaybackRequiresUserGesture = false;
            s.MixedContentMode = MixedContentHandling.AlwaysAllow;
            wv.SetBackgroundColor(global::Android.Graphics.Color.White);
            wv.SetWebViewClient(new AppInterceptClient(this));
            wv.SetWebChromeClient(new GeoChromeClient());        // ★ 授予定位权限提示

            // ★ 原生 TTS 桥：导航语音经 TextToSpeech 播报
            _tts?.Shutdown();
            _tts = new NativeTtsBridge(activity);
            wv.AddJavascriptInterface(_tts, "NativeTts");

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
            // ★ 地图/导航期间屏幕常亮（导航工具刚需：避免燄屏中断行程）
            try { activity.Window?.AddFlags(WindowManagerFlags.KeepScreenOn); } catch { }
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
        // ★ 退出地图：取消屏幕常亮，恢复系统默认息屏策略
        try { CurrentActivity.Window?.ClearFlags(WindowManagerFlags.KeepScreenOn); } catch { }
        _tts?.Shutdown();
        _tts = null;
    }

    // ── 原生 TTS 桥：Android WebView 无 Web Speech API，页面经 window.NativeTts 播报 ──────
    private sealed class NativeTtsBridge : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        private TextToSpeech? _tts;
        private bool _ready;

        public NativeTtsBridge(Activity activity) => _tts = new TextToSpeech(activity, this);

        public void OnInit(OperationResult status)
        {
            if (status == OperationResult.Success && _tts != null)
            {
                try { _tts.SetLanguage(Java.Util.Locale.SimplifiedChinese); _ready = true; }
                catch { /* 缺少中文语音包时静默，页面回退 speechSynthesis */ }
            }
        }

        [JavascriptInterface]
        [Export("speak")]
        public void Speak(string text, float rate, float pitch)
        {
            if (!_ready || _tts == null) return;
            try
            {
                _tts.SetSpeechRate(rate);
                _tts.SetPitch(pitch);
                _tts.Speak(text, QueueMode.Add, null, "nav" + Environment.TickCount);
            }
            catch { }
        }

        [JavascriptInterface]
        [Export("stopSpeak")]
        public void StopSpeak() { try { _tts?.Stop(); } catch { } }

        public void Shutdown()
        {
            try { _tts?.Stop(); _tts?.Shutdown(); } catch { }
            _tts = null; _ready = false;
        }
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
