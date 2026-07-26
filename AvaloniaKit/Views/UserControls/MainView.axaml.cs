using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaKit.ViewModels.Windows;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.Views.UserControls;

// ══════════════════════════════════════════════════════════════════════════════
//  MainView — 全局「右缘左滑返回」手势（仿微信右手拇指操作）
//  · 手指在页面最右侧 EdgeZone 内按下，向左滑超过 TriggerDx 即触发当前子页返回
//  · 拖动过程中页面跟随手指做阻尼位移，松手后回弹（或返回）
//  · 仅子页面生效（MainWindowViewModel.CanGoBack），四个主 Tab 不受影响
//  · 按在 Slider / ScrollBar 上不启动手势，避免劫持进度条等横向拖动
// ══════════════════════════════════════════════════════════════════════════════
public partial class MainView : UserControl
{
    private const double EdgeZone = 40;    // 右缘触发区宽度（px）
    private const double SwipeSlop = 14;   // 判定为横向滑动的最小位移
    private const double TriggerDx = 64;   // 触发返回的最小左滑距离

    private bool _tracking;                // 按下点在触发区内，正在观察
    private bool _swiping;                 // 已确认为左滑手势，页面跟随手指
    private Point _startPos;
    private readonly TranslateTransform _pageShift = new();
    private CancellationTokenSource? _resetCts;

    public MainView()
    {
        InitializeComponent();

        PageHost.RenderTransform = _pageShift;

        // Tunnel + handledEventsToo：即使子控件（列表/按钮）处理了事件也能观察到
        PageHost.AddHandler(PointerPressedEvent, OnPagePressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        PageHost.AddHandler(PointerMovedEvent, OnPageMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        PageHost.AddHandler(PointerReleasedEvent, OnPageReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        PageHost.AddHandler(PointerCaptureLostEvent, OnPageCaptureLost,
            RoutingStrategies.Direct, handledEventsToo: true);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    // ── 按下：只在「子页面 + 右缘区域 + 非滑杆控件」时开始观察 ──────────────
    private void OnPagePressed(object? sender, PointerPressedEventArgs e)
    {
        _tracking = false;
        _swiping = false;

        if (Vm is not { CanGoBack: true }) return;

        var pos = e.GetPosition(PageHost);
        if (pos.X < PageHost.Bounds.Width - EdgeZone) return;

        // 按在 Slider / ScrollBar 上时放行，避免与横向拖动冲突
        if (e.Source is Visual src &&
            (src.FindAncestorOfType<Slider>(includeSelf: true) != null ||
             src.FindAncestorOfType<ScrollBar>(includeSelf: true) != null))
            return;

        _resetCts?.Cancel();
        _startPos = pos;
        _tracking = true;
    }

    // ── 移动：确认左滑后接管指针，页面做阻尼跟随 ────────────────────────────
    private void OnPageMoved(object? sender, PointerEventArgs e)
    {
        if (!_tracking) return;

        var pos = e.GetPosition(PageHost);
        double dx = pos.X - _startPos.X;
        double dy = pos.Y - _startPos.Y;

        if (!_swiping)
        {
            // 纵向意图（滚动列表）则放弃本次观察
            if (Math.Abs(dy) > SwipeSlop && Math.Abs(dy) > Math.Abs(dx))
            {
                _tracking = false;
                return;
            }
            if (dx <= -SwipeSlop && Math.Abs(dx) > Math.Abs(dy))
            {
                _swiping = true;
                e.Pointer.Capture(PageHost);   // 从子控件手中接管指针
            }
        }

        if (_swiping)
        {
            // 阻尼系数 0.4：跟手但不完全等距，接近微信手感
            _pageShift.X = Math.Clamp(dx, -PageHost.Bounds.Width, 0) * 0.4;
            e.Handled = true;
        }
    }

    // ── 松手：超过阈值触发返回，否则回弹 ────────────────────────────────────
    private void OnPageReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_tracking) return;

        bool wasSwiping = _swiping;
        double dx = e.GetPosition(PageHost).X - _startPos.X;
        _tracking = false;
        _swiping = false;

        if (wasSwiping)
        {
            e.Handled = true;
            if (dx <= -TriggerDx && Vm?.TryGoBackFromSubPage() == true)
                _pageShift.X = 0;              // 页面已切换，立即复位
            else
                _ = AnimateShiftBackAsync();   // 未达阈值，回弹
        }
    }

    private void OnPageCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_tracking && !_swiping) return;
        _tracking = false;
        _swiping = false;
        _ = AnimateShiftBackAsync();
    }

    // ── 回弹动画：约 150ms EaseOutCubic ─────────────────────────────────────
    private async Task AnimateShiftBackAsync()
    {
        _resetCts?.Cancel();
        var cts = _resetCts = new CancellationTokenSource();

        double start = _pageShift.X;
        if (Math.Abs(start) < 1) { _pageShift.X = 0; return; }

        const int steps = 10;
        for (int i = 1; i <= steps; i++)
        {
            if (cts.IsCancellationRequested) return;
            double t = i / (double)steps;
            double ease = 1 - Math.Pow(1 - t, 3);
            _pageShift.X = start * (1 - ease);
            try { await Task.Delay(15, cts.Token); }
            catch (OperationCanceledException) { return; }
        }
        _pageShift.X = 0;
    }
}
