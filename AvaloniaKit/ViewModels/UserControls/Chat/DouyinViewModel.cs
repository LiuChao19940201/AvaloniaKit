using Avalonia.Threading;
using AvaloniaKit.Services;
using AvaloniaKit.Tools;
using AvaloniaKit.ViewModels.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  DouyinViewModel — 抖音短视频页
//  · 界面本体是共享 HTML（DouyinHtml.Page），由平台 IDouyinService 覆盖层承载；
//    本 VM 只负责导航进出时的覆盖层生命周期与返回消息
//  · 平台未注册服务时（如 iOS），页面显示占位提示
// ══════════════════════════════════════════════════════════════════════════════
public partial class DouyinViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasService = true;

    private bool _exitHooked = false;

    public void OnNavigatedTo()
    {
        var svc = ServiceLocator.DouyinService;
        HasService = svc != null;
        if (svc == null) return;

        if (!_exitHooked)
        {
            svc.ExitRequested += OnExitRequested;
            _exitHooked = true;
        }
        svc.Show(DouyinHtml.Page);
    }

    // HTML 内点击返回（可能来自 WebView 线程）→ 调度回 UI 线程执行返回
    private void OnExitRequested(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(GoBack);

    [RelayCommand]
    private void GoBack()
    {
        ServiceLocator.DouyinService?.Hide();
        WeakReferenceMessenger.Default.Send(new NavigateBackFromDouyinMessage());
    }
}
