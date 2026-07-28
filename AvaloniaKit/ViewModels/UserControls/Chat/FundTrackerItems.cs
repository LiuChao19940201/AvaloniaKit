using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

/// <summary>发现 Tab 分类标签</summary>
public partial class DiscoverCategory : ObservableObject
{
    public string Label { get; set; } = "";
    public string FundType { get; set; } = "";
    public int Index { get; set; } = 0;
    /// <summary>Material 风格 24×24 矢量图标 Path 数据（与全局图标风格统一）</summary>
    public string Icon { get; set; } = "";
    [ObservableProperty] private bool _isSelected = false;
}

/// <summary>发现 Tab 基金卡片</summary>
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

/// <summary>基金搜索结果条目</summary>
public partial class SearchResultItem : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isSelected = false;
    public string Display => $"{Code}  {Name}";
}

/// <summary>自选基金条目</summary>
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

    /// <summary>离线兜底数据（网络请求全部失败时使用）</summary>
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
