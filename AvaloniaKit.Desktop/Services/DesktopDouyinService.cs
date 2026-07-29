using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaKit.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaKit.Desktop.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  DesktopDouyinService — 抖音覆盖层（Windows Desktop 端）
//  · WebView2 以 HWND 子窗口形式挂到 Avalonia 主窗口，Bounds 跟随客户区，
//    ★ 顶部留出 topOffsetDip（状态栏安全区 + 标题栏），保留 Avalonia 标题栏
//    与统一样式返回按钮；NavigateToString 加载共享 HTML
//  · 环境参数放开自动播放限制（--autoplay-policy=no-user-gesture-required）
//  · 依赖系统 WebView2 Runtime（Win10/11 预装）；缺失时静默失败，
//    标题栏返回按钮仍可退出
// ══════════════════════════════════════════════════════════════════════════════
public class DesktopDouyinService : IDouyinService
{
    private Task<CoreWebView2Environment>? _envTask;
    private CoreWebView2Controller? _controller;
    private Window? _window;
    private bool _showing;
    private int _showVersion;   // ★ 代数：快速连切 Tab 时作废旧的异步 Show 流程
    private double _topOffsetDip;

    public event EventHandler? ExitRequested;
    public event EventHandler<string>? MessageReceived;

    public DesktopDouyinService()
    {
        // ★ 启动预热：WebView2 环境创建耗时秒级，提前到应用启动时后台完成，
        //   首次进抖音省掉这段等待（失败静默，进入时会重试）
        _ = GetEnvAsync().ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }

    // 环境单例任务：失败不缓存（下次调用重试），运行中/成功则复用
    private Task<CoreWebView2Environment> GetEnvAsync()
    {
        if (_envTask is { IsFaulted: false, IsCanceled: false } alive) return alive;
        return _envTask = CreateEnvAsync();
    }

    private static async Task<CoreWebView2Environment> CreateEnvAsync()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AvaloniaKit", "WebView2");
        var opts = new CoreWebView2EnvironmentOptions
        {
            // ★ 允许 video 自动播放（模拟抖音进入即播）
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
        };
        return await CoreWebView2Environment.CreateAsync(null, dataDir, opts);
    }

    public void Show(string html, double topOffsetDip)
    {
        _showing = true;
        _topOffsetDip = topOffsetDip;
        _ = ShowAsync(html, ++_showVersion);
    }

    private async Task ShowAsync(string html, int version)
    {
        try
        {
            // ★ 全程用局部变量：切 Tab 是 Hide()+Show() 连发，Hide 投递的清理会在
            //   下方 await 间隙执行并把 _window/_controller 置空，过早写字段会被
            //   清掉导致 NRE；字段赋值必须放在全部 await 之后
            var window = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var handle = window?.TryGetPlatformHandle();
            if (window == null || handle == null) return;

            // 启动时已预热：这里通常直接命中现成环境，不再有秒级等待
            var env = await GetEnvAsync();

            // 等待期间用户可能已经点了返回，或又切了一次 Tab（代数过期）
            if (!_showing || version != _showVersion) return;

            var controller = await env.CreateCoreWebView2ControllerAsync(handle.Handle);
            if (!_showing || version != _showVersion) { controller.Close(); return; }

            _window = window;
            _controller = controller;
            controller.DefaultBackgroundColor = System.Drawing.Color.Black;
            controller.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            controller.CoreWebView2.Settings.IsZoomControlEnabled = false;

            controller.CoreWebView2.NavigationStarting += OnNavigationStarting;
            window.PropertyChanged += OnWindowPropertyChanged;

            UpdateBounds();
            controller.CoreWebView2.NavigateToString(html);
            controller.IsVisible = true;
        }
        catch
        {
            // WebView2 Runtime 缺失等场景：保持 Avalonia 兜底页（黑底+返回按钮）
        }
    }

    public void Hide()
    {
        _showing = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _window.PropertyChanged -= OnWindowPropertyChanged;
                _window = null;
            }
            if (_controller != null)
            {
                try
                {
                    _controller.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                    _controller.Close();   // 关闭即停止视频与音频
                }
                catch { }
                _controller = null;
            }
        });
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // app:// 自定义协议 = HTML→宿主消息通道：取消导航并分发
        if (!e.Uri.StartsWith("app://", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        if (e.Uri.StartsWith("app://exit", StringComparison.OrdinalIgnoreCase))
            ExitRequested?.Invoke(this, EventArgs.Empty);
        else
            MessageReceived?.Invoke(this, e.Uri);
    }

    // 窗口尺寸/缩放变化时同步 WebView2 子窗口区域
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TopLevel.ClientSizeProperty)
            UpdateBounds();
    }

    private void UpdateBounds()
    {
        if (_controller == null || _window == null) return;
        double scale = _window.RenderScaling;
        // ★ 顶部下移：露出 Avalonia 标题栏（DIP × RenderScaling = 物理像素）
        int top = (int)Math.Round(_topOffsetDip * scale);
        _controller.Bounds = new System.Drawing.Rectangle(0, top,
            (int)(_window.ClientSize.Width * scale),
            Math.Max(0, (int)(_window.ClientSize.Height * scale) - top));
    }
}
