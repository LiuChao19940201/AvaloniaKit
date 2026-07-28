using Avalonia.Threading;
using AvaloniaKit.Messages;
using AvaloniaKit.Resources;
using AvaloniaKit.Services;
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
//  · 注意：OnNavigatedTo 由 MainWindowViewModel 在切页"之后"显式调用
//    （先切页再显示覆盖层，避免覆盖层盖住旧页面闪烁），故不实现 INavigationAware
// ══════════════════════════════════════════════════════════════════════════════
public partial class DouyinViewModel : PageViewModelBase, ISubPageViewModel
{
    public override bool ShowTitleBar => false;
    public override bool ShowTabBar => false;

    // ★ 覆盖层顶部预留（DIP）：44 = MainView 状态栏安全区，52 = 本页标题栏高度，
    //   与 DouyinUserControl.axaml 的头部布局保持一致，使 WebView 恰好从标题栏下方开始
    private const double TopOffsetDip = 44 + 52;

    private readonly IDouyinService? _douyin;

    [ObservableProperty] private bool _hasService = true;

    private bool _exitHooked;

    public DouyinViewModel(IDouyinService? douyinService = null)
        => _douyin = douyinService;

    public void OnNavigatedTo()
    {
        HasService = _douyin != null;
        if (_douyin == null) return;

        if (!_exitHooked)
        {
            _douyin.ExitRequested += OnExitRequested;
            _exitHooked = true;
        }
        _douyin.Show(DouyinHtml.Page, TopOffsetDip);
    }

    // HTML 内点击返回（可能来自 WebView 线程）→ 调度回 UI 线程执行返回
    private void OnExitRequested(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(GoBack);

    [RelayCommand]
    private void GoBack()
    {
        _douyin?.Hide();
        WeakReferenceMessenger.Default.Send(new NavigateBackFromDouyinMessage());
    }
}
