using AvaloniaKit.Messages;
using AvaloniaKit.ViewModels.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

public partial class ChatViewModel : ObservableObject
{
    public ObservableCollection<ChatItemViewModel> ChatList { get; } = new()
    {
        // ★ 天气预报入口（排在基金上方）
        new() { Name = "基金自选跟踪", Preview = "点击查看自选基金实时净值", Time = "昨天",  Unread = 0, IsFundTracker = true  },
        new() { Name = "网易云音乐",   Preview = "私人漫游 · 推荐、排行、搜索", Time = "昨天", Unread = 0, IsNetease = true },
        new() { Name = "天气预报", Preview = "实时天气 · 24小时与7日预报 · 多城市", Time = "昨天", Unread = 0, IsWeather = true },
        new() { Name = "文件传输助手", Preview = "欢迎使用微信",             Time = "昨天",  Unread = 0  },
        new() { Name = "张三",         Preview = "好的，明天见",             Time = "10:32", Unread = 2  },
        new() { Name = "产品讨论群",   Preview = "李四：下周评审",           Time = "09:15", Unread = 5  },
        new() { Name = "王五",         Preview = "发了一张图片",             Time = "昨天",  Unread = 0  },
        new() { Name = "家庭群",       Preview = "妈妈：吃饭了",             Time = "周一",  Unread = 1  },
    };

    [RelayCommand]
    private void OpenChat(ChatItemViewModel item)
    {
        if (item.IsWeather)
            WeakReferenceMessenger.Default.Send(new NavigateToWeatherMessage());
        else if (item.IsFundTracker)
            WeakReferenceMessenger.Default.Send(new NavigateToFundTrackerMessage());
        else if (item.IsNetease)
            WeakReferenceMessenger.Default.Send(new NavigateToNeteaseMessage());
    }
}

public partial class ChatItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private string _name = "";

    [ObservableProperty] private string _preview = "";
    [ObservableProperty] private string _time = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int _unread = 0;

    public bool IsWeather { get; init; } = false;
    public bool IsFundTracker { get; init; } = false;
    public bool IsNetease { get; init; } = false;

    // 是否是功能入口（天气 / 基金 / 网易云），控制右侧箭头和 Time 显示
    public bool IsSpecialEntry => IsWeather || IsFundTracker || IsNetease;

    public string AvatarLetter => Name.Length > 0 ? Name[..1] : "?";
    public bool HasUnread => Unread > 0;

    // 头像背景色
    public string AvatarBg => IsWeather ? "#4FC3F7"
                            : IsFundTracker ? "#59b7c0"
                            : IsNetease ? "#E05C5C"
                            : "#07C160";
}
