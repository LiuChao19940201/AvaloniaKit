using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaKit.ViewModels.Windows;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace AvaloniaKit.Views.Windows;

public partial class MainWindow : Ursa.Controls.UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainWindowViewModel>();

        // 无系统边框窗体：按住外壳区（标题栏/Tab 栏空白）拖动
        PointerPressed += OnRootPointerPressed;

        // 只在桌面端设置窗口大小
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
        {
            Width = 350;
            Height = 700;
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // ★ 页面内容区（MainView.PageHost 子树）绝不启动拖窗：
        //   BeginMoveDrag 会立即抢占指针捕获，页面收不到后续 PointerReleased，
        //   导致扫雷/数独点格子、贪吃蛇/2048 滑动、飞机拖动等交互全部失效
        //   （这些交互的按下事件不标记 Handled，会一路冒泡到 Window）。
        //   Button/Slider 等自带 Handled，原本就不会触发拖窗，不受影响。
        if (e.Source is Visual src &&
            src.GetVisualAncestors().Prepend(src)
               .OfType<Control>().Any(c => c.Name == "PageHost"))
            return;

        BeginMoveDrag(e);
    }
}
