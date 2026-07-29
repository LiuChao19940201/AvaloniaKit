using AvaloniaKit.Api;
using AvaloniaKit.Extensions;
using AvaloniaKit.Messages;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  NeteaseViewModel  — 网易云音乐主页
// ══════════════════════════════════════════════════════════════════════════════
public partial class NeteaseViewModel : PageViewModelBase, ISubPageViewModel, INavigationAware,
    IRecipient<NeteasePlayPrevMessage>,
    IRecipient<NeteasePlayNextMessage>
{
    public override string Title => "网易云音乐";
    public override bool ShowTitleBar => false;
    public override bool ShowTabBar => false;

    // ── HTTP ─────────────────────────────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static NeteaseViewModel()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
    }

    private readonly IAudioService? _audio;

    public NeteaseViewModel(IAudioService? audioService = null)
    {
        _audio = audioService;
        WeakReferenceMessenger.Default.RegisterAll(this);
        // ★ 启动预热：提前备好第一批随机推荐，首次进页秒开（对齐真实 App 体验）
        _ = PrefetchNextBatchAsync();
    }

    // ── Tab ───────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecommendActive))]
    [NotifyPropertyChangedFor(nameof(IsRankActive))]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    private int _activeTab = 0; // 0=推荐 1=排行榜 2=搜索

    public bool IsRecommendActive => ActiveTab == 0;
    public bool IsRankActive => ActiveTab == 1;
    public bool IsSearchActive => ActiveTab == 2;

    [RelayCommand] private void SwitchRecommend() { ActiveTab = 0; _ = LoadRecommendAsync(); }
    [RelayCommand] private void SwitchRank() { ActiveTab = 1; _ = LoadRankAsync(); }
    [RelayCommand] private void SwitchSearch() { ActiveTab = 2; }

    // ── 推荐页 ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isRecommendLoading = false;
    public ObservableCollection<NeteaseSongItem> RecommendSongs { get; } = new();

    // ── 排行榜 ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isRankLoading = false;

    public ObservableCollection<NeteaseRankCategory> RankCategories { get; } = new()
    {
        new NeteaseRankCategory { Name = "飙升榜", ListId = 19723756,  Index = 0, IsSelected = true  },
        new NeteaseRankCategory { Name = "新歌榜", ListId = 3779629,   Index = 1, IsSelected = false },
        new NeteaseRankCategory { Name = "热歌榜", ListId = 3778678,   Index = 2, IsSelected = false },
        new NeteaseRankCategory { Name = "原创榜", ListId = 2884035,   Index = 3, IsSelected = false },
    };

    [ObservableProperty] private int _selectedRankIndex = 0;
    public ObservableCollection<NeteaseSongItem> RankSongs { get; } = new();

    // ── 数据新鲜度：排行榜离线兑底在下次选择时自动重试真实接口 ──────────
    private bool _rankIsFallback;

    [RelayCommand]
    private void SelectRank(int index)
    {
        // 同榜单且已有“真实”数据才跳过；离线兜底数据允许重试
        if (index == SelectedRankIndex && RankSongs.Count > 0 && !_rankIsFallback) return;
        for (int i = 0; i < RankCategories.Count; i++)
            RankCategories[i].IsSelected = i == index;
        SelectedRankIndex = index;
        _ = LoadRankAsync();
    }

    // ── 搜索页 ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _searchKeyword = "";
    [ObservableProperty] private bool _isSearchLoading = false;
    [ObservableProperty] private string _searchStatus = "";
    public ObservableCollection<NeteaseSongItem> SearchResults { get; } = new();

    // ── 当前播放状态（用于迷你播放栏 + 上/下一曲）───────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentSong))]
    private NeteaseSongItem? _currentSong;
    public bool HasCurrentSong => CurrentSong != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayBtnIcon))]
    private bool _isPlaying = false;

    [ObservableProperty] private string _miniProgress = "0:00 / 0:00";

    public string PlayBtnIcon => IsPlaying
        ? "M6 19H10V5H6V19ZM14 5V19H18V5H14Z"
        : "M8 5V19L19 12Z";

    // ── ★ 当前播放列表引用（上/下一曲用）────────────────────────────────────
    // 记录"当前激活列表"及索引，PlaySong 时同步更新
    private ObservableCollection<NeteaseSongItem>? _activeList;
    private int _activeIndex = -1;

    // ── 状态 ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _statusText = "";

    public void OnNavigatedTo()
    {
        // ★ 每次进入（含返回后再进）内容都不一样，且进门秒开：
        //   双缓冲预取——上次离开后已在后台备好新一批，进门直接换上，
        //   再为下次进入继续备货（无任何本地记录，纯内存）
        _ = RefreshRecommendOnEntryAsync();
        SyncPlaybackState();
    }

    private async Task RefreshRecommendOnEntryAsync()
    {
        if (_nextBatch is { Count: > 0 } ready)
        {
            _nextBatch = null;
            SwapRecommend(ready);              // ★ 秒开：直接换上备好的新批次
            _ = PrefetchNextBatchAsync();      // 后台为下次进入备货
            return;
        }
        // 无备货（冷启动/上次预取失败）：现拉，旧内容保持可见不白屏
        await LoadRecommendAsync();
        _ = PrefetchNextBatchAsync();
    }

    // ── ★ 同步迷你播放栏状态（从播放器页返回时调用）─────────────────────────
    public void SyncPlaybackState()
    {
        if (_audio != null && CurrentSong != null)
            IsPlaying = _audio.IsPlaying;
    }

    // ── 导航 ─────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void GoBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackFromNeteaseMessage());

    // ── 播放歌曲（跳转播放器页）──────────────────────────────────────────────
    [RelayCommand]
    private void PlaySong(NeteaseSongItem? item)
    {
        if (item is null) return;

        // ★ 记录当前激活列表及索引
        _activeList = GetActiveList();
        _activeIndex = _activeList.IndexOf(item);

        CurrentSong = item;
        IsPlaying = true;
        SendNavigateToPlayer(item);
    }

    [RelayCommand]
    private void OpenPlayer()
    {
        if (CurrentSong is null) return;
        SendNavigateToPlayer(CurrentSong);
    }

    // ── ★ 迷你播放栏：播放/暂停按钮只控制播放状态，不跳转详情页 ────────────
    [RelayCommand]
    private void TogglePlay()
    {
        if (CurrentSong is null) return;

        if (_audio != null && _audio.DurationMs > 0)
        {
            if (_audio.IsPlaying) { _audio.Pause(); IsPlaying = false; }
            else { _audio.Resume(); IsPlaying = true; }
            return;
        }
        // 音频尚未加载（异常场景）：进播放器页重新加载
        SendNavigateToPlayer(CurrentSong);
    }

    private void SendNavigateToPlayer(NeteaseSongItem item)
    {
        WeakReferenceMessenger.Default.Send(new NavigateToNeteasePlayerMessage
        {
            SongId = item.Id,
            SongName = item.Name,
            Artist = item.Artist,
            Album = item.Album,
            CoverUrl = item.CoverUrl,
        });
    }

    // ── ★ 上/下一曲消息处理 ───────────────────────────────────────────────────
    public void Receive(NeteasePlayPrevMessage message) => PlayOffset(-1);
    public void Receive(NeteasePlayNextMessage message)
    {
        // ★ 随机模式：从当前列表随机选一首（尽量避开当前曲目）
        if (message.Random) PlayRandom();
        else PlayOffset(+1);
    }

    private void PlayRandom()
    {
        var list = _activeList;
        if (list == null || list.Count == 0) return;

        int newIndex = _activeIndex;
        if (list.Count > 1)
            while (newIndex == _activeIndex)
                newIndex = Random.Shared.Next(list.Count);

        _activeIndex = newIndex;
        var item = list[newIndex];
        CurrentSong = item;
        IsPlaying = true;
        SendNavigateToPlayer(item);
    }

    private void PlayOffset(int offset)
    {
        var list = _activeList;
        if (list == null || list.Count == 0) return;

        int newIndex = (_activeIndex + offset + list.Count) % list.Count;
        _activeIndex = newIndex;
        var item = list[newIndex];
        CurrentSong = item;
        IsPlaying = true;
        SendNavigateToPlayer(item);
    }

    // ── 返回当前激活列表（Tab决定）──────────────────────────────────────────
    private ObservableCollection<NeteaseSongItem> GetActiveList() => ActiveTab switch
    {
        1 => RankSongs,
        2 => SearchResults,
        _ => RecommendSongs,
    };

    // ══════════════════════════════════════════════════════════════════════════
    //  推荐歌曲加载 —— 纯随机、每次进入都不一样（不做任何本地记录/过滤）：
    //  ① 候选池 = 「推荐新音乐」接口 + 随机一个榜单的全量 trackIds；
    //  ② 洗牌抽样 30 首再打乱顺序（随机榜单 + 随机抽取，天然次次不同）
    //  —— 修复：原实现直拉固定接口前 30 首，服务端排序几天不变，
    //     导致每次打开都是同样的歌
    // ══════════════════════════════════════════════════════════════════════════
    private const int RecommendCount = 30;
    private const int MaxFromNewSong = 12;   // 新歌接口最多占用名额（保榜单多样性）

    // ── 双缓冲预取：备好的下一批（纯内存，不落盘）──
    private List<NeteaseSongItem>? _nextBatch;
    private Task? _prefetchTask;

    private Task PrefetchNextBatchAsync()
        => _prefetchTask is { IsCompleted: false } running ? running
                                                           : _prefetchTask = PrefetchCoreAsync();

    private async Task PrefetchCoreAsync()
    {
        try
        {
            var batch = await BuildRandomBatchAsync();
            if (batch.Count > 0) _nextBatch = batch;
        }
        catch { /* 预取失败不打扰，进页时走现拉兼兑底 */ }
    }

    private void SwapRecommend(List<NeteaseSongItem> batch)
    {
        RecommendSongs.Clear();
        foreach (var it in batch) RecommendSongs.Add(it);
        StatusText = $"已加载 {batch.Count} 首";
    }

    private async Task LoadRecommendAsync()
    {
        if (IsRecommendLoading) return;
        IsRecommendLoading = true;
        // ★ 不再进门先清空：旧内容保持可见，新批次就绪后一次性替换（告别白屏转圈）
        if (RecommendSongs.Count == 0) StatusText = "加载中…";

        try
        {
            var batch = await BuildRandomBatchAsync();
            if (batch.Count > 0) SwapRecommend(batch);
            else if (RecommendSongs.Count == 0)
            {
                LoadRecommendFallback();
                StatusText = "已加载（离线数据）";
            }
            else StatusText = "网络不稳，已保留当前列表";
        }
        catch
        {
            if (RecommendSongs.Count == 0)
            {
                LoadRecommendFallback();
                StatusText = "已加载（离线数据）";
            }
            else StatusText = "网络不稳，已保留当前列表";
        }
        finally { IsRecommendLoading = false; }
    }

    // 构建一批全新随机推荐（预取与现拉共用同一条流水线）
    private async Task<List<NeteaseSongItem>> BuildRandomBatchAsync()
    {
        var picked = new List<NeteaseSongItem>();
        var pickedIds = new HashSet<long>();

        // ★ 候选池 A（推荐新音乐）与 B（随机榜单全量 trackIds）并行拉取，
        //   省掉一次串行网络往返（原 A→B→详情三跳串行是慢的主因之一）
        var poolA = new List<NeteaseSongItem>();
        long listId = RankCategories[Random.Shared.Next(RankCategories.Count)].ListId;
        var taskA = LoadPersonalizedNewSongAsync(poolA, RecommendCount);
        var taskB = GetPlaylistTrackIdsAsync(listId, 200);
        await Task.WhenAll(taskA, taskB);
        var poolB = taskB.Result;
        Shuffle(poolA);
        Shuffle(poolB);

        // ① 新歌池随机先取一部分
        foreach (var it in poolA)
        {
            if (picked.Count >= MaxFromNewSong) break;
            if (!pickedIds.Add(it.Id)) continue;
            picked.Add(it);
        }

        // ② 榜单池随机凑满 30；榜单失效时用新歌池剩余垫底
        var wantIds = new List<long>();
        foreach (var id in poolB)
        {
            if (picked.Count + wantIds.Count >= RecommendCount) break;
            if (pickedIds.Contains(id)) continue;
            wantIds.Add(id);
        }
        if (picked.Count + wantIds.Count < RecommendCount)
            foreach (var it in poolA)
            {
                if (picked.Count + wantIds.Count >= RecommendCount) break;
                if (!pickedIds.Add(it.Id)) continue;
                picked.Add(it);
            }

        if (wantIds.Count > 0)
        {
            var details = new List<NeteaseSongItem>();
            await LoadSongDetailAsync(details, wantIds);
            foreach (var it in details)
                if (pickedIds.Add(it.Id)) picked.Add(it);
        }

        // ③ 打乱最终顺序
        Shuffle(picked);
        if (picked.Count > RecommendCount)
            picked.RemoveRange(RecommendCount, picked.Count - RecommendCount);
        return picked;
    }

    // Fisher-Yates 洗牌
    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── 官方「推荐新音乐」：与网易云 App 未登录态推荐一致 ──
    private async Task LoadPersonalizedNewSongAsync(
        IList<NeteaseSongItem> target, int limit)
    {
        try
        {
            string url = NeteaseApi.PersonalizedNewSong(limit);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            string raw = await _http.GetStringAsync(url, cts.Token);

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Array)
                return;

            var seen = new HashSet<long>();
            foreach (var entry in result.EnumerateArray())
            {
                if (!entry.TryGetProperty("song", out var song)) continue;
                var item = ParseSongItem(song);
                if (item == null || !seen.Add(item.Id)) continue;
                // 封面优先用外层 picUrl（song.album 里可能缺失）
                if (string.IsNullOrEmpty(item.CoverUrl))
                    item.CoverUrl = entry.TryGetStr("picUrl") ?? "";
                target.Add(item);
            }
        }
        catch { /* 保持 target 为空，由调用方兜底 */ }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  排行榜加载
    // ══════════════════════════════════════════════════════════════════════════
    private async Task LoadRankAsync()
    {
        if (IsRankLoading) return;
        IsRankLoading = true;

        try
        {
            // ★ 先拉到临时列表再整体替换：刷新/切榜期间旧内容保持可见不白屏
            long listId = RankCategories[SelectedRankIndex].ListId;
            var tmp = new List<NeteaseSongItem>();
            await LoadPlaylistAsync(tmp, listId, 20);
            _rankIsFallback = tmp.Count == 0;
            RankSongs.Clear();
            if (_rankIsFallback) LoadRankFallback();
            else foreach (var s in tmp) RankSongs.Add(s);
        }
        catch
        {
            _rankIsFallback = true;
            RankSongs.Clear();
            LoadRankFallback();
        }
        finally { IsRankLoading = false; }
    }

    // ── 通用歌单加载：v6 接口拿实时 trackIds → song/detail 批量取详情 ──
    // 老版 api/playlist/detail 返回的是陈旧缓存，与官方榜单不一致
    private async Task LoadPlaylistAsync(
        IList<NeteaseSongItem> target, long listId, int limit)
    {
        var ids = await GetPlaylistTrackIdsAsync(listId, limit);
        if (ids.Count > 0)
        {
            await LoadSongDetailAsync(target, ids);
            if (target.Count > 0) return;
        }
        // 兜底：老接口（数据可能滞后，但好过没有）
        await LoadPlaylistLegacyAsync(target, listId, limit);
    }

    private async Task<List<long>> GetPlaylistTrackIdsAsync(long listId, int limit)
    {
        var ids = new List<long>();
        try
        {
            string url = NeteaseApi.PlaylistDetail(listId);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            string raw = await _http.GetStringAsync(url, cts.Token);

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("playlist", out var playlist) &&
                playlist.TryGetProperty("trackIds", out var trackIds) &&
                trackIds.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in trackIds.EnumerateArray())
                {
                    long id = t.TryGetLong("id");
                    if (id != 0) ids.Add(id);
                    if (ids.Count >= limit) break;
                }
            }
        }
        catch { /* 返回空，走老接口兜底 */ }
        return ids;
    }

    private async Task LoadSongDetailAsync(
        IList<NeteaseSongItem> target, List<long> ids)
    {
        try
        {
            string url = NeteaseApi.SongDetail(ids);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            string raw = await _http.GetStringAsync(url, cts.Token);

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("songs", out var songs) ||
                songs.ValueKind != JsonValueKind.Array)
                return;

            var seen = new HashSet<long>();
            foreach (var s in songs.EnumerateArray())
            {
                var item = ParseSongItem(s);
                if (item != null && seen.Add(item.Id))
                    target.Add(item);
            }
        }
        catch { /* 保持 target 为空，由调用方兜底 */ }
    }

    private async Task LoadPlaylistLegacyAsync(
        IList<NeteaseSongItem> target, long listId, int limit)
    {
        string url = NeteaseApi.PlaylistDetailLegacy(listId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        string raw = await _http.GetStringAsync(url, cts.Token);

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (!root.TryGetProperty("result", out var result) &&
            !root.TryGetProperty("playlist", out result))
            return;

        if (!result.TryGetProperty("tracks", out var trackList))
            return;

        int count = 0;
        var seen = new HashSet<long>();
        foreach (var t in trackList.EnumerateArray())
        {
            if (count >= limit) break;
            var item = ParseSongItem(t);
            if (item != null && seen.Add(item.Id))
            {
                target.Add(item);
                count++;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  搜索
    // ══════════════════════════════════════════════════════════════════════════
    [RelayCommand]
    private async Task SearchAsync()
    {
        string keyword = SearchKeyword.Trim();
        if (string.IsNullOrEmpty(keyword)) return;

        IsSearchLoading = true;
        SearchResults.Clear();
        SearchStatus = "搜索中…";

        try
        {
            // ★ 官方 cloudsearch 接口：返回 v3 结构（ar/al/dt + privilege），
            //   与推荐/榜单同一条解析管线，封面、时长齐全，并可过滤不可播曲目
            await SearchCloudAsync(keyword);
            // 兜底：老 web 接口只取 id，再走 song/detail 补全封面
            if (SearchResults.Count == 0)
                await SearchLegacyAsync(keyword);

            SearchStatus = SearchResults.Count > 0
                ? $"找到 {SearchResults.Count} 首"
                : "未找到可播放的歌曲";
        }
        catch { SearchStatus = "搜索失败，请检查网络"; }
        finally { IsSearchLoading = false; }
    }

    // ── ★ cloudsearch：官方搜索主链路 ──
    private async Task SearchCloudAsync(string keyword)
    {
        try
        {
            string url = NeteaseApi.CloudSearch(keyword, 30);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string raw = await _http.GetStringAsync(url, cts.Token);
            using var doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("songs", out var songs) ||
                songs.ValueKind != JsonValueKind.Array)
                return;

            var seen = new HashSet<long>();
            foreach (var s in songs.EnumerateArray())
            {
                // ★ 过滤播不了的：已下架(st<0) 或匿名无任何可播码率(pl<=0，VIP/无版权)
                if (s.TryGetProperty("privilege", out var priv) &&
                    priv.ValueKind == JsonValueKind.Object)
                {
                    if (priv.TryGetLong("st") < 0)
                        continue;
                    // pl 字段存在才判断（镜像接口可能不带 pl，避免误杀全部结果）
                    if (priv.TryGetProperty("pl", out var pl) &&
                        pl.ValueKind == JsonValueKind.Number && pl.GetInt64() <= 0)
                        continue;
                }

                var item = ParseSongItem(s);
                if (item != null && seen.Add(item.Id))
                    SearchResults.Add(item);
            }
        }
        catch { /* 保持为空，由老接口兜底 */ }
    }

    // ── 老 web 搜索兜底：只取 id，经 song/detail 补全封面/时长 ──
    private async Task SearchLegacyAsync(string keyword)
    {
        string url = NeteaseApi.SearchLegacy(keyword, 20);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string raw = await _http.GetStringAsync(url, cts.Token);
        using var doc = JsonDocument.Parse(raw);

        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array)
            return;

        var ids = new List<long>();
        foreach (var s in songs.EnumerateArray())
        {
            long id = s.TryGetLong("id");
            if (id != 0 && !ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count > 0)
            await LoadSongDetailAsync(SearchResults, ids);
    }

    // ── 解析歌曲（兼容 v3 song/detail 的 ar/al/dt 与老接口的 artists/album/duration）──
    private static NeteaseSongItem? ParseSongItem(JsonElement t)
    {
        try
        {
            long id = t.TryGetLong("id");
            if (id == 0) return null;
            string name = t.TryGetStr("name") ?? "未知歌曲";

            string artist = "未知歌手";
            if (t.TryGetProperty("ar", out var ar) && ar.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var a in ar.EnumerateArray())
                {
                    string? n = a.TryGetStr("name");
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
                if (names.Count > 0) artist = string.Join("/", names);
            }
            else if (t.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var a in artists.EnumerateArray())
                {
                    string? n = a.TryGetStr("name");
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
                if (names.Count > 0) artist = string.Join("/", names);
            }

            string album = "";
            string cover = "";
            if (t.TryGetProperty("al", out var al))
            {
                album = al.TryGetStr("name") ?? "";
                cover = al.TryGetStr("picUrl") ?? "";
            }
            else if (t.TryGetProperty("album", out var alb))
            {
                album = alb.TryGetStr("name") ?? "";
                cover = alb.TryGetStr("picUrl") ?? alb.TryGetStr("blurPicUrl") ?? "";
            }

            long durationMs = t.TryGetLong("dt");
            if (durationMs == 0) durationMs = t.TryGetLong("duration");

            return new NeteaseSongItem
            {
                Id = id,
                Name = name,
                Artist = artist,
                Album = album,
                CoverUrl = cover,
                DurationMs = durationMs,
            };
        }
        catch { return null; }
    }

    // ── Fallback 离线数据 ─────────────────────────────────────────────────────
    private void LoadRecommendFallback()
    {
        var fallback = new[]
        {
            (2044745257L, "失眠飞行",     "沈以诚/薛明媛",   "失眠飞行",    ""),
            (1974443814L, "漠河舞厅",     "柳爽",            "漠河舞厅",    ""),
            (1490661558L, "易燃易爆炸",   "华晨宇",          "异类",        ""),
            (1311845667L, "起风了",       "买辣椒也用券",    "起风了",      ""),
            (1374405649L, "我记得",       "赵雷",            "我记得",      ""),
            (28391863L,   "七里香",       "周杰伦",          "七里香",      ""),
            (186016L,     "晴天",         "周杰伦",          "叶惠美",      ""),
            (1859245754L, "心如止水",     "Ice Paper",       "心如止水",    ""),
        };
        foreach (var (id, name, artist, album, cover) in fallback)
        {
            if (!SongExists(RecommendSongs, id))
                RecommendSongs.Add(new NeteaseSongItem { Id = id, Name = name, Artist = artist, Album = album, CoverUrl = cover });
        }
    }

    private void LoadRankFallback()
    {
        var fallback = new[]
        {
            (2044745257L, "失眠飞行",   "沈以诚/薛明媛"),
            (1974443814L, "漠河舞厅",   "柳爽"),
            (1311845667L, "起风了",     "买辣椒也用券"),
            (1490661558L, "易燃易爆炸", "华晨宇"),
            (1374405649L, "我记得",     "赵雷"),
        };
        foreach (var (id, name, artist) in fallback)
        {
            if (!SongExists(RankSongs, id))
                RankSongs.Add(new NeteaseSongItem { Id = id, Name = name, Artist = artist });
        }
    }

    private static bool SongExists(ObservableCollection<NeteaseSongItem> col, long id)
    {
        foreach (var s in col) if (s.Id == id) return true;
        return false;
    }
}
