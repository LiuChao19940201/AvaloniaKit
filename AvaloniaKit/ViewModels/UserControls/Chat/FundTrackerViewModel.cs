using AvaloniaKit.Messages;
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
//  新增：
//  · ActiveTab（0=发现  1=自选）
//  · 发现 Tab：热门分类标签 + 分类基金排行榜
//  · DiscoverFundItem：可「+」一键添加到自选，可点击跳转图表
//  原有自选、搜索、净值刷新逻辑完全不变
// ══════════════════════════════════════════════════════════════════════════════
public partial class FundTrackerViewModel : ObservableObject
{
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

    // ── 持久化路径 ────────────────────────────────────────────────────────────
    private static string? GetSaveFilePath()
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "fund_watchlist.json");
        }
        catch { return null; }
    }

    private readonly ObservableCollection<string> _watchCodes = new();

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
    [RelayCommand] private void SwitchToWatchlist() { ActiveTab = 1; _ = DoRefreshAsync(); }

    // ════════════════════════════════════════════════════════════════════════
    //  发现 Tab —— 热门分类
    // ════════════════════════════════════════════════════════════════════════
    public ObservableCollection<DiscoverCategory> DiscoverCategories { get; } = new()
    {
        new DiscoverCategory { Label = "🔥 热门",   FundType = "hot",   Index = 0, IsSelected = true  },
        new DiscoverCategory { Label = "📈 股票型", FundType = "stock", Index = 1, IsSelected = false },
        new DiscoverCategory { Label = "📊 指数型", FundType = "index", Index = 2, IsSelected = false },
        new DiscoverCategory { Label = "🌍 QDII",   FundType = "qdii",  Index = 3, IsSelected = false },
        new DiscoverCategory { Label = "💵 债券型", FundType = "bond",  Index = 4, IsSelected = false },
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

            // 按近1月涨幅排序，取前20条
            string url = $"https://fund.eastmoney.com/data/rankhandler.aspx" +
                         $"?op=ph&dt=kf&ft={ft}&rs=&gs=0&sc=yzf&st=desc" +
                         $"&sd={DateTime.Today.AddMonths(-1):yyyy-MM-dd}" +
                         $"&ed={DateTime.Today:yyyy-MM-dd}" +
                         $"&qdii=&tabSubtype=,,,,,&pi=1&pn=20&dx=1" +
                         $"&v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            string raw = await _http.GetStringAsync(url, cts.Token);

            // 响应格式: var rankData = {datas:["code,abbr,name,...", ...], ...}
            var m = Regex.Match(raw, @"datas:\[(.+?)\]", RegexOptions.Singleline);
            if (!m.Success)
            {
                LoadDiscoverFallback();
                return;
            }

            // 拆分每条记录（逗号分隔的字符串，用引号括起）
            var seenCodes = new HashSet<string>();   // 去重
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

                DiscoverFunds.Add(new DiscoverFundItem
                {
                    Code = code,
                    Name = string.IsNullOrWhiteSpace(name) ? abbr : name,
                    NavStr = nav > 0 ? nav.ToString("F4") : "--",
                    ChangeRaw = chg,
                    IsAdded = _watchCodes.Contains(code),
                });
            }

            if (DiscoverFunds.Count == 0)
                LoadDiscoverFallback();
        }
        catch
        {
            LoadDiscoverFallback();
        }
        finally
        {
            IsDiscoverLoading = false;
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
        LoadWatchlist();
    }

    public void OnNavigatedTo()
    {
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
        Funds.Clear();

        if (_watchCodes.Count == 0)
        {
            IsLoading = false;
            StatusText = "自选列表为空，点击「+」添加基金";
            return;
        }

        int ok = 0;
        try
        {
            foreach (var code in _watchCodes.ToList())
            {
                ct.ThrowIfCancellationRequested();
                var item = await FetchFundAsync(code, ct);
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
                string url = "https://fund.eastmoney.com/js/fundcode_search.js";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                string raw = await _http.GetStringAsync(url, cts.Token);
                var match = Regex.Match(raw, @"var r = (\[.+\])");
                if (match.Success)
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value);
                    int count = 0;
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        string code = item[0].GetString() ?? "";
                        string pinyin = item[1].GetString() ?? "";
                        string name = item[2].GetString() ?? "";
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

    private async Task SearchByCodeAsync(string code)
    {
        if (!Regex.IsMatch(code, @"^\d{6}$"))
        {
            SearchStatus = "未找到（可直接输入6位基金代码重试）";
            return;
        }
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string url = $"https://fundgz.1234567.com.cn/js/{code}.js?rt={ts}";
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
        string? path = GetSaveFilePath();
        if (path is null) return;
        try { File.WriteAllText(path, JsonSerializer.Serialize(_watchCodes.ToList())); }
        catch { }
    }

    private void LoadWatchlist()
    {
        string? path = GetSaveFilePath();
        if (path is null || !File.Exists(path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            if (list is null) return;
            foreach (var code in list)
                if (!_watchCodes.Contains(code))
                    _watchCodes.Add(code);
        }
        catch { }
    }

    private async Task<FundItemViewModel> FetchFundAsync(string code, CancellationToken ct)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string url = $"https://fundgz.1234567.com.cn/js/{code}.js?rt={ts}";
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
            string url = $"https://push2.eastmoney.com/api/qt/slist/get" +
                         $"?fltt=2&fields=f2,f3,f12,f14&secid=0.{code}";
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

// ══════════════════════════════════════════════════════════════════════════════
//  发现分类 Tab 标签
// ══════════════════════════════════════════════════════════════════════════════
public partial class DiscoverCategory : ObservableObject
{
    public string Label { get; set; } = "";
    public string FundType { get; set; } = "";
    public int Index { get; set; } = 0;
    [ObservableProperty] private bool _isSelected = false;
}

// ══════════════════════════════════════════════════════════════════════════════
//  发现基金卡片
// ══════════════════════════════════════════════════════════════════════════════
public partial class DiscoverFundItem : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _navStr = "--";
    [ObservableProperty] private double _changeRaw = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddBtnText))]
    [NotifyPropertyChangedFor(nameof(AddBtnBg))]
    [NotifyPropertyChangedFor(nameof(AddBtnFg))]
    private bool _isAdded = false;

    public bool IsUp => ChangeRaw >= 0;
    public string ChangeText => (IsUp ? "+" : "") + ChangeRaw.ToString("F2") + "%";
    public string ChangeColor => IsUp ? "#C0392B" : "#18B06A";
    public string ChangeBg => IsUp ? "#1AE05C5C" : "#1A18B06A";
    public string AddBtnText => IsAdded ? "✓" : "+";
    public string AddBtnBg => IsAdded ? "#E8F5E9" : "#E8F0FE";
    public string AddBtnFg => IsAdded ? "#18B06A" : "#1565C0";
}

