using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaKit.Messages;
using AvaloniaKit.Tools.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  NeteaseViewModel  — 网易云音乐主页
// ══════════════════════════════════════════════════════════════════════════════
public partial class NeteaseViewModel : ObservableObject,
    IRecipient<NeteasePlayPrevMessage>,
    IRecipient<NeteasePlayNextMessage>
{
    // ── HTTP ─────────────────────────────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static NeteaseViewModel()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
    }

    public NeteaseViewModel()
    {
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    // ── Tab ───────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecommendActive))]
    [NotifyPropertyChangedFor(nameof(IsRankActive))]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    private int _activeTab = 0; // 0=推荐 1=排行榜 2=搜索
    partial void OnActiveTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsRecommendActive));
        OnPropertyChanged(nameof(IsRankActive));
        OnPropertyChanged(nameof(IsSearchActive));
    }

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

    [RelayCommand]
    private void SelectRank(int index)
    {
        if (index == SelectedRankIndex && RankSongs.Count > 0) return;
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
        if (RecommendSongs.Count == 0)
            _ = LoadRecommendAsync();
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
    //  推荐歌曲加载
    // ══════════════════════════════════════════════════════════════════════════
    private async Task LoadRecommendAsync()
    {
        if (IsRecommendLoading) return;
        IsRecommendLoading = true;
        RecommendSongs.Clear();
        StatusText = "加载中…";

        try
        {
            // ★ 对齐官方未登录态首页：「推荐新音乐」接口返回实时新歌
            await LoadPersonalizedNewSongAsync(RecommendSongs, 30);
            // 接口失效时退回热歌榜，再不行用离线数据
            if (RecommendSongs.Count == 0)
                await LoadPlaylistAsync(RecommendSongs, 3778678, 30);
            if (RecommendSongs.Count == 0)
                LoadRecommendFallback();
            StatusText = $"已加载 {RecommendSongs.Count} 首";
        }
        catch
        {
            LoadRecommendFallback();
            StatusText = "已加载（离线数据）";
        }
        finally { IsRecommendLoading = false; }
    }

    // ── 官方「推荐新音乐」：与网易云 App 未登录态推荐一致 ──
    private async Task LoadPersonalizedNewSongAsync(
        ObservableCollection<NeteaseSongItem> target, int limit)
    {
        try
        {
            string url = $"https://music.163.com/api/personalized/newsong?limit={limit}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
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
        RankSongs.Clear();

        try
        {
            long listId = RankCategories[SelectedRankIndex].ListId;
            await LoadPlaylistAsync(RankSongs, listId, 20);
            if (RankSongs.Count == 0) LoadRankFallback();
        }
        catch { LoadRankFallback(); }
        finally { IsRankLoading = false; }
    }

    // ── 通用歌单加载：v6 接口拿实时 trackIds → song/detail 批量取详情 ──
    // 老版 api/playlist/detail 返回的是陈旧缓存，与官方榜单不一致
    private async Task LoadPlaylistAsync(
        ObservableCollection<NeteaseSongItem> target, long listId, int limit)
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
            string url = $"https://music.163.com/api/v6/playlist/detail?id={listId}&n=0";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
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
        ObservableCollection<NeteaseSongItem> target, List<long> ids)
    {
        try
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(ids[i]).Append('}');
            }
            sb.Append(']');

            string url = $"https://music.163.com/api/v3/song/detail?c={Uri.EscapeDataString(sb.ToString())}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
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
        ObservableCollection<NeteaseSongItem> target, long listId, int limit)
    {
        string url = $"https://music.163.com/api/playlist/detail?id={listId}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
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
            string url = $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(keyword)}&type=1&limit=20&offset=0";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string raw = await _http.GetStringAsync(url, cts.Token);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("songs", out var songs) &&
                songs.ValueKind == JsonValueKind.Array)
            {
                var seen = new HashSet<long>();
                foreach (var s in songs.EnumerateArray())
                {
                    var item = ParseSearchSong(s);
                    if (item != null && seen.Add(item.Id))
                        SearchResults.Add(item);
                }
                SearchStatus = SearchResults.Count > 0
                    ? $"找到 {SearchResults.Count} 首"
                    : "未找到相关歌曲";
            }
            else
            {
                SearchStatus = "未找到相关歌曲";
            }
        }
        catch { SearchStatus = "搜索失败，请检查网络"; }
        finally { IsSearchLoading = false; }
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

    private static NeteaseSongItem? ParseSearchSong(JsonElement s)
    {
        try
        {
            long id = s.TryGetLong("id");
            if (id == 0) return null;
            string name = s.TryGetStr("name") ?? "未知歌曲";

            string artist = "未知歌手";
            if (s.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
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
            if (s.TryGetProperty("album", out var alb))
            {
                album = alb.TryGetStr("name") ?? "";
                cover = alb.TryGetStr("picUrl") ?? alb.TryGetStr("blurPicUrl") ?? "";
            }

            long durationMs = s.TryGetLong("duration");

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

// ══════════════════════════════════════════════════════════════════════════════
//  数据模型
// ══════════════════════════════════════════════════════════════════════════════
public partial class NeteaseSongItem : ObservableObject
{
    // ── 静态共享 HTTP 客户端 + 缓存 ──────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // ★ 修复：封面下载也必须带 UA/Referer，否则部分图片 CDN 会拒绝导致封面加载失败
    static NeteaseSongItem()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
    }

    // key = thumbUrl, value = Bitmap(可能为null代表加载失败)
    private static readonly ConcurrentDictionary<string, Bitmap?> _bmpCache = new();

    // ── 数据字段 ──────────────────────────────────────────────────────────────
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long DurationMs { get; set; }

    public string DurationText
    {
        get
        {
            if (DurationMs <= 0) return "";
            var ts = TimeSpan.FromMilliseconds(DurationMs);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    // ── CoverUrl：设置后自动触发异步封面加载 ─────────────────────────────────
    private string _coverUrl = "";
    public string CoverUrl
    {
        get => _coverUrl;
        set
        {
            if (_coverUrl == value) return;
            _coverUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCover));
            // 重置 Bitmap，触发重新加载
            _coverBitmap = null;
            OnPropertyChanged(nameof(CoverBitmap));
            if (!string.IsNullOrEmpty(value))
                _ = LoadCoverAsync(value);
        }
    }

    public bool HasCover => !string.IsNullOrEmpty(_coverUrl);

    // ── CoverBitmap：XAML 绑定此属性显示封面 ─────────────────────────────────
    private Bitmap? _coverBitmap;
    public Bitmap? CoverBitmap
    {
        get
        {
            // 如果还没开始加载但有 URL，触发一次加载
            if (_coverBitmap == null && !string.IsNullOrEmpty(_coverUrl))
                _ = LoadCoverAsync(_coverUrl);
            return _coverBitmap;
        }
    }

    // ── 异步加载封面 ──────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task LoadCoverAsync(string url)
    {
        // 加上缩略图参数（网易云支持）
        string thumbUrl = url.Contains('?')
            ? $"{url}&param=120y120"
            : $"{url}?param=120y120";

        // 命中缓存
        if (_bmpCache.TryGetValue(thumbUrl, out var cached))
        {
            if (_coverBitmap != cached)
            {
                _coverBitmap = cached;
                await Dispatcher.UIThread.InvokeAsync(
                    () => OnPropertyChanged(nameof(CoverBitmap)));
            }
            return;
        }

        // 防止重复下载
        if (!_bmpCache.TryAdd(thumbUrl, null))
        {
            // 另一个实例正在下载，等一会再读
            await System.Threading.Tasks.Task.Delay(600);
            if (_bmpCache.TryGetValue(thumbUrl, out var cached2))
            {
                _coverBitmap = cached2;
                await Dispatcher.UIThread.InvokeAsync(
                    () => OnPropertyChanged(nameof(CoverBitmap)));
            }
            return;
        }

        try
        {
            byte[] bytes = await _http.GetByteArrayAsync(thumbUrl).ConfigureAwait(false);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);

            _bmpCache[thumbUrl] = bmp;   // 更新缓存
            _coverBitmap = bmp;

            // 回 UI 线程通知绑定
            await Dispatcher.UIThread.InvokeAsync(
                () => OnPropertyChanged(nameof(CoverBitmap)));
        }
        catch
        {
            _bmpCache[thumbUrl] = null;   // 失败也缓存，避免重试风暴
        }
    }

    /// <summary>清空封面内存缓存（可在低内存时调用）</summary>
    public static void ClearCoverCache()
    {
        foreach (var bmp in _bmpCache.Values)
            bmp?.Dispose();
        _bmpCache.Clear();
    }
}


public partial class NeteaseRankCategory : ObservableObject
{
    public string Name { get; set; } = "";
    public long ListId { get; set; }
    public int Index { get; set; }
    [ObservableProperty] private bool _isSelected = false;
}

