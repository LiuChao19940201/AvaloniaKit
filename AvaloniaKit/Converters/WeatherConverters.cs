using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AvaloniaKit.Converters;

// ══════════════════════════════════════════════════════════════════════
//  将和风天气图标编号转为对应 Emoji（无需网络，纯本地映射）
// ══════════════════════════════════════════════════════════════════════
public class WeatherIconToEmojiConverter : IValueConverter
{
    public static readonly WeatherIconToEmojiConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string icon) return "🌤";
        return icon switch
        {
            "100" or "150" => "☀️",
            "101"          => "🌤",
            "102"          => "⛅",
            "103"          => "🌥",
            "104"          => "☁️",
            "300"          => "🌦",
            "301"          => "🌧",
            "302"          => "⛈",
            "303"          => "🌩",
            "304"          => "🌨",
            "305"          => "🌂",
            "306"          => "🌧",
            "307"          => "🌧",
            "308"          => "⛈",
            "309"          => "🌫",
            "310"          => "🌧",
            "311"          => "🌧",
            "312"          => "🌧",
            "313"          => "🌨",
            "314"          => "🌧",
            "315"          => "🌧",
            "316"          => "🌧",
            "317"          => "⛈",
            "318"          => "⛈",
            "399"          => "🌧",
            "400"          => "❄️",
            "401"          => "🌨",
            "402"          => "🌨",
            "403"          => "🌨",
            "404"          => "🌨",
            "405"          => "🌨",
            "406"          => "🌨",
            "407"          => "🌨",
            "408"          => "🌨",
            "409"          => "🌨",
            "410"          => "🌨",
            "499"          => "❄️",
            "500"          => "🌫",
            "501"          => "🌫",
            "502"          => "🌫",
            "503"          => "😷",   // 扬沙
            "504"          => "😷",   // 浮尘
            "507"          => "😷",   // 沙尘暴
            "508"          => "😷",   // 强沙尘暴
            "509"          => "😷",   // 霾
            "510"          => "😷",
            "511"          => "😷",
            "512"          => "😷",
            "513"          => "😷",
            "514"          => "😷",
            "515"          => "😷",
            "900"          => "🔥",   // 热
            "901"          => "🥶",   // 冷
            "999"          => "🌡️",
            _              => "🌤",
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ══════════════════════════════════════════════════════════════════════
//  将颜色十六进制字符串转为 IBrush（用于动态绑定背景色）
// ══════════════════════════════════════════════════════════════════════
public class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorStr && !string.IsNullOrEmpty(colorStr))
        {
            try { return SolidColorBrush.Parse(colorStr); }
            catch { }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ══════════════════════════════════════════════════════════════════════
//  将颜色十六进制字符串转为 Color（用于 GradientStop）
// ══════════════════════════════════════════════════════════════════════
public class StringToColorConverter : IValueConverter
{
    public static readonly StringToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorStr && !string.IsNullOrEmpty(colorStr))
        {
            try { return Color.Parse(colorStr); }
            catch { }
        }
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ══════════════════════════════════════════════════════════════════════
//  字符串相等转 bool（用于场景 Panel 显示判断）
// ══════════════════════════════════════════════════════════════════════
public class StringEqualConverter : IValueConverter
{
    public static readonly StringEqualConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ══════════════════════════════════════════════════════════════════════
//  非零可见（降水概率为0时隐藏）
// ══════════════════════════════════════════════════════════════════════
public class NonZeroVisConverter : IValueConverter
{
    public static readonly NonZeroVisConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && int.TryParse(s, out int v))
            return v > 0;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
