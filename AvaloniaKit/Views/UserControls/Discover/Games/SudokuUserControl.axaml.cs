using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using System;

namespace AvaloniaKit.Views.UserControls.Discover.Games;

public partial class SudokuUserControl : UserControl
{
    private SudokuViewModel? Vm => DataContext as SudokuViewModel;

    private Point _pressPos;
    private const double TapSlop = 12;

    public SudokuUserControl()
    {
        InitializeComponent();
        Focusable = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);

        // 棋盘层统一点选（DataTemplate 内元素的 DataContext 即 SudokuCell）
        Board.AddHandler(PointerPressedEvent, OnBoardPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        Board.AddHandler(PointerReleasedEvent, OnBoardReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        // 物理键盘 1-9 / 删除 / 提示
        AddHandler(KeyDownEvent, OnKeyDownTunnel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Focus();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Focus();   // 点按钮后拉回焦点，保证键盘输入持续可用
    }

    private void OnBoardPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressPos = e.GetPosition(Board);
    }

    private void OnBoardReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is not { IsRunning: true } vm) return;

        var delta = e.GetPosition(Board) - _pressPos;
        if (Math.Abs(delta.X) > TapSlop || Math.Abs(delta.Y) > TapSlop) return;

        if ((e.Source as Control)?.DataContext is SudokuCell cell)
            vm.SelectCell(cell);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        // 数字 1-9（主键盘 + 小键盘）
        int num = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => 0,
        };
        if (num > 0)
        {
            Vm.InputNumberCommand.Execute(num.ToString());
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
            case Key.D0:
            case Key.NumPad0:
                Vm.EraseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.H:
                Vm.HintCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
