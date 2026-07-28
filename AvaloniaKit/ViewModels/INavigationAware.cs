namespace AvaloniaKit.ViewModels;

/// <summary>
/// 页面导航生命周期：MainWindowViewModel 在切换到目标页前调用 OnNavigatedTo，
/// 页面在此完成进入时的状态重置/数据刷新。
/// </summary>
public interface INavigationAware
{
    void OnNavigatedTo();
}
