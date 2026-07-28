using AvaloniaKit.Messages;
using AvaloniaKit.ViewModels.UserControls.Chat;
using AvaloniaKit.ViewModels.UserControls.Contacts;
using AvaloniaKit.ViewModels.UserControls.Discover;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using AvaloniaKit.ViewModels.UserControls.Profile;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AvaloniaKit.ViewModels.Windows;

// ══════════════════════════════════════════════════════════════════════════════
//  MainWindowViewModel — 导航中枢
//  · 页面 ViewModel 由 DI 容器注入（常驻单例，切页保留状态）
//  · 通过 WeakReferenceMessenger 接收各页导航消息并切换 CurrentPage
//  · 标题/标题栏/Tab 栏显隐由 PageViewModelBase 各页自述，返回能力由
//    ISubPageViewModel 标记（供右缘滑动手势与系统返回统一调用）
// ══════════════════════════════════════════════════════════════════════════════
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
    IRecipient<NavigateToGameBoxesMessage>,
    IRecipient<NavigateBackFromGameBoxesMessage>,
    IRecipient<NavigateToTetrisMessage>,
    IRecipient<NavigateBackFromTetrisMessage>,
    IRecipient<NavigateToSnakeMessage>,
    IRecipient<NavigateToGame2048Message>,
    IRecipient<NavigateToMinesweeperMessage>,
    IRecipient<NavigateToSudokuMessage>,
    IRecipient<NavigateToPlaneMessage>
{
    // ── 页面 ViewModel（DI 注入的常驻单例） ──
    private readonly ChatViewModel _chatVm;
    private readonly ContactsViewModel _contactsVm;
    private readonly DiscoverViewModel _discoverVm;
    private readonly ProfileViewModel _profileVm;
    private readonly ServiceViewModel _serviceVm;
    private readonly FundTrackerViewModel _fundTrackerVm;
    private readonly FundChartViewModel _fundChartVm;
    private readonly NeteaseViewModel _neteaseVm;
    private readonly NeteasePlayerViewModel _neteasePlayerVm;
    private readonly WeatherViewModel _weatherVm;
    private readonly DouyinViewModel _douyinVm;
    private readonly GameBoxesViewModel _gameBoxesVm;
    private readonly TetrisViewModel _tetrisVm;
    private readonly SnakeViewModel _snakeVm;
    private readonly Game2048ViewModel _game2048Vm;
    private readonly MinesweeperViewModel _minesweeperVm;
    private readonly SudokuViewModel _sudokuVm;
    private readonly PlaneViewModel _planeVm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatActive))]
    [NotifyPropertyChangedFor(nameof(IsContactsActive))]
    [NotifyPropertyChangedFor(nameof(IsDiscoverActive))]
    [NotifyPropertyChangedFor(nameof(IsProfileActive))]
    [NotifyPropertyChangedFor(nameof(CurrentPageTitle))]
    [NotifyPropertyChangedFor(nameof(ShowTitleBar))]
    [NotifyPropertyChangedFor(nameof(ShowTabBar))]
    private PageViewModelBase _currentPage;

    /// <summary>当前实例，供平台宿主（如 Android MainActivity 返回回调）直接访问</summary>
    public static MainWindowViewModel? Current { get; private set; }

    public MainWindowViewModel(
        ChatViewModel chatVm,
        ContactsViewModel contactsVm,
        DiscoverViewModel discoverVm,
        ProfileViewModel profileVm,
        ServiceViewModel serviceVm,
        FundTrackerViewModel fundTrackerVm,
        FundChartViewModel fundChartVm,
        NeteaseViewModel neteaseVm,
        NeteasePlayerViewModel neteasePlayerVm,
        WeatherViewModel weatherVm,
        DouyinViewModel douyinVm,
        GameBoxesViewModel gameBoxesVm,
        TetrisViewModel tetrisVm,
        SnakeViewModel snakeVm,
        Game2048ViewModel game2048Vm,
        MinesweeperViewModel minesweeperVm,
        SudokuViewModel sudokuVm,
        PlaneViewModel planeVm)
    {
        _chatVm = chatVm;
        _contactsVm = contactsVm;
        _discoverVm = discoverVm;
        _profileVm = profileVm;
        _serviceVm = serviceVm;
        _fundTrackerVm = fundTrackerVm;
        _fundChartVm = fundChartVm;
        _neteaseVm = neteaseVm;
        _neteasePlayerVm = neteasePlayerVm;
        _weatherVm = weatherVm;
        _douyinVm = douyinVm;
        _gameBoxesVm = gameBoxesVm;
        _tetrisVm = tetrisVm;
        _snakeVm = snakeVm;
        _game2048Vm = game2048Vm;
        _minesweeperVm = minesweeperVm;
        _sudokuVm = sudokuVm;
        _planeVm = planeVm;

        _currentPage = _chatVm;
        Current = this;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    // ── 主 Tab 高亮 ──
    public bool IsChatActive => CurrentPage is ChatViewModel;
    public bool IsContactsActive => CurrentPage is ContactsViewModel;
    public bool IsDiscoverActive => CurrentPage is DiscoverViewModel;
    public bool IsProfileActive => CurrentPage is ProfileViewModel;

    // ── 页面外壳：由各页 ViewModel 自述 ──
    public string CurrentPageTitle => CurrentPage.Title;
    public bool ShowTitleBar => CurrentPage.ShowTitleBar;
    public bool ShowTabBar => CurrentPage.ShowTabBar;

    // ── 全局边缘滑动/系统返回：子页面统一返回入口 ──
    // 复用各子页 VM 自己的 GoBackCommand（保留其内部清理逻辑，如游戏停表、音频退订）
    public bool CanGoBack => CurrentPage is ISubPageViewModel;

    public bool TryGoBackFromSubPage()
    {
        if (CurrentPage is not ISubPageViewModel subPage) return false;
        subPage.GoBackCommand.Execute(null);
        return true;
    }

    /// <summary>切换页面；目标页实现 INavigationAware 时先触发其进入回调</summary>
    private void NavigateTo(PageViewModelBase page)
    {
        (page as INavigationAware)?.OnNavigatedTo();
        CurrentPage = page;
    }

    [RelayCommand] private void SwitchToChat() => CurrentPage = _chatVm;
    [RelayCommand] private void SwitchToContacts() => CurrentPage = _contactsVm;
    [RelayCommand] private void SwitchToDiscover() => CurrentPage = _discoverVm;
    [RelayCommand] private void SwitchToProfile() => CurrentPage = _profileVm;

    public void Receive(NavigateToServiceMessage message) => NavigateTo(_serviceVm);
    public void Receive(NavigateBackToProfileMessage message) => CurrentPage = _profileVm;

    public void Receive(NavigateToFundTrackerMessage message) => NavigateTo(_fundTrackerVm);
    public void Receive(NavigateBackFromFundTrackerMessage message) => CurrentPage = _chatVm;

    public void Receive(NavigateToFundChartMessage message)
    {
        _fundChartVm.OnNavigatedTo(message.Code, message.Name);
        CurrentPage = _fundChartVm;
    }

    public void Receive(NavigateBackFromFundChartMessage message) => CurrentPage = _fundTrackerVm;

    public void Receive(NavigateToNeteaseMessage message) => NavigateTo(_neteaseVm);
    public void Receive(NavigateBackFromNeteaseMessage message) => CurrentPage = _chatVm;

    public void Receive(NavigateToNeteasePlayerMessage message)
    {
        _neteasePlayerVm.OnNavigatedTo(
            message.SongId, message.SongName,
            message.Artist, message.Album, message.CoverUrl);
        CurrentPage = _neteasePlayerVm;
    }

    public void Receive(NavigateBackFromNeteasePlayerMessage message)
    {
        // 回到列表页时同步迷你播放栏的播放/暂停图标（用户可能在播放器页暂停过）
        _neteaseVm.SyncPlaybackState();
        CurrentPage = _neteaseVm;
    }

    public void Receive(NavigateToWeatherMessage message) => NavigateTo(_weatherVm);
    public void Receive(NavigateBackFromWeatherMessage message) => CurrentPage = _chatVm;

    public void Receive(NavigateToDouyinMessage message)
    {
        // ★ 先切页再显示覆盖层，避免覆盖层盖住旧页面闪烁
        CurrentPage = _douyinVm;
        _douyinVm.OnNavigatedTo();
    }

    public void Receive(NavigateBackFromDouyinMessage message) => CurrentPage = _chatVm;

    public void Receive(NavigateToGameBoxesMessage message) => CurrentPage = _gameBoxesVm;
    public void Receive(NavigateBackFromGameBoxesMessage message) => CurrentPage = _gameBoxesVm;

    public void Receive(NavigateToTetrisMessage message) => CurrentPage = _tetrisVm;
    public void Receive(NavigateBackFromTetrisMessage message) => CurrentPage = _discoverVm;

    public void Receive(NavigateToSnakeMessage message) => CurrentPage = _snakeVm;
    public void Receive(NavigateToGame2048Message message) => CurrentPage = _game2048Vm;
    public void Receive(NavigateToMinesweeperMessage message) => CurrentPage = _minesweeperVm;
    public void Receive(NavigateToSudokuMessage message) => CurrentPage = _sudokuVm;
    public void Receive(NavigateToPlaneMessage message) => CurrentPage = _planeVm;
}
