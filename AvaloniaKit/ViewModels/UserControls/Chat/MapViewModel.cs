using Avalonia.Threading;
using AvaloniaKit.Messages;
using AvaloniaKit.Resources;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  MapViewModel — 地图（仿高德：查路线 + 语音包切换）
//  · 界面本体是共享 HTML（MapHtml.Build），由平台 IMapService 覆盖层承载；
//    本 VM 只负责覆盖层生命周期与所选语音包的持久化
//  · 语音包：HTML 内切换 → MessageReceived(app://voice?id=…) → 持久化到
//    ILocalDataService（map_voice），重开可恢复
//  · 平台未注册服务时（如缺 WebView2）显示占位提示
//  · OnNavigatedTo 由 MainWindowViewModel 在切页“之后”显式调用（先切页再显覆盖层，
//    避免覆盖层盖住旧页面闪烁），故不实现 INavigationAware
// ══════════════════════════════════════════════════════════════════════════════
public partial class MapViewModel : PageViewModelBase, ISubPageViewModel
{
    public override bool ShowTitleBar => false;
    public override bool ShowTabBar => false;

    // ★ 覆盖层顶部预留（DIP）：44 状态栏安全区 + 52 标题栏高度，
    //   与 MapUserControl.axaml 的头部布局一致（与抖音同一契约）
    private const double TopOffsetDip = 44 + 52;

    // 持久化 key：所选语音包 id（小写下划线命名，与项目其它 key 一致）
    private const string VoiceKey = "map_voice";

    private readonly IMapService? _map;
    private readonly ILocalDataService? _localData;

    [ObservableProperty] private bool _hasService = true;

    private bool _hooked;
    private bool _voiceLoaded;
    private bool _navOpen;               // HTML 内是否处于导航态（决定返回键语义）
    private string _voiceId = "warm";   // 默认语音包（与 MapHtml 内置列表对应）

    public MapViewModel(IMapService? mapService = null,
                        ILocalDataService? localDataService = null)
    {
        _map = mapService;
        _localData = localDataService;
    }

    public void OnNavigatedTo()
    {
        HasService = _map != null;
        if (_map == null) return;

        if (!_hooked)
        {
            _map.ExitRequested += OnExitRequested;
            _map.MessageReceived += OnMessageReceived;
            _hooked = true;
        }
        _ = ShowOverlayAsync();
    }

    // 先恢复所选语音包再显示（首次读盘，之后内存缓存）；重建后必然回到非导航态
    private async Task ShowOverlayAsync()
    {
        _navOpen = false;
        await EnsureVoiceLoadedAsync();
        _map?.Show(MapHtml.Build(_voiceId), TopOffsetDip);
    }

    // HTML 内点击返回（可能来自 WebView 线程）→ 调度回 UI 线程
    private void OnExitRequested(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(GoBack);

    // HTML 内业务消息（切换语音包，可能来自 WebView 线程）
    private void OnMessageReceived(object? sender, string uri)
        => Dispatcher.UIThread.Post(() => HandleMessage(uri));

    private void HandleMessage(string uri)
    {
        var (action, args) = ParseAppUri(uri);
        switch (action)
        {
            case "voice":
                string id = args.GetValueOrDefault("id", "");
                if (id.Length == 0 || id == _voiceId) return;
                _voiceId = id;
                _ = SaveVoiceAsync();
                break;

            // 导航开/关：返回键据此先退导航而非退出模块
            case "nav":
                _navOpen = args.GetValueOrDefault("open", "") == "1";
                break;
        }
    }

    // "app://voice?id=warm" → ("voice", {id:warm})
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

    // ── 语音包选择持久化（读写静默容错，与抖音关注一致）──────────────────────
    private async Task EnsureVoiceLoadedAsync()
    {
        if (_voiceLoaded) return;
        _voiceLoaded = true;
        if (_localData == null) return;
        try
        {
            string? raw = await _localData.LoadSettingAsync(VoiceKey);
            if (!string.IsNullOrWhiteSpace(raw)) _voiceId = raw.Trim();
        }
        catch { /* 读取失败按默认语音包处理 */ }
    }

    private async Task SaveVoiceAsync()
    {
        if (_localData == null) return;
        try { await _localData.SaveSettingAsync(VoiceKey, _voiceId); }
        catch { /* 存储不可用时静默（仅本次会话生效） */ }
    }

    [RelayCommand]
    private void GoBack()
    {
        // ★ 导航进行中：返回键先退出导航（重建覆盖层回到路线选择），不退出模块
        if (_navOpen && _map != null)
        {
            _map.Hide();
            _ = ShowOverlayAsync();
            return;
        }
        _map?.Hide();
        WeakReferenceMessenger.Default.Send(new NavigateBackFromMapMessage());
    }
}
