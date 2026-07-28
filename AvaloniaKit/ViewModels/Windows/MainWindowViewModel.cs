using AvaloniaKit.Messages;
using AvaloniaKit.ViewModels.Messages;
using AvaloniaKit.ViewModels.UserControls.Chat;
using AvaloniaKit.ViewModels.UserControls.Contacts;
using AvaloniaKit.ViewModels.UserControls.Discover;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using AvaloniaKit.ViewModels.UserControls.Profile;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AvaloniaKit.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject,
    IRecipient<NavigateToServiceMessage>,
    IRecipient<NavigateBackToProfileMessage>,
    IRecipient<NavigateToFundTrackerMessage>,
    IRecipient<NavigateBackFromFundTrackerMessage>,
    IRecipient<NavigateToFundChartMessage>,
    IRecipient<NavigateBackFromFundChartMessage>,
    IRecipient<NavigateToNeteaseMessage>,
    IRecipient<NavigateBackFromNeteaseMessage>,
    IRecipient<NavigateToNeteasePlayerMessage>,
    IRecipient<NavigateBackFromNeteasePlayerMessage>,
    IRecipient<NavigateToWeatherMessage>,
    IRecipient<NavigateBackFromWeatherMessage>,
    IRecipient<NavigateToDouyinMessage>,
    IRecipient<NavigateBackFromDouyinMessage>,
    IRecipient<NavigateToGameBoxesMessages>,
    IRecipient<NavigateBackFromGameBoxesMessage>,
    IRecipient<NavigateToTetrisMessages>,
    IRecipient<NavigateBackFromTetrisMessage>,
    IRecipient<NavigateToSnakeMessages>,
    IRecipient<NavigateToGame2048Messages>,
    IRecipient<NavigateToMinesweeperMessages>,
    IRecipient<NavigateToSudokuMessages>,
    IRecipient<NavigateToPlaneMessages>
{
    // ── 页面 ViewModel 实例 ──
    private readonly ChatViewModel _chatVm = new();
    private readonly ContactsViewModel _contactsVm = new();
    private readonly DiscoverViewModel _discoverVm = new();
    private readonly TetrisViewModel _tetrisVm = new();
    private readonly SnakeViewModel _snakeVm = new();
    private readonly Game2048ViewModel _game2048Vm = new();
    private readonly MinesweeperViewModel _minesweeperVm = new();
    private readonly SudokuViewModel _sudokuVm = new();
    private readonly PlaneViewModel _planeVm = new();
    private readonly GameBoxesViewModel _gameBoxesVm = new(); 
    private readonly ProfileViewModel _profileVm = new();
    private readonly ServiceViewModel _serviceVm = new();
    private readonly FundTrackerViewModel _fundTrackerVm = new();
    private readonly FundChartViewModel _fundChartVm = new();
    private readonly NeteaseViewModel _neteaseVm = new();
    private readonly NeteasePlayerViewModel _neteasePlayerVm = new();
    private readonly WeatherViewModel _weatherVm = new();
    private readonly DouyinViewModel _douyinVm = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatActive))]
    [NotifyPropertyChangedFor(nameof(IsContactsActive))]
    [NotifyPropertyChangedFor(nameof(IsDiscoverActive))]
    [NotifyPropertyChangedFor(nameof(IsTetrisActive))]
    [NotifyPropertyChangedFor(nameof(IsProfileActive))]
    [NotifyPropertyChangedFor(nameof(CurrentPageTitle))]
    [NotifyPropertyChangedFor(nameof(ShowTitleBar))]
    [NotifyPropertyChangedFor(nameof(ShowTabBar))]
    private ObservableObject _currentPage;

    /// <summary>★ 当前实例，供平台宿主（如 Android MainActivity 返回回调）直接访问</summary>
    public static MainWindowViewModel? Current { get; private set; }

    public MainWindowViewModel()
    {
        _currentPage = _chatVm;
        Current = this;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public bool IsChatActive => CurrentPage is ChatViewModel;
    public bool IsContactsActive => CurrentPage is ContactsViewModel;
    public bool IsDiscoverActive => CurrentPage is DiscoverViewModel;
    public bool IsTetrisActive => CurrentPage is TetrisViewModel;
    public bool IsProfileActive => CurrentPage is ProfileViewModel;

    public string CurrentPageTitle => CurrentPage switch
    {
        ChatViewModel => "微信",
        ContactsViewModel => "通讯录",
        DiscoverViewModel => "发现",
        ProfileViewModel => "我",
        ServiceViewModel => "服务",
        FundTrackerViewModel => "基金自选跟踪",
        FundChartViewModel => "净值走势",
        NeteaseViewModel => "网易云音乐",
        NeteasePlayerViewModel => "",
        WeatherViewModel => "",
        DouyinViewModel => "",
        _ => ""
    };

    public bool ShowTitleBar => CurrentPage is not ProfileViewModel
                                           and not ServiceViewModel
                                           and not FundTrackerViewModel
                                           and not TetrisViewModel
                                           and not SnakeViewModel
                                           and not Game2048ViewModel
                                           and not MinesweeperViewModel
                                           and not SudokuViewModel
                                           and not PlaneViewModel
                                           and not NeteaseViewModel
                                           and not NeteasePlayerViewModel
                                           and not WeatherViewModel
                                           and not DouyinViewModel;

    public bool ShowTabBar => CurrentPage is not ServiceViewModel
                                        and not FundTrackerViewModel
                                        and not FundChartViewModel
                                        and not TetrisViewModel
                                        and not SnakeViewModel
                                        and not Game2048ViewModel
                                        and not MinesweeperViewModel
                                        and not SudokuViewModel
                                        and not PlaneViewModel
                                        and not NeteaseViewModel
                                        and not NeteasePlayerViewModel
                                        and not GameBoxesViewModel
                                        and not WeatherViewModel
                                        and not DouyinViewModel;

    // ── ★ 全局边缘滑动返回：子页面统一返回入口 ──────────────────────────────
    // 复用各子页 VM 自己的 GoBackCommand（保留其内部清理逻辑，如游戏停表、音频退订）
    public bool CanGoBack => CurrentPage is ServiceViewModel
                                         or FundTrackerViewModel
                                         or FundChartViewModel
                                         or NeteaseViewModel
                                         or NeteasePlayerViewModel
                                         or WeatherViewModel
                                         or DouyinViewModel
                                         or TetrisViewModel
                                         or SnakeViewModel
                                         or Game2048ViewModel
                                         or MinesweeperViewModel
                                         or SudokuViewModel
                                         or PlaneViewModel
                                         or GameBoxesViewModel;

    public bool TryGoBackFromSubPage()
    {
        System.Windows.Input.ICommand? back = CurrentPage switch
        {
            ServiceViewModel vm => vm.GoBackCommand,
            FundTrackerViewModel vm => vm.GoBackCommand,
            FundChartViewModel vm => vm.GoBackCommand,
            NeteaseViewModel vm => vm.GoBackCommand,
            NeteasePlayerViewModel vm => vm.GoBackCommand,
            WeatherViewModel vm => vm.GoBackCommand,
            DouyinViewModel vm => vm.GoBackCommand,
            TetrisViewModel vm => vm.GoBackCommand,
            SnakeViewModel vm => vm.GoBackCommand,
            Game2048ViewModel vm => vm.GoBackCommand,
            MinesweeperViewModel vm => vm.GoBackCommand,
            SudokuViewModel vm => vm.GoBackCommand,
            PlaneViewModel vm => vm.GoBackCommand,
            GameBoxesViewModel vm => vm.GoBackCommand,
            _ => null,
        };
        if (back is null) return false;
        back.Execute(null);
        return true;
    }

    [RelayCommand] private void SwitchToChat() => CurrentPage = _chatVm;
    [RelayCommand] private void SwitchToContacts() => CurrentPage = _contactsVm;
    [RelayCommand] private void SwitchToDiscover() => CurrentPage = _discoverVm;
    [RelayCommand] private void SwitchToTetris() => CurrentPage = _tetrisVm;
    [RelayCommand] private void SwitchToProfile() => CurrentPage = _profileVm;

    public void Receive(NavigateToServiceMessage message)
    {
        _serviceVm.OnNavigatedTo();
        CurrentPage = _serviceVm;
    }

    public void Receive(NavigateBackToProfileMessage message)
        => CurrentPage = _profileVm;

    public void Receive(NavigateToFundTrackerMessage message)
    {
        _fundTrackerVm.OnNavigatedTo();
        CurrentPage = _fundTrackerVm;
    }

    public void Receive(NavigateBackFromFundTrackerMessage message)
        => CurrentPage = _chatVm;

    public void Receive(NavigateToFundChartMessage message)
    {
        _fundChartVm.OnNavigatedTo(message.Code, message.Name);
        CurrentPage = _fundChartVm;
    }

    public void Receive(NavigateBackFromFundChartMessage message)
        => CurrentPage = _fundTrackerVm;

    public void Receive(NavigateToNeteaseMessage message)
    {
        _neteaseVm.OnNavigatedTo();
        CurrentPage = _neteaseVm;
    }

    public void Receive(NavigateBackFromNeteaseMessage message)
        => CurrentPage = _chatVm;

    public void Receive(NavigateToNeteasePlayerMessage message)
    {
        _neteasePlayerVm.OnNavigatedTo(
            message.SongId, message.SongName,
            message.Artist, message.Album, message.CoverUrl);
        CurrentPage = _neteasePlayerVm;
    }

    public void Receive(NavigateBackFromNeteasePlayerMessage message)
    {
        // ★ 回到列表页时同步迷你播放栏的播放/暂停图标（用户可能在播放器页暂停过）
        _neteaseVm.SyncPlaybackState();
        CurrentPage = _neteaseVm;
    }

    public void Receive(NavigateToWeatherMessage message)
    {
        // ★ 数据超过 10 分钟自动刷新
        _weatherVm.OnNavigatedTo();
        CurrentPage = _weatherVm;
    }

    public void Receive(NavigateBackFromWeatherMessage message)
        => CurrentPage = _chatVm;

    public void Receive(NavigateToDouyinMessage message)
    {
        CurrentPage = _douyinVm;
        // ★ 先切页再显示覆盖层，避免覆盖层盖住旧页面闪烁
        _douyinVm.OnNavigatedTo();
    }

    public void Receive(NavigateBackFromDouyinMessage message)
        => CurrentPage = _chatVm;

    public void Receive(NavigateToTetrisMessages message)
    {
        CurrentPage = _tetrisVm;
    }

    public void Receive(NavigateBackFromTetrisMessage message)
        => CurrentPage = _discoverVm;

    public void Receive(NavigateToGameBoxesMessages message)
    {
        CurrentPage = _gameBoxesVm;
    }

    public void Receive(NavigateBackFromGameBoxesMessage message)
    {
        CurrentPage = _gameBoxesVm;
    }
    public void Receive(NavigateToSnakeMessages message)
    {
        CurrentPage = _snakeVm;
    }

    public void Receive(NavigateToGame2048Messages message)
        => CurrentPage = _game2048Vm;

    public void Receive(NavigateToMinesweeperMessages message)
        => CurrentPage = _minesweeperVm;

    public void Receive(NavigateToSudokuMessages message)
        => CurrentPage = _sudokuVm;

    public void Receive(NavigateToPlaneMessages message)
        => CurrentPage = _planeVm;
}
