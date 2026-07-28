using AvaloniaKit.Api;
using AvaloniaKit.Extensions;
using AvaloniaKit.Messages;
using AvaloniaKit.Models;
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
//  FundChartViewModel  （增强版）
//  新增只读派生属性，不改变任何数据源逻辑：
//  · LatestNavText    — 最新净值（大字显示）
//  · ChangeBadgeBg    — 涨跌徽章背景色
//  · ChangeDateRange  — 区间日期文字（分离自 ChangeText）
//  · StatHigh/Low/Avg/Days — 区间统计数据
//  · RecentRows       — 最近 5 条净值（含日涨跌）
//  · HasRecentRows    — 控制最近净值表格可见性
// ══════════════════════════════════════════════════════════════════════════════
public partial class FundChartViewModel : PageViewModelBase, ISubPageViewModel
{
    public override string Title => "净值走势";
    public override bool ShowTabBar => false;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static FundChartViewModel()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Referer", "https://fundf10.eastmoney.com/");
    }

    // ── 基金信息（由导航消息传入）────────────────────────────────────────────
    [ObservableProperty] private string _fundCode = "";
    [ObservableProperty] private string _fundName = "";
    [ObservableProperty] private string _pageTitle = "";

    // ── UI 状态 ───────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _statusText = "";

    // ── 区间选择（0 = 当月，1 = 近三个月）────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonthSelected))]
    [NotifyPropertyChangedFor(nameof(Is3MonthSelected))]
    private int _selectedRange = 0;

    public bool IsMonthSelected => SelectedRange == 0;
    public bool Is3MonthSelected => SelectedRange == 1;

    // ── 折线数据点（暴露给 View 的 Canvas 绘图）──────────────────────────────
    public ObservableCollection<NavPoint> NavPoints { get; } = new();

    // ── 原有涨跌属性 ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _lineColor = "#4080FF";
    [ObservableProperty] private string _changeText = "--";   // 保持不变，兼容旧代码
    [ObservableProperty] private bool _isUp = true;

    // Y 轴范围（供 Canvas 绘图归一化用）
    [ObservableProperty] private double _yMin = 0;
    [ObservableProperty] private double _yMax = 1;

    // ── 新增：大净值显示 ──────────────────────────────────────────────────────
    [ObservableProperty] private string _latestNavText = "--";
    [ObservableProperty] private string _changeBadgeBg = "#C0392B";
    [ObservableProperty] private string _changeDateRange = "";

    // ── 新增：统计卡片 ────────────────────────────────────────────────────────
    [ObservableProperty] private string _statHigh = "--";
    [ObservableProperty] private string _statLow = "--";
    [ObservableProperty] private string _statAvg = "--";
    [ObservableProperty] private string _statDays = "--";

    // ── 新增：最近净值列表 ────────────────────────────────────────────────────
    public ObservableCollection<RecentNavRow> RecentRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentRows))]
    private int _recentRowCount = 0;
    public bool HasRecentRows => RecentRowCount > 0;

    private CancellationTokenSource? _cts;

    // ── 由 MainWindowViewModel.Receive 调用，传入基金信息 ─────────────────────
    public void OnNavigatedTo(string code, string name)
    {
        FundCode = code;
        FundName = name;
        PageTitle = $"{name}（{code}）";
        SelectedRange = 0;
        _ = LoadDataAsync(0);
    }

    // ── 导航：返回基金列表 ────────────────────────────────────────────────────
    [RelayCommand]
    private void GoBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackFromFundChartMessage());

    // ── 区间切换 ──────────────────────────────────────────────────────────────
    [RelayCommand]
    private void SelectMonth()
    {
        if (SelectedRange == 0) return;
        SelectedRange = 0;
        _ = LoadDataAsync(0);
    }

    [RelayCommand]
    private void Select3Month()
    {
        if (SelectedRange == 1) return;
        SelectedRange = 1;
        _ = LoadDataAsync(1);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  历史净值加载（数据源完全不变，仅在加载完毕后派生新属性）
    // ══════════════════════════════════════════════════════════════════════════
    private async Task LoadDataAsync(int range)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsLoading = true;
        HasError = false;
        NavPoints.Clear();
        RecentRows.Clear();
        RecentRowCount = 0;
        LatestNavText = "--";
        ChangeBadgeBg = "#AAAAAA";
        ChangeDateRange = "";
        StatHigh = StatLow = StatAvg = StatDays = "--";
        StatusText = "数据加载中…";

        try
        {
            DateTime endDate = DateTime.Today;
            DateTime startDate = range == 0
                ? new DateTime(endDate.Year, endDate.Month, 1)
                : endDate.AddMonths(-3);

            // FundApi：Web 端自动套 CORS 代理，三端真实数据一致
            string url = FundApi.NavHistory(FundCode, startDate, endDate);

            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(TimeSpan.FromSeconds(12));

            string raw = await _http.GetStringAsync(url, reqCts.Token);

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Data", out var data) ||
                !data.TryGetProperty("LSJZList", out var list) ||
                list.ValueKind != JsonValueKind.Array ||
                list.GetArrayLength() == 0)
            {
                SetError("暂无历史净值数据");
                return;
            }

            // ── 解析并排序（原逻辑不变）──────────────────────────────────
            var points = new List<NavPoint>();
            foreach (var item in list.EnumerateArray())
            {
                string dateStr = item.TryGetStr("FSRQ") ?? "";
                string navStr = item.TryGetStr("DWJZ") ?? "";
                if (DateTime.TryParse(dateStr, out DateTime dt) &&
                    double.TryParse(navStr, out double nav))
                {
                    points.Add(new NavPoint { Date = dt, Nav = nav });
                }
            }
            points.Sort((a, b) => a.Date.CompareTo(b.Date));

            if (points.Count == 0)
            {
                SetError("暂无可用数据");
                return;
            }

            // ── Y 轴范围（原逻辑不变）──────────────────────────────────
            double minNav = double.MaxValue, maxNav = double.MinValue;
            foreach (var p in points)
            {
                if (p.Nav < minNav) minNav = p.Nav;
                if (p.Nav > maxNav) maxNav = p.Nav;
            }
            double padding = (maxNav - minNav) * 0.1;
            if (padding < 0.001) padding = 0.001;
            YMin = Math.Round(minNav - padding, 4);
            YMax = Math.Round(maxNav + padding, 4);

            // ── 涨跌判断（原逻辑不变）──────────────────────────────────
            bool up = points[^1].Nav >= points[0].Nav;
            IsUp = up;
            LineColor = up ? "#C0392B" : "#18B06A";
            double chg = points.Count >= 2
                ? (points[^1].Nav - points[0].Nav) / points[0].Nav * 100
                : 0;
            // 保留旧 ChangeText（兼容旧绑定）
            ChangeText = $"{(up ? "+" : "")}{chg:F2}%  {points[0].Date:MM/dd} → {points[^1].Date:MM/dd}";

            // ── 新增派生属性（纯展示，不修改 points 数据）────────────────

            // 大净值
            LatestNavText = points[^1].Nav.ToString("F4");

            // 徽章颜色 + 日期范围（拆分自旧 ChangeText）
            ChangeBadgeBg = up ? "#C0392B" : "#18B06A";
            ChangeText = $"{(up ? "+" : "")}{chg:F2}%";          // 只保留百分比
            ChangeDateRange = $"{points[0].Date:MM/dd} → {points[^1].Date:MM/dd}";

            // 统计卡片
            double sum = 0;
            foreach (var p in points) sum += p.Nav;
            StatHigh = maxNav.ToString("F4");
            StatLow = minNav.ToString("F4");
            StatAvg = (sum / points.Count).ToString("F4");
            StatDays = $"{points.Count} 天";

            // 最近 5 条净值（含日涨跌）
            int recentCount = Math.Min(5, points.Count);
            for (int i = points.Count - 1; i >= points.Count - recentCount; i--)
            {
                double dayChg = (i > 0)
                    ? (points[i].Nav - points[i - 1].Nav) / points[i - 1].Nav * 100
                    : 0;
                bool dayUp = dayChg >= 0;
                RecentRows.Add(new RecentNavRow
                {
                    DateStr = points[i].Date.ToString("MM-dd"),
                    NavStr = points[i].Nav.ToString("F4"),
                    DayChangeStr = i > 0
                        ? $"{(dayUp ? "+" : "")}{dayChg:F2}%"
                        : "--",
                    DayChangeColor = i > 0
                        ? (dayUp ? "#C0392B" : "#18B06A")
                        : "#AAAAAA"
                });
            }
            RecentRowCount = RecentRows.Count;

            // ── 填充 NavPoints（原逻辑不变）─────────────────────────────
            foreach (var p in points)
                NavPoints.Add(p);

            StatusText = $"共 {points.Count} 个交易日  最新净值 {points[^1].Nav:F4}";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            SetError("请求超时，请检查网络");
        }
        catch (OperationCanceledException) { /* 被新请求取消，静默退出 */ }
        catch (Exception ex)
        {
            SetError($"加载失败：{ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetError(string msg)
    {
        HasError = true;
        ErrorText = msg;
    }
}

// ── 最近净值行 ───────────────────────────────────────────────────────────────
public class RecentNavRow
{
    public string DateStr { get; set; } = "";
    public string NavStr { get; set; } = "";
    public string DayChangeStr { get; set; } = "";
    public string DayChangeColor { get; set; } = "#AAAAAA";
}


