using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Irihi.Avalonia.Shared.Contracts;
using System;

namespace AvaloniaKit.ViewModels.Windows
{
    // ★ Avalonia 12 升级：Avalonia.ReactiveUI 包已随 v12 移除（移交 ReactiveUI 团队），
    //   与项目其余 VM 统一改用 CommunityToolkit.Mvvm 的 ObservableObject
    public partial class SplashViewModel : ObservableObject, IDialogContext
    {
        [ObservableProperty] private double _progress;

        private Random _r = new();

        public SplashViewModel()
        {
            DispatcherTimer.Run(OnUpdate, TimeSpan.FromMilliseconds(20), DispatcherPriority.Default);
        }

        private bool OnUpdate()
        {
            Progress += 10 * _r.NextDouble();
            if (Progress <= 100)
            {
                return true;
            }
            else
            {
                RequestClose?.Invoke(this, true);
                return false;
            }
        }

        public void Close()
        {
            RequestClose?.Invoke(this, false);
        }

        public event EventHandler<object?>? RequestClose;
    }
}
