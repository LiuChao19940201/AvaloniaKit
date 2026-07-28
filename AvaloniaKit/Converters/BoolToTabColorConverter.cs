using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AvaloniaKit.Converters;

/// <summary>
/// true  → 微信绿 #07C160（Tab 选中态）
/// false → 灰色   #888888（Tab 未选中）
/// </summary>
public class BoolToTabColorConverter : IValueConverter
{
    public static readonly BoolToTabColorConverter Instance = new();

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#07C160"));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#888888"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : InactiveBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}