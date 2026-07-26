using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaKit.ViewModels.UserControls.Chat;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.Views.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  NeteasePlayerUserControl — 仿网易云交互（视觉行为全部放在 code-behind）
//  1. 黑胶唱片旋转：DispatcherTimer 累加角度，暂停时停在原角度不复位
//  2. 歌词自动滚动：当前行平滑滚动到视口中央；用户手动滚动后暂停 3 秒
//  3. 点击中间区域：封面 ⇄ 歌词 切换（与官方 App 一致）
//  4. 进度条：松手后才 Seek，避免拖动过程中反复跳转
// ══════════════════════════════════════════════════════════════════════════════
public partial class NeteasePlayerUserControl : UserControl
{
    private NeteasePlayerViewModel? _vm;

    // ── 黑胶旋转 ─────────────────────────────────────────────────────────────
    private readonly DispatcherTimer _discTimer;
    private readonly RotateTransform _discTransform = new();
    private double _discAngle;

    // ── 歌词滚动 ─────────────────────────────────────────────────────────────
    private CancellationTokenSource? _scrollCts;
    private DateTime _userScrollUntil = DateTime.MinValue;

    public NeteasePlayerUserControl()
    {
        InitializeComponent();

        DiscPanel.RenderTransform = _discTransform;

        // 约 30fps，每转一圈 25 秒（与官方 App 转速接近）
        _discTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _discTimer.Tick += OnDiscTick;

        DataContextChanged += OnDataContextChanged;

        // 点击中间区域切换 封面/歌词
        CenterPanel.Tapped += OnCenterTapped;

        // 用户手动滚动歌词 → 暂停自动滚动 3 秒
        LyricScroll.AddHandler(PointerPressedEvent, OnLyricPointerActivity,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        LyricScroll.AddHandler(PointerWheelChangedEvent, OnLyricPointerActivity,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        // 进度条松手后 Seek
        ProgressSlider.AddHandler(PointerReleasedEvent, OnSliderReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        ProgressSlider.AddHandler(PointerCaptureLostEvent, OnSliderCaptureLost,
            RoutingStrategies.Direct, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _discTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _discTimer.Stop();
        _scrollCts?.Cancel();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DataContext / VM 事件
    // ══════════════════════════════════════════════════════════════════════════
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as NeteasePlayerViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NeteasePlayerViewModel.CurrentLyricIndex):
                ScrollToCurrentLyric(animated: true);
                break;

            case nameof(NeteasePlayerViewModel.SongId):
                // 换歌：黑胶角度归零
                _discAngle = 0;
                _discTransform.Angle = 0;
                break;

            case nameof(NeteasePlayerViewModel.IsLyricView):
                // 切到歌词视图后，等布局完成立刻定位到当前行（不带动画）
                if (_vm?.IsLyricView == true)
                    Dispatcher.UIThread.Post(
                        () => ScrollToCurrentLyric(animated: false),
                        DispatcherPriority.Background);
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  黑胶旋转
    // ══════════════════════════════════════════════════════════════════════════
    private void OnDiscTick(object? sender, EventArgs e)
    {
        if (_vm is not { IsPlaying: true } || _vm.IsLyricView) return;
        _discAngle = (_discAngle + 0.48) % 360;   // 0.48°/33ms ≈ 25s/圈
        _discTransform.Angle = _discAngle;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  歌词自动滚动（当前行居中）
    // ══════════════════════════════════════════════════════════════════════════
    private void OnLyricPointerActivity(object? sender, RoutedEventArgs e)
        => _userScrollUntil = DateTime.UtcNow.AddSeconds(3);

    private void ScrollToCurrentLyric(bool animated)
    {
        if (_vm is not { IsLyricView: true } vm) return;
        int idx = vm.CurrentLyricIndex;
        if (idx < 0 || idx >= vm.LyricLines.Count) return;
        if (animated && DateTime.UtcNow < _userScrollUntil) return;

        var container = LyricItems.ContainerFromIndex(idx);
        if (container is null) return;

        // 当前行中心点在视口中的位置
        var pt = container.TranslatePoint(
            new Point(0, container.Bounds.Height / 2), LyricScroll);
        if (pt is null) return;

        double target = LyricScroll.Offset.Y + pt.Value.Y
                        - LyricScroll.Viewport.Height / 2;
        target = Math.Clamp(target, 0,
            Math.Max(0, LyricScroll.Extent.Height - LyricScroll.Viewport.Height));

        if (animated)
            _ = SmoothScrollToAsync(target);
        else
        {
            _scrollCts?.Cancel();
            LyricScroll.Offset = new Vector(0, target);
        }
    }

    private async Task SmoothScrollToAsync(double target)
    {
        _scrollCts?.Cancel();
        var cts = _scrollCts = new CancellationTokenSource();

        double start = LyricScroll.Offset.Y;
        if (Math.Abs(target - start) < 1) return;

        const int steps = 22;                      // ≈ 350ms
        for (int i = 1; i <= steps; i++)
        {
            if (cts.IsCancellationRequested) return;
            double t = i / (double)steps;
            double ease = 1 - Math.Pow(1 - t, 3);  // EaseOutCubic
            LyricScroll.Offset = new Vector(0, start + (target - start) * ease);
            try { await Task.Delay(16, cts.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  交互
    // ══════════════════════════════════════════════════════════════════════════
    private void OnCenterTapped(object? sender, TappedEventArgs e)
        => _vm?.ToggleViewCommand.Execute(null);

    private void OnSliderReleased(object? sender, PointerReleasedEventArgs e)
        => SeekToSlider();

    private void OnSliderCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => SeekToSlider();

    private void SeekToSlider()
        => _vm?.SeekProgressCommand.Execute(ProgressSlider.Value);
}
