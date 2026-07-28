using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using System;

namespace AvaloniaKit.Views.UserControls.Discover.Games;

public partial class MinesweeperUserControl : UserControl
{
    private MinesweeperViewModel? Vm => DataContext as MinesweeperViewModel;

    private Point _pressPos;
    private const double TapSlop = 12;   // 位移超过此值视为滑动（让位给全局右缘返回手势）

    public MinesweeperUserControl()
    {
        InitializeComponent();

        // 统一在棋盘层处理点按：左键/触摸=翻格（或插旗模式插旗），右键=插旗。
        // 无需 140 个 Button，也避免与全局右缘滑动返回手势冲突。
        Board.AddHandler(PointerPressedEvent, OnBoardPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerReleasedEvent, OnBoardReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnBoardPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressPos = e.GetPosition(Board);
    }

    private void OnBoardReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is not { IsRunning: true } vm) return;

        // 位移过大 → 是滑动手势不是点按
        var delta = e.GetPosition(Board) - _pressPos;
        if (Math.Abs(delta.X) > TapSlop || Math.Abs(delta.Y) > TapSlop) return;

        // 命中的格子（DataTemplate 内任意元素的 DataContext 都是 MineCell）
        if ((e.Source as Control)?.DataContext is not MineCell cell) return;

        if (e.InitialPressMouseButton == MouseButton.Right)
            vm.ToggleFlag(cell);          // 桌面右键插旗
        else
            vm.CellTapCommand.Execute(cell);
    }
}
