using CommunityToolkit.Mvvm.Input;

namespace AvaloniaKit.ViewModels;

/// <summary>
/// 子页面标记接口：实现者即支持"右缘滑动/系统返回"统一返回。
/// GoBackCommand 由 CommunityToolkit.Mvvm 的 [RelayCommand] GoBack() 自动生成，
/// 各子页在其中完成自身清理（游戏停表、音频退订等）后发送返回导航消息。
/// </summary>
public interface ISubPageViewModel
{
    IRelayCommand GoBackCommand { get; }
}
