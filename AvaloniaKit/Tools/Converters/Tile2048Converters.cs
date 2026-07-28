using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AvaloniaKit.Converters;

/// <summary>2048 格子数值 → 经典配色背景画刷（0 为空格底色）</summary>
public sealed class Tile2048ToBrushConverter : IValueConverter
{
    private static SolidColorBrush B(string hex) => new(Color.Parse(hex));

    private static readonly SolidColorBrush Empty = B("#3A3A3C");
    private static readonly SolidColorBrush T2 = B("#EEE4DA");
    private static readonly SolidColorBrush T4 = B("#EDE0C8");
    private static readonly SolidColorBrush T8 = B("#F2B179");
    private static readonly SolidColorBrush T16 = B("#F59563");
    private static readonly SolidColorBrush T32 = B("#F67C5F");
    private static readonly SolidColorBrush T64 = B("#F65E3B");
    private static readonly SolidColorBrush T128 = B("#EDCF72");
    private static readonly SolidColorBrush T256 = B("#EDCC61");
    private static readonly SolidColorBrush T512 = B("#EDC850");
    private static readonly SolidColorBrush T1024 = B("#EDC53F");
    private static readonly SolidColorBrush T2048 = B("#EDC22E");
    private static readonly SolidColorBrush Super = B("#3C3A32");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int v ? v switch
        {
            0 => Empty,
            2 => T2,
            4 => T4,
            8 => T8,
            16 => T16,
            32 => T32,
            64 => T64,
            128 => T128,
            256 => T256,
            512 => T512,
            1024 => T1024,
            2048 => T2048,
            _ => Super,
        } : Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>2048 格子数值 → 前景色（2/4 深字，其余白字）</summary>
public sealed class Tile2048ToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush Dark = new(Color.Parse("#776E65"));
    private static readonly SolidColorBrush Light = new(Color.Parse("#F9F6F2"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int v && v is 2 or 4 ? Dark : Light;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>2048 格子数值 → 显示文本（0 显示为空）</summary>
public sealed class Tile2048ToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int v && v > 0 ? v.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>2048 格子数值 → 字号（位数越多字越小，配合 Viewbox 自适应）</summary>
public sealed class Tile2048ToFontSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int v ? (double)(v switch
        {
            < 100 => 26,
            < 1000 => 22,
            < 10000 => 18,
            _ => 14,
        }) : 26d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
