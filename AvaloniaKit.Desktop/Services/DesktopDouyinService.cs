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
//    NavigateToString 加载共享 HTML（视频/手势/操作栏全部在 HTML 内）
//  · 环境参数放开自动播放限制（--autoplay-policy=no-user-gesture-required）
//  · HTML 内返回按钮导航 app://exit → NavigationStarting 拦截触发 ExitRequested
//  · 依赖系统 WebView2 Runtime（Win10/11 预装）；缺失时静默失败，
//    页面仍可用 Avalonia 兜底返回按钮退出
// ══════════════════════════════════════════════════════════════════════════════
public class DesktopDouyinService : IDouyinService
{
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private Window? _window;
    private bool _showing;

    public event EventHandler? ExitRequested;

    public void Show(string html)
    {
        _showing = true;
        _ = ShowAsync(html);
    }

    private async Task ShowAsync(string html)
    {
        try
        {
            _window = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var handle = _window?.TryGetPlatformHandle();
            if (_window == null || handle == null) return;

            if (_env == null)
            {
                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AvaloniaKit", "WebView2");
                var opts = new CoreWebView2EnvironmentOptions
                {
                    // ★ 允许 video 自动播放（模拟抖音进入即播）
                    AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                };
                _env = await CoreWebView2Environment.CreateAsync(null, dataDir, opts);
            }

            // 等待期间用户可能已经点了返回
            if (!_showing) return;

            var controller = await _env.CreateCoreWebView2ControllerAsync(handle.Handle);
            if (!_showing) { controller.Close(); return; }

            _controller = controller;
            controller.DefaultBackgroundColor = System.Drawing.Color.Black;
            controller.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            controller.CoreWebView2.Settings.IsZoomControlEnabled = false;

            controller.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _window.PropertyChanged += OnWindowPropertyChanged;

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
        if (e.Uri.StartsWith("app://exit", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
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
        _controller.Bounds = new System.Drawing.Rectangle(0, 0,
            (int)(_window.ClientSize.Width * scale),
            (int)(_window.ClientSize.Height * scale));
    }
}
