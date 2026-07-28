using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaKit.Messages;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using AvaloniaKit.Views.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;

namespace AvaloniaKit.UiTests;

// ══════════════════════════════════════════════════════════════════════════════
//  桌面窗口宿主端到端回归：MainWindow（含拖窗逻辑）内游戏交互必须可用
//  背景 bug：MainWindow 全局 PointerPressed → BeginMoveDrag 抢占指针捕获，
//  导致扫雷/数独点格子、贪吃蛇/2048 滑动收不到 PointerReleased（桌面端独有）
// ══════════════════════════════════════════════════════════════════════════════
public class MainWindowGameInteractionTests
{
    [AvaloniaFact]
    public void Minesweeper_CellClick_InsideMainWindow_RevealsCells()
    {
        App.Services = new ServiceCollection().AddAvaloniaKitCore().BuildServiceProvider();

        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 导航到扫雷页（走真实消息链路）
        WeakReferenceMessenger.Default.Send(new NavigateToMinesweeperMessage());
        Dispatcher.UIThread.RunJobs();

        var vm = App.Services.GetRequiredService<MinesweeperViewModel>();

        // 点"开始游戏"
        ClickAt(window, FindButtonCenter(window, "开 始 游 戏"));
        Assert.True(vm.IsRunning, "扫雷开始按钮在 MainWindow 宿主下应可点击");

        // 点棋盘中央格子（普通 Border 元素，按下事件不 Handled，
        // 修复前会被窗口拖动逻辑抢占导致 Released 丢失、格子翻不开）
        var board = window.GetVisualDescendants().OfType<ItemsControl>()
            .First(c => c.Name == "Board");
        var boardCenter = board.TranslatePoint(
            new Point(board.Bounds.Width / 2, board.Bounds.Height / 2), window)!.Value;
        ClickAt(window, boardCenter);

        Assert.True(vm.Cells.Any(c => c.IsRevealed),
            "点击棋盘格子后应有格子被翻开（PointerReleased 未被拖窗逻辑吞掉）");

        vm.GoBackCommand.Execute(null);
    }

    private static Point FindButtonCenter(Window window, string content)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsVisible && b.Content as string == content);
        return button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    }

    private static void ClickAt(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
