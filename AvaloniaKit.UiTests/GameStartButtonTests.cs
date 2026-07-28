using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using AvaloniaKit.Views.UserControls.Discover.Games;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Xunit;

namespace AvaloniaKit.UiTests;

// ══════════════════════════════════════════════════════════════════════════════
//  游戏"开始游戏"按钮点击回归测试（Headless 真实输入管线）
//  背景：桌面端反馈 贪吃蛇/数独/2048/扫雷 点击开始无反应，Tetris/Plane 正常
// ══════════════════════════════════════════════════════════════════════════════
public class GameStartButtonTests
{
    private static IServiceProvider BuildServices()
    {
        // 仅共享层注册（无平台服务，GameSfx/GameScoreStore 走可空降级）
        return new ServiceCollection().AddAvaloniaKitCore().BuildServiceProvider();
    }

    private static Window ShowInWindow(Control content)
    {
        var window = new Window
        {
            Width = 400,
            Height = 800,
            Content = content,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void ClickButton(Window window, Func<Button, bool> match)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.IsVisible && match(b));
        Assert.NotNull(button);

        var center = button!.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(center);

        window.MouseMove(center!.Value);
        window.MouseDown(center.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool ContentIs(Button b, string text)
        => b.Content as string == text;

    [AvaloniaFact]
    public void Snake_StartButton_Click_StartsGame()
    {
        var sp = BuildServices();
        App.Services = sp;
        var vm = sp.GetRequiredService<SnakeViewModel>();
        var window = ShowInWindow(new SnakeUserControl { DataContext = vm });

        ClickButton(window, b => ContentIs(b, "开 始 游 戏"));

        Assert.True(vm.IsRunning, "点击开始后 SnakeViewModel.IsRunning 应为 true");

        // 回归：棋盘必须实际渲染出非零尺寸的格子
        // （UniformGrid 在 Viewbox 内若无固定尺寸，空 Border 格子期望尺寸为 0 → 整盘空白）
        Dispatcher.UIThread.RunJobs();
        var board = window.GetVisualDescendants().OfType<ItemsControl>()
            .First(c => c.ItemsSource == vm.Cells);
        Assert.True(board.Bounds.Width > 100 && board.Bounds.Height > 100,
            $"贪吃蛇棋盘渲染尺寸异常：{board.Bounds.Width}x{board.Bounds.Height}（格子不可见）");

        vm.GoBackCommand.Execute(null);   // 停表清理
    }

    [AvaloniaFact]
    public void Game2048_StartButton_Click_StartsGame()
    {
        var sp = BuildServices();
        App.Services = sp;
        var vm = sp.GetRequiredService<Game2048ViewModel>();
        var window = ShowInWindow(new Game2048UserControl { DataContext = vm });

        ClickButton(window, b => ContentIs(b, "开 始 游 戏"));

        Assert.True(vm.IsRunning, "点击开始后 Game2048ViewModel.IsRunning 应为 true");
        vm.GoBackCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Minesweeper_StartButton_Click_StartsGame()
    {
        var sp = BuildServices();
        App.Services = sp;
        var vm = sp.GetRequiredService<MinesweeperViewModel>();
        var window = ShowInWindow(new MinesweeperUserControl { DataContext = vm });

        ClickButton(window, b => ContentIs(b, "开 始 游 戏"));

        Assert.True(vm.IsRunning, "点击开始后 MinesweeperViewModel.IsRunning 应为 true");
        vm.GoBackCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Sudoku_NormalButton_Click_StartsGame()
    {
        var sp = BuildServices();
        App.Services = sp;
        var vm = sp.GetRequiredService<SudokuViewModel>();
        var window = ShowInWindow(new SudokuUserControl { DataContext = vm });

        ClickButton(window, b => ContentIs(b, "普通"));

        Assert.True(vm.IsRunning, "点击难度后 SudokuViewModel.IsRunning 应为 true");
        vm.GoBackCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Tetris_StartButton_Click_StartsGame()
    {
        var sp = BuildServices();
        App.Services = sp;
        var vm = sp.GetRequiredService<TetrisViewModel>();
        var window = ShowInWindow(new TetrisUserControl { DataContext = vm });

        ClickButton(window, b => ContentIs(b, "开 始 游 戏"));

        Assert.True(vm.IsRunning, "点击开始后 TetrisViewModel.IsRunning 应为 true");
        vm.GoBackCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Plane_StartButton_Click_StartsGame()
    {
        var sp = BuildServices();
        App.Services = sp;
        var vm = sp.GetRequiredService<PlaneViewModel>();
        var window = ShowInWindow(new PlaneUserControl { DataContext = vm });

        ClickButton(window, b => ContentIs(b, "开 始 游 戏"));

        Assert.True(vm.IsRunning, "点击开始后 PlaneViewModel.IsRunning 应为 true");
        vm.GoBackCommand.Execute(null);
    }
}
