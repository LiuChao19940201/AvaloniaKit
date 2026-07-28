using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Irihi.Avalonia.Shared.Contracts;
using System;

namespace AvaloniaKit.ViewModels.Windows;

/// <summary>启动闪屏进度（桌面端 SplashWindow 专用，走完即关闭并进入主窗口）</summary>
public partial class SplashViewModel : ObservableObject, IDialogContext
{
    [ObservableProperty] private double _progress;

    private readonly Random _random = new();

    public SplashViewModel()
    {
        DispatcherTimer.Run(OnUpdate, TimeSpan.FromMilliseconds(20), DispatcherPriority.Default);
    }

    private bool OnUpdate()
    {
        Progress += 10 * _random.NextDouble();
        if (Progress <= 100)
        {
            return true;
        }

        RequestClose?.Invoke(this, true);
        return false;
    }

    public void Close()
    {
        RequestClose?.Invoke(this, false);
    }

    public event EventHandler<object?>? RequestClose;
}
