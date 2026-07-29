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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  FundTrackerViewModel  （增强版）
//  · ActiveTab（0=发现  1=自选）
//  · 发现 Tab：分类标签（矢量图标）+ 分类基金排行榜（5 分钟内存缓存，切回秒开）
//  · 自选 Tab：净值并发刷新（Task.WhenAll，替代原串行逐只等待）
//  · 自选码表经 ILocalDataService 持久化（三端 JSON 文件 / localStorage），重开可恢复
//  · DiscoverFundItem：可「+」一键添加到自选，可点击跳转图表
// ══════════════════════════════════════════════════════════════════════════════
public partial class FundTrackerViewModel : PageViewModelBase, ISubPageViewModel, INavigationAware
{
    public override string Title => "基金自选跟踪";
    public override bool ShowTitleBar => false;
    public override bool ShowTabBar => false;

    // ── HTTP ─────────────────────────────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static FundTrackerViewModel()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Referer", "https://fund.eastmoney.com/");
    }

    private readonly ILocalDataService? _localData;

    public FundTrackerViewModel(ILocalDataService? localDataService = null)
        => _localData = localDataService;

    // ── 持久化：ILocalDataService（三端 JSON 文件 / localStorage），代码逗号拼接存储 ──
    //    旧版写安装目录 fund_watchlist.json（AOT 发布后目录只读会静默失败），
    //    首次读库为空时做一次性迁移
    private const string WatchlistKey = "fund_watchlist";

    private readonly ObservableCollection<string> _watchCodes = new();
    private Task? _watchlistLoadTask;

    // ── 状态属性 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _isOffline = false;
    [ObservableProperty] private string _statusText = "";

    // ── 搜索面板 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _showSearch = false;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching = false;
    [ObservableProperty] private string _searchStatus = "";

    // ── 自选基金列表 ──────────────────────────────────────────────────────────
    public ObservableCollection<FundItemViewModel> Funds { get; } = new();

    // ── 搜索结果列表 ──────────────────────────────────────────────────────────
    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();

    private CancellationTokenSource? _refreshCts;

    // ════════════════════════════════════════════════════════════════════════
    //  Tab 切换
    // ════════════════════════════════════════════════════════════════════════
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverActive))]
    [NotifyPropertyChangedFor(nameof(IsWatchlistActive))]
    private int _activeTab = 0;   // 0=发现  1=自选

    public bool IsDiscoverActive => ActiveTab == 0;
    public bool IsWatchlistActive => ActiveTab == 1;

    [RelayCommand] private void SwitchToDiscover() => ActiveTab = 0;

    [RelayCommand]
    private void SwitchToWatchlist()
    {
        ActiveTab = 1;
        // 已有数据时不重复全量拉取（顶部有专门的刷新按钮），切 Tab 秒开
        if (Funds.Count == 0 || IsOffline)
            _ = DoRefreshAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  发现 Tab —— 热门分类
    // ════════════════════════════════════════════════════════════════════════
    public ObservableCollection<DiscoverCategory> DiscoverCategories { get; } = new()
    {
        // 图标为 Material 风格 Path（与全局扁平矢量图标统一，替代原 emoji）
        new DiscoverCategory { Label = "热门",   FundType = "hot",   Index = 0, IsSelected = true,
            Icon = "M13.5.67s.74 2.65.74 4.8c0 2.06-1.35 3.73-3.41 3.73-2.07 0-3.63-1.67-3.63-3.73l.03-.36C5.21 7.51 4 10.62 4 14c0 4.42 3.58 8 8 8s8-3.58 8-8C20 8.61 17.41 3.8 13.5.67zM11.71 19c-1.78 0-3.22-1.4-3.22-3.14 0-1.62 1.05-2.76 2.81-3.12 1.77-.36 3.6-1.21 4.62-2.58.39 1.29.59 2.65.59 4.04 0 2.65-2.15 4.8-4.8 4.8z" },
        new DiscoverCategory { Label = "股票型", FundType = "stock", Index = 1, IsSelected = false,
            Icon = "M16 6l2.29 2.29-4.88 4.88-4-4L2 16.59 3.41 18l6-6 4 4 6.3-6.29L22 12V6z" },
        new DiscoverCategory { Label = "指数型", FundType = "index", Index = 2, IsSelected = false,
            Icon = "M10 20h4V4h-4v16zm-6 0h4v-8H4v8zM16 9v11h4V9h-4z" },
        new DiscoverCategory { Label = "QDII",   FundType = "qdii",  Index = 3, IsSelected = false,
            Icon = "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zm6.93 6h-2.95c-.32-1.25-.78-2.45-1.38-3.56 1.84.63 3.37 1.91 4.33 3.56zM12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96zM4.26 14C4.1 13.36 4 12.69 4 12s.1-1.36.26-2h3.38c-.08.66-.14 1.32-.14 2 0 .68.06 1.34.14 2H4.26zm.82 2h2.95c.32 1.25.78 2.45 1.38 3.56-1.84-.63-3.37-1.9-4.33-3.56zm2.95-8H5.08c.96-1.66 2.49-2.93 4.33-3.56C8.81 5.55 8.35 6.75 8.03 8zM12 19.96c-.83-1.2-1.48-2.53-1.91-3.96h3.82c-.43 1.43-1.08 2.76-1.91 3.96zM14.34 14H9.66c-.09-.66-.16-1.32-.16-2 0-.68.07-1.35.16-2h4.68c.09.65.16 1.32.16 2 0 .68-.07 1.34-.16 2zm.25 5.56c.6-1.11 1.06-2.31 1.38-3.56h2.95c-.96 1.65-2.49 2.93-4.33 3.56zM16.36 14c.08-.66.14-1.32.14-2 0-.68-.06-1.34-.14-2h3.38c.16.64.26 1.31.26 2s-.1 1.36-.26 2h-3.38z" },
        new DiscoverCategory { Label = "债券型", FundType = "bond",  Index = 4, IsSelected = false,
            Icon = "M4 10v7h3v-7H4zm6 0v7h3v-7h-3zM2 22h19v-3H2v3zm14-12v7h3v-7h-3zm-4.5-9L2 6v2h19V6l-9.5-5z" },
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCategory))]
    private int _selectedCategoryIndex = 0;

    public DiscoverCategory? SelectedCategory =>
        SelectedCategoryIndex >= 0 && SelectedCategoryIndex < DiscoverCategories.Count
            ? DiscoverCategories[SelectedCategoryIndex]
            : null;

    [ObservableProperty] private bool _isDiscoverLoading = false;

    public ObservableCollection<DiscoverFundItem> DiscoverFunds { get; } = new();

    // ── 发现榜单缓存：分类 → (条目, 拉取时间)，TTL 内切回分类直接命中不再请求 ──
    private readonly Dictionary<string, (List<DiscoverFundItem> Items, DateTime At)> _discoverCache = new();
    private static readonly TimeSpan DiscoverCacheTtl = TimeSpan.FromMinutes(5);
    private int _discoverVersion;   // 防止慢响应覆盖后选分类的数据

    [RelayCommand]
    private void SelectCategory(int index)
    {
        if (index == SelectedCategoryIndex && DiscoverFunds.Count > 0) return;
        // 更新选中状态
        for (int i = 0; i < DiscoverCategories.Count; i++)
            DiscoverCategories[i].IsSelected = (i == index);
        SelectedCategoryIndex = index;
        _ = LoadDiscoverAsync(DiscoverCategories[index].FundType);
    }

    private async Task LoadDiscoverAsync(string fundType)
    {
        int version = ++_discoverVersion;

        // 缓存命中：直接展示（仅同步 IsAdded 状态），切分类秒开
        if (_discoverCache.TryGetValue(fundType, out var cached) &&
            DateTime.Now - cached.At < DiscoverCacheTtl)
        {
            DiscoverFunds.Clear();
            foreach (var f in cached.Items)
            {
                f.IsAdded = _watchCodes.Contains(f.Code);
                DiscoverFunds.Add(f);
            }
            IsDiscoverLoading = false;
            return;
        }

        IsDiscoverLoading = true;
        DiscoverFunds.Clear();

        try
        {
            // 东方财富基金排行 API
            // fundType 映射 → ft 参数（25=股票型, 27=指数型, 26=混合型, 31=债券型, 0=全部）
            string ft = fundType switch
            {
                "stock" => "25",
                "index" => "27",
                "bond" => "31",
                "qdii" => "35",
                _ => "0"     // hot / 全部
            };

            // 按近1月涨幅排序，取前20条（FundApi：Web 端自动套 CORS 代理，三端真实数据一致）
            string url = FundApi.Rank(ft, DateTime.Today.AddMonths(-1), DateTime.Today, 20);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            string raw = await _http.GetStringAsync(url, cts.Token);
            if (version != _discoverVersion) return;   // 已切到其他分类，丢弃过期响应

            // 响应格式: var rankData = {datas:["code,abbr,name,...", ...], ...}
            var m = Regex.Match(raw, @"datas:\[(.+?)\]", RegexOptions.Singleline);
            if (!m.Success)
            {
                LoadDiscoverFallback();
                return;
            }

            // 拆分每条记录（逗号分隔的字符串，用引号括起）
            var seenCodes = new HashSet<string>();   // 去重
            var loaded = new List<DiscoverFundItem>();
            var entries = Regex.Matches(m.Groups[1].Value, @"""([^""]+)""");
            foreach (Match entry in entries)
            {
                var parts = entry.Groups[1].Value.Split(',');
                if (parts.Length < 6) continue;

                string code = parts[0].Trim();
                string abbr = parts[1].Trim();
                string name = parts[2].Trim();
                string navStr = parts[3].Trim();
                string nav2Str = parts[4].Trim();
                string chgStr = parts[5].Trim();

                if (!seenCodes.Add(code)) continue;   // 跳过重复 code

                if (!double.TryParse(navStr, out double nav)) nav = 0;
                if (!double.TryParse(chgStr, out double chg)) chg = 0;

                loaded.Add(new DiscoverFundItem
                {
                    Code = code,
                    Name = string.IsNullOrWhiteSpace(name) ? abbr : name,
                    NavStr = nav > 0 ? nav.ToString("F4") : "--",
                    ChangeRaw = chg,
                    IsAdded = _watchCodes.Contains(code),
                });
            }

            if (loaded.Count == 0)
            {
                LoadDiscoverFallback();
                return;
            }

            foreach (var f in loaded)
                DiscoverFunds.Add(f);
            _discoverCache[fundType] = (loaded, DateTime.Now);   // 仅成功数据入缓存，fallback 不缓存
        }
        catch
        {
            if (version == _discoverVersion) LoadDiscoverFallback();
        }
        finally
        {
            if (version == _discoverVersion) IsDiscoverLoading = false;
        }
    }

    private void LoadDiscoverFallback()
    {
        var fallback = new[]
        {
            ("110022", "易方达消费行业股票",   "3.2100",  18.56),
            ("161725", "招商中证白酒指数(LOF)","1.1560",  15.23),
            ("270042", "广发纳斯达克100",      "2.6780",  12.88),
            ("000961", "天弘沪深300ETF联接A",  "1.3210",   8.04),
            ("000001", "华夏成长混合",          "1.8423",   5.31),
            ("519674", "银河创新成长混合",      "2.4400",   4.76),
            ("007119", "汇添富中证新能源汽车",  "1.1880",   3.92),
            ("008888", "华夏中证科技50ETF联接", "1.0523",  -1.24),
        };
        var existing = new HashSet<string>(DiscoverFunds.Select(f => f.Code));
        foreach (var (code, name, nav, chg) in fallback)
        {
            if (!existing.Add(code)) continue;   // 跳过已存在的 code
            DiscoverFunds.Add(new DiscoverFundItem
            {
                Code = code,
                Name = name,
                NavStr = nav,
                ChangeRaw = chg,
                IsAdded = _watchCodes.Contains(code),
            });
        }
    }

    // ★ 从发现列表一键 + 添加到自选
    [RelayCommand]
    private async Task AddDiscoverFund(DiscoverFundItem? item)
    {
        if (item is null) return;
        await EnsureWatchlistLoadedAsync();   // 防止码表未加载完就写入导致覆盖丢数据
        if (_watchCodes.Contains(item.Code))
        {
            item.IsAdded = true;
            return;
        }
        _watchCodes.Add(item.Code);
        SaveWatchlist();
        item.IsAdded = true;

        // 后台拉取净值并插入自选列表（去重：避免并发或重复触发时重复添加）
        if (Funds.Any(f => f.Code == item.Code)) return;
        var fund = await FetchFundAsync(item.Code, CancellationToken.None);
        if (Funds.All(f => f.Code != fund.Code))
            Funds.Add(fund);
    }

    // ★ 从发现列表点击跳转图表页
    [RelayCommand]
    private void OpenDiscoverChart(DiscoverFundItem? item)
    {
        if (item is null) return;
        WeakReferenceMessenger.Default.Send(
            new NavigateToFundChartMessage(item.Code, item.Name));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  以下原有逻辑完全不变
    // ════════════════════════════════════════════════════════════════════════

    public FundTrackerViewModel()
    {
        _ = EnsureWatchlistLoadedAsync();
    }

    public void OnNavigatedTo() => _ = OnNavigatedToAsync();

    private async Task OnNavigatedToAsync()
    {
        // 榜单 IsAdded 标记与自选刷新都依赖自选码表，先确保其加载完成
        await EnsureWatchlistLoadedAsync();

        // 每次进入页面：发现Tab预加载，自选不重复刷新
        if (DiscoverFunds.Count == 0)
            _ = LoadDiscoverAsync(DiscoverCategories[SelectedCategoryIndex].FundType);

        if (ActiveTab == 1 && (Funds.Count == 0 || IsOffline))
            _ = DoRefreshAsync();
    }

    [RelayCommand]
    private void GoBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackFromFundTrackerMessage());

    [RelayCommand]
    private void Refresh() => _ = DoRefreshAsync();

    private async Task DoRefreshAsync()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        IsLoading = true;
        IsOffline = false;
        StatusText = "加载中…";

        await EnsureWatchlistLoadedAsync();

        if (_watchCodes.Count == 0)
        {
            Funds.Clear();
            IsLoading = false;
            StatusText = "自选列表为空，点击「+」添加基金";
            return;
        }

        int ok = 0;
        try
        {
            // ★ 并发抓取全部自选（原 foreach 串行逐只等待，N 只基金 N 次顺序往返是慢的主因）；
            //   抓完再整体替换列表，刷新期间旧数据保持可见
            var codes = _watchCodes.ToList();
            var items = await Task.WhenAll(codes.Select(c => FetchFundAsync(c, ct)));
            ct.ThrowIfCancellationRequested();

            Funds.Clear();
            foreach (var item in items)
            {
                Funds.Add(item);
                if (!item.IsMock) ok++;
            }
        }
        catch (OperationCanceledException) { return; }
        finally { IsLoading = false; }

        if (ok == 0) { IsOffline = true; StatusText = "网络不可用，显示本地数据"; }
        else StatusText = $"更新于 {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        ShowSearch = !ShowSearch;
        if (!ShowSearch) { SearchText = ""; SearchStatus = ""; SearchResults.Clear(); }
    }

    [RelayCommand]
    private async Task SearchFund()
    {
        string keyword = SearchText.Trim();
        if (string.IsNullOrEmpty(keyword)) return;

        IsSearching = true;
        SearchStatus = "搜索中…";
        SearchResults.Clear();

        try
        {
            try
            {
                // ★ 全量基金代码表（数 MB）会话内只下载解析一次，后续搜索纯内存过滤
                var table = await GetFundCodeTableAsync();
                if (table is not null)
                {
                    int count = 0;
                    foreach (var (code, pinyin, name) in table)
                    {
                        if (code.Contains(keyword) || name.Contains(keyword)
                            || pinyin.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            // 去重：同一 code 不重复添加到搜索结果
                            if (SearchResults.All(r => r.Code != code))
                                SearchResults.Add(new SearchResultItem { Code = code, Name = name });
                            if (++count >= 8) break;
                        }
                    }
                    SearchStatus = count > 0 ? $"找到 {count} 条，选中后点「添加」" : "未找到相关基金";
                    return;
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            await SearchByCodeAsync(keyword);
        }
        finally { IsSearching = false; }
    }

    // ── 基金代码表会话级缓存 ──
    private static List<(string Code, string Pinyin, string Name)>? _fundCodeTable;
    private static Task<List<(string, string, string)>?>? _fundCodeTableTask;

    private static Task<List<(string, string, string)>?> GetFundCodeTableAsync()
    {
        if (_fundCodeTable is not null) return Task.FromResult<List<(string, string, string)>?>(_fundCodeTable);
        // 失败的任务不复用，下次搜索重新发起
        if (_fundCodeTableTask is { IsCompleted: false } running) return running;
        return _fundCodeTableTask = DownloadFundCodeTableAsync();
    }

    private static async Task<List<(string, string, string)>?> DownloadFundCodeTableAsync()
    {
        string url = FundApi.CodeTable();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string raw = await _http.GetStringAsync(url, cts.Token);
        var match = Regex.Match(raw, @"var r = (\[.+\])");
        if (!match.Success) return null;

        var list = new List<(string, string, string)>();
        using var doc = JsonDocument.Parse(match.Groups[1].Value);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add((item[0].GetString() ?? "",
                      item[1].GetString() ?? "",
                      item[2].GetString() ?? ""));
        }
        _fundCodeTable = list;
        return list;
    }

    private async Task SearchByCodeAsync(string code)
    {
        if (!Regex.IsMatch(code, @"^\d{6}$"))
        {
            SearchStatus = "未找到（可直接输入6位基金代码重试）";
            return;
        }
        try
        {
            string url = FundApi.Estimate(code);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string raw = await _http.GetStringAsync(url, cts.Token);
            var m = Regex.Match(raw, @"jsonpgz\((.+)\)");
            if (m.Success)
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                string name = doc.RootElement.TryGet("name") ?? code;
                if (SearchResults.All(r => r.Code != code))
                    SearchResults.Add(new SearchResultItem { Code = code, Name = name });
                SearchStatus = "找到 1 条，选中后点「添加」";
            }
            else SearchStatus = "未找到该基金代码";
        }
        catch { SearchStatus = "搜索失败，请检查网络"; }
    }

    [RelayCommand]
    private async Task AddFund(SearchResultItem? item)
    {
        if (item is null) return;
        await EnsureWatchlistLoadedAsync();   // 防止码表未加载完就写入导致覆盖丢数据
        if (_watchCodes.Contains(item.Code))
        {
            SearchStatus = $"{item.Code} 已在自选列表中";
            return;
        }
        _watchCodes.Add(item.Code);
        SaveWatchlist();
        StatusText = $"正在加载 {item.Name}…";
        var fund = await FetchFundAsync(item.Code, CancellationToken.None);
        if (Funds.All(f => f.Code != fund.Code))
            Funds.Add(fund);
        StatusText = $"已添加 {item.Name}，更新于 {DateTime.Now:HH:mm:ss}";
        SearchStatus = $"{item.Code} 已添加到自选";
    }

    [RelayCommand]
    private void OpenChart(FundItemViewModel? item)
    {
        if (item is null) return;
        WeakReferenceMessenger.Default.Send(new NavigateToFundChartMessage(item.Code, item.Name));
    }

    [RelayCommand]
    private void RemoveFund(FundItemViewModel? item)
    {
        if (item is null) return;
        _watchCodes.Remove(item.Code);
        Funds.Remove(item);
        SaveWatchlist();
        // 同步更新发现列表中该基金的 IsAdded 状态
        foreach (var d in DiscoverFunds)
            if (d.Code == item.Code) d.IsAdded = false;
        StatusText = $"已从自选移除 {item.Name}";
    }

    private void SaveWatchlist()
    {
        // 逗号拼接存库（fire-and-forget，与主题/头像持久化同模式）
        var value = string.Join(",", _watchCodes);
        if (_localData is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _localData.SaveSettingAsync(WatchlistKey, value);
            }
            catch { }
        });
    }

    /// <summary>从数据库加载自选码表（只执行一次，后续调用等待同一任务）</summary>
    private Task EnsureWatchlistLoadedAsync()
        => _watchlistLoadTask ??= LoadWatchlistAsync();

    private async Task LoadWatchlistAsync()
    {
        try
        {
            if (_localData is null) return;

            var saved = await _localData.LoadSettingAsync(WatchlistKey);

            // 一次性迁移：库里没有时尝试读旧版 JSON 文件（安装目录），迁入后不再读文件
            if (saved is null)
            {
                var legacy = TryLoadLegacyFile();
                if (legacy.Count > 0)
                {
                    saved = string.Join(",", legacy);
                    try { await _localData.SaveSettingAsync(WatchlistKey, saved); } catch { }
                }
            }
            if (string.IsNullOrEmpty(saved)) return;

            foreach (var code in saved.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (!_watchCodes.Contains(code))
                    _watchCodes.Add(code);
        }
        catch { }
    }

    private static List<string> TryLoadLegacyFile()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "fund_watchlist.json");
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new();
        }
        catch { return new(); }
    }

    private async Task<FundItemViewModel> FetchFundAsync(string code, CancellationToken ct)
    {
        try
        {
            string url = FundApi.Estimate(code);
            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(TimeSpan.FromSeconds(10));
            string raw = await _http.GetStringAsync(url, reqCts.Token);
            var m = Regex.Match(raw, @"jsonpgz\((.+)\)");
            if (m.Success)
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                var root = doc.RootElement;
                return new FundItemViewModel
                {
                    Code = code,
                    Name = root.TryGet("name") ?? code,
                    LastNav = root.TryGet("dwjz") ?? "--",
                    EstNav = root.TryGet("gsz") ?? "--",
                    ChangeRaw = root.TryGet("gszzl") ?? "0",
                    UpdatedAt = (root.TryGet("gztime") ?? "--").Length >= 5
                                    ? root.TryGet("gztime")![..5]
                                    : root.TryGet("gztime") ?? "--",
                    Source = "天天基金",
                    IsMock = false,
                };
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch { }

        try
        {
            string url = FundApi.Quote(code);
            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(TimeSpan.FromSeconds(10));
            string raw = await _http.GetStringAsync(url, reqCts.Token);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("diff", out var diff) &&
                diff.ValueKind == JsonValueKind.Array &&
                diff.GetArrayLength() > 0)
            {
                var first = diff[0];
                string nav = first.TryGet("f2") ?? "--";
                return new FundItemViewModel
                {
                    Code = code,
                    Name = first.TryGet("f14") ?? code,
                    LastNav = nav,
                    EstNav = nav,
                    ChangeRaw = first.TryGet("f3") ?? "0",
                    UpdatedAt = DateTime.Now.ToString("HH:mm"),
                    Source = "东方财富",
                    IsMock = false,
                };
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch { }

        return FundItemViewModel.Mock(code);
    }
}