// ══════════════════════════════════════════════════════════════════════════════
//  原有模型（完全不变）
// ══════════════════════════════════════════════════════════════════════════════
public partial class SearchResultItem : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isSelected = false;
    public string Display => $"{Code}  {Name}";
}

public partial class FundItemViewModel : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _lastNav = "--";
    [ObservableProperty] private string _estNav = "--";
    [ObservableProperty] private string _changeRaw = "0";
    [ObservableProperty] private string _updatedAt = "--";
    [ObservableProperty] private string _source = "--";
    [ObservableProperty] private bool _isMock = false;

    public bool IsUp => double.TryParse(ChangeRaw, out double v) && v >= 0;
    public string ChangeText
    {
        get
        {
            if (!double.TryParse(ChangeRaw, out double v)) return "--";
            return (v >= 0 ? "+" : "") + v.ToString("F2") + "%";
        }
    }
    public string ChipBackground => IsUp ? "#1AE05C5C" : "#1A18B06A";
    public string ChipForeground => IsUp ? "#C0392B" : "#18B06A";

    private static readonly (string code, string name, double nav, double chg)[] _mocks =
    {
        ("000001", "华夏成长混合",    1.8423,  0.56),
        ("110022", "易方达消费行业",  3.2100, -0.32),
        ("161725", "招商中证白酒",    1.1560,  1.20),
        ("000961", "天弘沪深300ETF",  1.3210,  0.08),
        ("270042", "广发纳斯达克100", 2.6780, -0.75),
    };

    internal static FundItemViewModel Mock(string code)
    {
        foreach (var (c, n, nav, chg) in _mocks)
            if (c == code)
                return new FundItemViewModel
                {
                    Code = c,
                    Name = n,
                    LastNav = nav.ToString("F4"),
                    EstNav = "--",
                    ChangeRaw = chg.ToString("F2"),
                    UpdatedAt = "离线",
                    Source = "本地缓存",
                    IsMock = true,
                };
        return new FundItemViewModel { Code = code, Name = code, IsMock = true };
    }
}

// ── JsonElement 扩展（不变）──────────────────────────────────────────────────
internal static class JsonElementExtensions
{
    internal static string? TryGet(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString() ?? v.GetRawText()
            : null;
}
