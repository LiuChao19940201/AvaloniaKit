using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using System;

namespace AvaloniaKit.Views.UserControls.Discover.Games;

public partial class Game2048UserControl : UserControl
{
    private Game2048ViewModel? Vm => DataContext as Game2048ViewModel;

    // 棋盘滑动手势（移动端主要操作方式）
    private Point _swipeStart;
    private bool _swipeTracking;
    private const double SwipeMin = 24;

    public Game2048UserControl()
    {
        InitializeComponent();
        Focusable = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);

        // Tunnel + handledEventsToo：与其他游戏页一致，优先于子控件拦截键盘
        AddHandler(KeyDownEvent, OnKeyDownTunnel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Focus();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _swipeStart = e.GetPosition(this);
        _swipeTracking = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_swipeTracking && Vm is { IsRunning: true } vm)
        {
            var delta = e.GetPosition(this) - _swipeStart;
            if (Math.Abs(delta.X) >= SwipeMin || Math.Abs(delta.Y) >= SwipeMin)
            {
                if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                    (delta.X > 0 ? vm.MoveRightCommand : vm.MoveLeftCommand).Execute(null);
                else
                    (delta.Y > 0 ? vm.MoveDownCommand : vm.MoveUpCommand).Execute(null);
            }
        }
        _swipeTracking = false;

        Focus();
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (Vm == null) return;

        switch (e.Key)
        {
            case Key.Left:
            case Key.A:
                Vm.MoveLeftCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Right:
            case Key.D:
                Vm.MoveRightCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Up:
            case Key.W:
                Vm.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
            case Key.S:
                Vm.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                if (!Vm.IsRunning)
                    Vm.StartCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
