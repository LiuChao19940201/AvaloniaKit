using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;

namespace AvaloniaKit.Views.UserControls.Discover.Games;

public partial class PlaneUserControl : UserControl
{
    private PlaneViewModel? Vm => DataContext as PlaneViewModel;

    private bool _dragging;

    public PlaneUserControl()
    {
        InitializeComponent();
        Focusable = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);

        // 游戏区拖动跟手（GetPosition(GameArea) 自动换算 Viewbox 缩放后的逻辑坐标）
        GameArea.AddHandler(PointerPressedEvent, OnAreaPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        GameArea.AddHandler(PointerMovedEvent, OnAreaMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        GameArea.AddHandler(PointerReleasedEvent, OnAreaReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);

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
        Focus();
    }

    private void OnAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { IsRunning: true } vm) return;
        _dragging = true;
        var p = e.GetPosition(GameArea);
        // 手指上方 30px 处控制机身，避免手指遮挡（鼠标同样适用）
        vm.MovePlayerTo(p.X, p.Y - 30);
    }

    private void OnAreaMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || Vm is not { IsRunning: true } vm) return;
        var p = e.GetPosition(GameArea);
        vm.MovePlayerTo(p.X, p.Y - 30);
    }

    private void OnAreaReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
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

            case Key.P:
            case Key.Escape:
                Vm.TogglePauseCommand.Execute(null);
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
