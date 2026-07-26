using AvaloniaKit.ViewModels.Messages;
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
//  WeatherViewModel — 天气预报（实时 + 24小时逐时 + 7日预报 + 城市切换）
//  · 数据源：Open-Meteo（免费无Key、HTTPS、自带 CORS:*）→ 三端同一条链路直连，
//    替换掉原来的 d1.weather.com.cn（明文 http + 无 CORS + 只有武汉实时数据）
//  · 天气现象采用 WMO 标准编码，本地映射为中文描述/Emoji/动效场景
//  · 动态效果（晴/雨/雪/云/雷）由 View 层 code-behind 根据 WeatherKind 驱动
// ══════════════════════════════════════════════════════════════════════════════
public partial class WeatherViewModel : ObservableObject
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ── 城市列表（名称 + 经纬度，默认武汉）─────────────────────────────────
    public ObservableCollection<WeatherCity> Cities { get; } = new()
    {
        new("武汉", 30.5928, 114.3055), new("北京", 39.9042, 116.4074),
        new("上海", 31.2304, 121.4737), new("广州", 23.1291, 113.2644),
        new("深圳", 22.5431, 114.0579), new("成都", 30.5728, 104.0668),
        new("杭州", 30.2741, 120.1551), new("重庆", 29.5630, 106.5516),
        new("西安", 34.3416, 108.9398), new("南京", 32.0603, 118.7969),
        new("天津", 39.3434, 117.3616), new("苏州", 31.2989, 120.5853),
        new("郑州", 34.7466, 113.6254), new("长沙", 28.2282, 112.9388),
        new("青岛", 36.0671, 120.3826), new("沈阳", 41.8057, 123.4315),
        new("大连", 38.9140, 121.6147), new("厦门", 24.4798, 118.0894),
        new("昆明", 24.8801, 102.8329), new("哈尔滨", 45.8038, 126.5349),
        new("乌鲁木齐", 43.8256, 87.6168), new("拉萨", 29.6520, 91.1721),
        new("三亚", 18.2528, 109.5119), new("香港", 22.3193, 114.1694),
    };

    // ── 当前状态 ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _cityName = "武汉";
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _updateTime = "";

    // 实时天气
    [ObservableProperty] private string _currentTemp = "--";
    [ObservableProperty] private string _weatherDesc = "加载中…";
    [ObservableProperty] private string _weatherEmoji = "🌤";
    [ObservableProperty] private string _feelsLike = "";
    [ObservableProperty] private string _humidity = "";
    [ObservableProperty] private string _windInfo = "";
    [ObservableProperty] private string _todayRange = "";

    // ── 动效场景：Sunny / Cloudy / Overcast / Fog / Rain / Snow / Thunder / Wind ──
    [ObservableProperty] private string _weatherKind = "Sunny";

    // ★ 夜间标志：晴夜切换为月亮星空场景，背景同步变深
    [ObservableProperty] private bool _isNight = false;

    // 背景渐变（View 用 StringToColorConverter 绑定 GradientStop）
    [ObservableProperty] private string _bgTopColor = "#4A90D9";
    [ObservableProperty] private string _bgBottomColor = "#87CEEB";

    // 逐时 / 逐日预报
    public ObservableCollection<WeatherHourItem> HourlyItems { get; } = new();
    public ObservableCollection<WeatherDayItem> DailyItems { get; } = new();

    // 城市选择面板
    [ObservableProperty] private bool _isCityPanelOpen = false;

    private WeatherCity _city;
    private DateTime _lastLoaded = DateTime.MinValue;
    private CancellationTokenSource? _loadCts;

    public WeatherViewModel()
    {
        _city = Cities[0];
        _ = LoadWeatherAsync();
    }

    /// <summary>导航进入时调用：数据超过 10 分钟自动刷新</summary>
    public void OnNavigatedTo()
    {
        if (DateTime.UtcNow - _lastLoaded > TimeSpan.FromMinutes(10))
            _ = LoadWeatherAsync();
    }

    // ── 命令 ─────────────────────────────────────────────────────────────────
    [RelayCommand] private Task Refresh() => LoadWeatherAsync();

    [RelayCommand] private void ToggleCityPanel() => IsCityPanelOpen = !IsCityPanelOpen;

    [RelayCommand]
    private void SelectCity(WeatherCity? city)
    {
        IsCityPanelOpen = false;
        if (city is null || city == _city) return;
        _city = city;
        CityName = city.Name;
        _ = LoadWeatherAsync();
    }

    [RelayCommand]
    private void GoBack()
        => WeakReferenceMessenger.Default.Send(new NavigateBackFromWeatherMessage());

    // ══════════════════════════════════════════════════════════════════════════
    //  加载天气（Open-Meteo：current + hourly + daily 一次拉全）
    // ══════════════════════════════════════════════════════════════════════════
    private async Task LoadWeatherAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        StatusText = "";
        try
        {
            string url =
                "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={_city.Lat}&longitude={_city.Lon}" +
                "&current=temperature_2m,relative_humidity_2m,apparent_temperature," +
                "weather_code,wind_speed_10m,wind_direction_10m" +
                "&hourly=temperature_2m,weather_code" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min," +
                "precipitation_probability_max" +
                "&timezone=Asia%2FShanghai&forecast_days=7";

            string raw = await _http.GetStringAsync(url, ct);
            if (ct.IsCancellationRequested) return;

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            ParseCurrent(root);
            ParseHourly(root);
            ParseDaily(root);

            UpdateTime = $"更新于 {DateTime.Now:HH:mm}";
            _lastLoaded = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { }
        catch
        {
            StatusText = "天气加载失败，请检查网络后下拉刷新";
            WeatherDesc = "加载失败";
        }
        finally { IsLoading = false; }
    }

    private void ParseCurrent(JsonElement root)
    {
        if (!root.TryGetProperty("current", out var cur)) return;

        double temp = GetNum(cur, "temperature_2m");
        double feels = GetNum(cur, "apparent_temperature");
        double hum = GetNum(cur, "relative_humidity_2m");
        double wind = GetNum(cur, "wind_speed_10m");
        double windDir = GetNum(cur, "wind_direction_10m");
        int code = (int)GetNum(cur, "weather_code");

        CurrentTemp = $"{Math.Round(temp)}";
        FeelsLike = $"体感 {Math.Round(feels)}°";
        Humidity = $"湿度 {Math.Round(hum)}%";
        WindInfo = $"{WindDirText(windDir)} {WindLevel(wind)}级";

        var (desc, emoji, kind) = MapWmo(code);
        WeatherDesc = desc;
        WeatherEmoji = emoji;

        // ★ 风力达 6 级以上时优先展示「刮风」动效场景
        bool isNight = DateTime.Now.Hour < 6 || DateTime.Now.Hour >= 19;
        IsNight = isNight;
        WeatherKind = WindLevel(wind) >= 6 && kind is "Sunny" or "Cloudy" or "Overcast"
            ? "Wind" : kind;
        ApplyBackground(WeatherKind, isNight);
    }

    private void ParseHourly(JsonElement root)
    {
        HourlyItems.Clear();
        if (!root.TryGetProperty("hourly", out var hourly)) return;
        if (!hourly.TryGetProperty("time", out var times) ||
            !hourly.TryGetProperty("temperature_2m", out var temps) ||
            !hourly.TryGetProperty("weather_code", out var codes)) return;

        var now = DateTime.Now;
        int count = Math.Min(times.GetArrayLength(),
                    Math.Min(temps.GetArrayLength(), codes.GetArrayLength()));
        int added = 0;

        for (int i = 0; i < count && added < 24; i++)
        {
            if (!DateTime.TryParse(times[i].GetString(), out var t)) continue;
            if (t < now.AddHours(-1)) continue;   // 从当前小时开始

            var (_, emoji, _) = MapWmo((int)GetArrNum(codes, i));
            HourlyItems.Add(new WeatherHourItem
            {
                TimeText = added == 0 ? "现在" : $"{t.Hour}时",
                Emoji = emoji,
                TempText = $"{Math.Round(GetArrNum(temps, i))}°",
            });
            added++;
        }
    }

    private void ParseDaily(JsonElement root)
    {
        DailyItems.Clear();
        if (!root.TryGetProperty("daily", out var daily)) return;
        if (!daily.TryGetProperty("time", out var times) ||
            !daily.TryGetProperty("weather_code", out var codes) ||
            !daily.TryGetProperty("temperature_2m_max", out var maxs) ||
            !daily.TryGetProperty("temperature_2m_min", out var mins)) return;

        daily.TryGetProperty("precipitation_probability_max", out var rains);

        int count = times.GetArrayLength();
        for (int i = 0; i < count; i++)
        {
            if (!DateTime.TryParse(times[i].GetString(), out var d)) continue;
            var (desc, emoji, _) = MapWmo((int)GetArrNum(codes, i));

            double min = Math.Round(GetArrNum(mins, i));
            double max = Math.Round(GetArrNum(maxs, i));
            int rain = rains.ValueKind == JsonValueKind.Array
                ? (int)GetArrNum(rains, i) : 0;

            DailyItems.Add(new WeatherDayItem
            {
                DayText = i == 0 ? "今天" : i == 1 ? "明天" : DayOfWeekCn(d.DayOfWeek),
                DateText = $"{d.Month}/{d.Day}",
                Emoji = emoji,
                Desc = desc,
                RainProb = rain >= 20 ? $"💧{rain}%" : "",
                TempRange = $"{min}° ~ {max}°",
            });

            // 首日同步到顶部「今日温度区间」
            if (i == 0) TodayRange = $"最高 {max}°  最低 {min}°";
        }
    }

    // ── WMO 天气编码 → (中文描述, Emoji, 动效场景) ──────────────────────────
    private static (string desc, string emoji, string kind) MapWmo(int code) => code switch
    {
        0           => ("晴",       "☀️", "Sunny"),
        1           => ("晴间多云", "🌤", "Sunny"),
        2           => ("多云",     "⛅", "Cloudy"),
        3           => ("阴",       "☁️", "Overcast"),
        45 or 48    => ("雾",       "🌫", "Fog"),
        51 or 53 or 55 => ("毛毛雨", "🌦", "Rain"),
        56 or 57    => ("冻毛毛雨", "🌧", "Rain"),
        61          => ("小雨",     "🌧", "Rain"),
        63          => ("中雨",     "🌧", "Rain"),
        65          => ("大雨",     "🌧", "Rain"),
        66 or 67    => ("冻雨",     "🌧", "Rain"),
        71          => ("小雪",     "🌨", "Snow"),
        73          => ("中雪",     "🌨", "Snow"),
        75          => ("大雪",     "❄️", "Snow"),
        77          => ("雪粒",     "🌨", "Snow"),
        80 or 81    => ("阵雨",     "🌦", "Rain"),
        82          => ("强阵雨",   "⛈", "Rain"),
        85 or 86    => ("阵雪",     "🌨", "Snow"),
        95          => ("雷阵雨",   "⛈", "Thunder"),
        96 or 99    => ("雷雨冰雹", "⛈", "Thunder"),
        _           => ("未知",     "🌡", "Cloudy"),
    };

    // ── 背景渐变随天气 + 昼夜变化 ────────────────────────────────────────────
    private void ApplyBackground(string kind, bool night)
    {
        (BgTopColor, BgBottomColor) = (kind, night) switch
        {
            ("Sunny", false)    => ("#2F80CE", "#6FB6F5"),
            ("Sunny", true)     => ("#0F2350", "#2C4A7C"),
            ("Cloudy", false)   => ("#4A7098", "#8FB0CB"),
            ("Cloudy", true)    => ("#1B2A44", "#3A5573"),
            ("Overcast", false) => ("#5A6B7D", "#8D9DAC"),
            ("Overcast", true)  => ("#232D3A", "#42566B"),
            ("Fog", false)      => ("#6E7F8D", "#A6B4BF"),
            ("Fog", true)       => ("#2A3540", "#4D5E6C"),
            ("Rain", false)     => ("#3A4A5C", "#61788D"),
            ("Rain", true)      => ("#141E2B", "#31465C"),
            ("Thunder", _)      => ("#1E2633", "#3D4A63"),
            ("Snow", false)     => ("#6C86A3", "#AFC4D8"),
            ("Snow", true)      => ("#26364A", "#50677F"),
            ("Wind", false)     => ("#3D7A8C", "#7FB5C4"),
            ("Wind", true)      => ("#15303B", "#39616F"),
            _                   => ("#4A90D9", "#87CEEB"),
        };
    }

    // ── 工具 ─────────────────────────────────────────────────────────────────
    private static double GetNum(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;

    private static double GetArrNum(JsonElement arr, int i)
        => i < arr.GetArrayLength() && arr[i].ValueKind == JsonValueKind.Number
            ? arr[i].GetDouble() : 0;

    /// <summary>风速(km/h) → 蒲福风级</summary>
    private static int WindLevel(double kmh) => kmh switch
    {
        < 1 => 0, < 6 => 1, < 12 => 2, < 20 => 3, < 29 => 4,
        < 39 => 5, < 50 => 6, < 62 => 7, < 75 => 8, < 89 => 9,
        < 103 => 10, < 118 => 11, _ => 12,
    };

    private static string WindDirText(double deg) => ((deg % 360 + 360) % 360) switch
    {
        >= 337.5 or < 22.5 => "北风",
        >= 22.5 and < 67.5 => "东北风",
        >= 67.5 and < 112.5 => "东风",
        >= 112.5 and < 157.5 => "东南风",
        >= 157.5 and < 202.5 => "南风",
        >= 202.5 and < 247.5 => "西南风",
        >= 247.5 and < 292.5 => "西风",
        _ => "西北风",
    };

    private static string DayOfWeekCn(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "周一", DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三", DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五", DayOfWeek.Saturday => "周六",
        _ => "周日",
    };
}

// ── 数据模型 ──────────────────────────────────────────────────────────────────
public record WeatherCity(string Name, double Lat, double Lon);

public class WeatherHourItem
{
    public string TimeText { get; init; } = "";
    public string Emoji { get; init; } = "";
    public string TempText { get; init; } = "";
}

public class WeatherDayItem
{
    public string DayText { get; init; } = "";
    public string DateText { get; init; } = "";
    public string Emoji { get; init; } = "";
    public string Desc { get; init; } = "";
    public string RainProb { get; init; } = "";
    public string TempRange { get; init; } = "";
}
