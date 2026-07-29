using Avalonia.Threading;
using AvaloniaKit.Messages;
using AvaloniaKit.Resources;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  DouyinViewModel — 抖音短视频页
//  · 界面本体是共享 HTML（DouyinHtml.Build），由平台 IDouyinService 覆盖层承载；
//    本 VM 负责覆盖层生命周期、标题栏 关注/推荐 Tab、关注列表持久化
//  · 切 Tab：以新初始状态（Tab + 关注列表）重建覆盖层（信息流本为随机流，重建无感）
//  · 关注：HTML 内点头像"+"/主页关注按钮 → MessageReceived(app://follow…) →
//    更新 _follows 并经 ILocalDataService 持久化（douyin_follows），重开可恢复
//  · 平台未注册服务时（如未装 WebView2），页面显示占位提示
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

    // 持久化 key：每行 "博主名|头像URL"（与 fund_watchlist 等小写下划线命名一致）
    private const string FollowsKey = "douyin_follows";

    private readonly IDouyinService? _douyin;
    private readonly ILocalDataService? _localData;

    [ObservableProperty] private bool _hasService = true;

    // ── 标题栏 Tab：0=关注 1=推荐（默认推荐，与抖音一致）──────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFollowTab))]
    [NotifyPropertyChangedFor(nameof(IsRecommendTab))]
    private int _activeTab = 1;

    public bool IsFollowTab => ActiveTab == 0;
    public bool IsRecommendTab => ActiveTab == 1;

    private bool _hooked;
    private bool _followsLoaded;
    private bool _profileOpen;   // HTML 内博主主页是否打开（决定标题栏返回的语义）
    private readonly List<(string Name, string Avatar)> _follows = new();

    public DouyinViewModel(IDouyinService? douyinService = null,
                           ILocalDataService? localDataService = null)
    {
        _douyin = douyinService;
        _localData = localDataService;
    }

    public void OnNavigatedTo()
    {
        HasService = _douyin != null;
        if (_douyin == null) return;

        if (!_hooked)
        {
            _douyin.ExitRequested += OnExitRequested;
            _douyin.MessageReceived += OnMessageReceived;
            _hooked = true;
        }
        _ = ShowOverlayAsync();
    }

    // 先恢复关注列表再显示（首次读盘，之后内存缓存）；重建后主页必然处于关闭态
    private async Task ShowOverlayAsync()
    {
        _profileOpen = false;
        await EnsureFollowsLoadedAsync();
        _douyin?.Show(DouyinHtml.Build(ActiveTab, BuildFollowsJson()), TopOffsetDip);
    }

    // ── 标题栏 关注/推荐 切换：重建覆盖层（随机流重建无感知成本）──────────────
    [RelayCommand]
    private void SwitchTab(string tab)
    {
        int t = tab == "0" ? 0 : 1;
        if (t == ActiveTab) return;
        ActiveTab = t;
        if (_douyin == null) return;
        _douyin.Hide();
        _ = ShowOverlayAsync();
    }

    // HTML 内点击返回（可能来自 WebView 线程）→ 调度回 UI 线程执行返回
    private void OnExitRequested(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(GoBack);

    // HTML 内业务消息（关注/取关，可能来自 WebView 线程）
    private void OnMessageReceived(object? sender, string uri)
        => Dispatcher.UIThread.Post(() => HandleMessage(uri));

    private void HandleMessage(string uri)
    {
        var (action, args) = ParseAppUri(uri);
        switch (action)
        {
            case "follow":
                string name = args.GetValueOrDefault("n", "");
                if (name.Length == 0) return;
                if (_follows.FindIndex(f => f.Name == name) < 0)
                    _follows.Add((name, args.GetValueOrDefault("a", "")));
                _ = SaveFollowsAsync();
                break;

            case "unfollow":
                string target = args.GetValueOrDefault("n", "");
                if (_follows.RemoveAll(f => f.Name == target) > 0)
                    _ = SaveFollowsAsync();
                break;

            // 博主主页开/关：标题栏返回据此先关主页而非退出模块
            case "profile":
                _profileOpen = args.GetValueOrDefault("open", "") == "1";
                break;
        }
    }

    // "app://follow?n=%E5%B0%8F&a=https%3A%2F%2F…" → ("follow", {n:小…, a:https://…})
    private static (string Action, Dictionary<string, string> Args) ParseAppUri(string uri)
    {
        var args = new Dictionary<string, string>();
        if (!uri.StartsWith("app://", StringComparison.OrdinalIgnoreCase)) return ("", args);

        string rest = uri["app://".Length..];
        int q = rest.IndexOf('?');
        string action = (q < 0 ? rest : rest[..q]).TrimEnd('/').ToLowerInvariant();
        if (q >= 0)
        {
            foreach (var kv in rest[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                args[kv[..eq]] = Uri.UnescapeDataString(kv[(eq + 1)..]);
            }
        }
        return (action, args);
    }

    // ── 关注列表持久化（写入静默容错，与基金自选一致）──────────────────────────
    private async Task EnsureFollowsLoadedAsync()
    {
        if (_followsLoaded) return;
        _followsLoaded = true;
        if (_localData == null) return;
        try
        {
            string? raw = await _localData.LoadSettingAsync(FollowsKey);
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int p = line.IndexOf('|');
                string name = p < 0 ? line : line[..p];
                string avatar = p < 0 ? "" : line[(p + 1)..];
                if (name.Length > 0 && _follows.FindIndex(f => f.Name == name) < 0)
                    _follows.Add((name, avatar));
            }
        }
        catch { /* 读取失败按空关注处理 */ }
    }

    private async Task SaveFollowsAsync()
    {
        if (_localData == null) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var f in _follows)
                sb.Append(f.Name).Append('|').Append(f.Avatar).Append('\n');
            await _localData.SaveSettingAsync(FollowsKey, sb.ToString());
        }
        catch { /* 存储不可用时静默（关注仅本次会话生效） */ }
    }

    // 注入 HTML 的关注列表 JSON（Utf8JsonWriter 手写，AOT 安全且自带 HTML 转义）
    private string BuildFollowsJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var f in _follows)
            {
                w.WriteStartObject();
                w.WriteString("n", f.Name);
                w.WriteString("a", f.Avatar);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [RelayCommand]
    private void GoBack()
    {
        // ★ 博主主页打开时：返回键先“关主页回信息流”（重建覆盖层），不退出模块；
        //   HTML 内不再自绘主页返回按钮，四端统一走标题栏/系统返回手势
        if (_profileOpen && _douyin != null)
        {
            _douyin.Hide();
            _ = ShowOverlayAsync();
            return;
        }
        _douyin?.Hide();
        WeakReferenceMessenger.Default.Send(new NavigateBackFromDouyinMessage());
    }
}
