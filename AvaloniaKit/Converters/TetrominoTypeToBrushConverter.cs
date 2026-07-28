using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using System;
using System.Globalization;
using Avalonia;

namespace AvaloniaKit.Converters;

/// <summary>
/// TetrominoType 枚举 → SolidColorBrush
/// 在 UserControl.Resources 中声明后通过 StaticResource 引用。
/// </summary>
public sealed class TetrominoTypeToBrushConverter : IValueConverter
{
    // 经典俄罗斯方块配色（与 Tetris Guideline 一致）
    private static readonly SolidColorBrush I = Brush("#00BCD4"); // 青
    private static readonly SolidColorBrush O = Brush("#FFD600"); // 黄
    private static readonly SolidColorBrush T = Brush("#AB47BC"); // 紫
    private static readonly SolidColorBrush S = Brush("#4CAF50"); // 绿
    private static readonly SolidColorBrush Z = Brush("#F44336"); // 红
    private static readonly SolidColorBrush J = Brush("#2196F3"); // 蓝
    private static readonly SolidColorBrush L = Brush("#FF7043"); // 橙

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    public object Convert(object? value, Type targetType,
                          object? parameter, CultureInfo culture)
        => value is TetrominoType t ? t switch
        {
            TetrominoType.I => I,
            TetrominoType.O => O,
            TetrominoType.T => T,
            TetrominoType.S => S,
            TetrominoType.Z => Z,
            TetrominoType.J => J,
            TetrominoType.L => L,
            TetrominoType.Ghost => GetGhostBrush(),
            _ => GetEmptyBrush(),
        } : GetEmptyBrush();

    public object ConvertBack(object? value, Type targetType,
                              object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    // 根据当前主题动态返回幽灵块画刷（暗色主题使用白色半透明，亮色主题使用黑色半透明）
    private static SolidColorBrush GetGhostBrush()
    {
        try
        {
            var app = Application.Current;
            var theme = app?.ActualThemeVariant ?? ThemeVariant.Default;

            if (theme == ThemeVariant.Dark)
            {
                // 暗色主题：用半透明白色（更明显）
                return new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
            }
            else
            {
                // 亮色主题：用半透明黑色（避免与白背景混淆）
                return new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            }
        }
        catch
        {
            // 回退：半透明白色
            return new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
        }
    }

    // 空格/背景格子画刷：使用透明，避免方块移动/旋转时留下“经过路径”
    // 网格可见性由单元格的 BorderBrush/BorderThickness 提供（在 XAML 中设置）
    private static SolidColorBrush GetEmptyBrush()
    {
        return new SolidColorBrush(Colors.Transparent);
    }
}