using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

/// <summary>方块类型（决定颜色）</summary>
public enum TetrominoType
{
    Empty = 0,
    I = 1, O = 2, T = 3, S = 4, Z = 5, J = 6, L = 7,
    Ghost = 8   // 幽灵（落点预览）
}

/// <summary>游戏板单个格子</summary>
public partial class CellViewModel : ObservableObject
{
    [ObservableProperty] private TetrominoType _type = TetrominoType.Empty;
    public int Row { get; init; }
    public int Col { get; init; }
}

/// <summary>
/// 所有方块的 4 种旋转形态
/// index 0 = Empty 占位，1..7 对应 TetrominoType
/// 每个 int[2] = [deltaRow, deltaCol]，相对于锚点偏移
/// </summary>
public static class TetrominoShapes
{
    public static readonly int[][][][] All =
    {
        Array.Empty<int[][]>(), // 0: Empty

        // 1: I  ████
        new[]
        {
            new[]{new[]{0,0},new[]{0,1},new[]{0,2},new[]{0,3}},
            new[]{new[]{0,2},new[]{1,2},new[]{2,2},new[]{3,2}},
            new[]{new[]{2,0},new[]{2,1},new[]{2,2},new[]{2,3}},
            new[]{new[]{0,1},new[]{1,1},new[]{2,1},new[]{3,1}},
        },
        // 2: O  ██
        //       ██
        new[]
        {
            new[]{new[]{0,0},new[]{0,1},new[]{1,0},new[]{1,1}},
            new[]{new[]{0,0},new[]{0,1},new[]{1,0},new[]{1,1}},
            new[]{new[]{0,0},new[]{0,1},new[]{1,0},new[]{1,1}},
            new[]{new[]{0,0},new[]{0,1},new[]{1,0},new[]{1,1}},
        },
        // 3: T
        new[]
        {
            new[]{new[]{0,0},new[]{0,1},new[]{0,2},new[]{1,1}},
            new[]{new[]{0,1},new[]{1,0},new[]{1,1},new[]{2,1}},
            new[]{new[]{1,1},new[]{2,0},new[]{2,1},new[]{2,2}},
            new[]{new[]{0,1},new[]{1,1},new[]{1,2},new[]{2,1}},
        },
        // 4: S
        new[]
        {
            new[]{new[]{0,1},new[]{0,2},new[]{1,0},new[]{1,1}},
            new[]{new[]{0,0},new[]{1,0},new[]{1,1},new[]{2,1}},
            new[]{new[]{1,1},new[]{1,2},new[]{2,0},new[]{2,1}},
            new[]{new[]{0,1},new[]{1,1},new[]{1,2},new[]{2,2}},
        },
        // 5: Z
        new[]
        {
            new[]{new[]{0,0},new[]{0,1},new[]{1,1},new[]{1,2}},
            new[]{new[]{0,2},new[]{1,1},new[]{1,2},new[]{2,1}},
            new[]{new[]{1,0},new[]{1,1},new[]{2,1},new[]{2,2}},
            new[]{new[]{0,1},new[]{1,0},new[]{1,1},new[]{2,0}},
        },
        // 6: J
        new[]
        {
            new[]{new[]{0,0},new[]{1,0},new[]{1,1},new[]{1,2}},
            new[]{new[]{0,1},new[]{0,2},new[]{1,1},new[]{2,1}},
            new[]{new[]{1,0},new[]{1,1},new[]{1,2},new[]{2,2}},
            new[]{new[]{0,1},new[]{1,1},new[]{2,0},new[]{2,1}},
        },
        // 7: L
        new[]
        {
            new[]{new[]{0,2},new[]{1,0},new[]{1,1},new[]{1,2}},
            new[]{new[]{0,1},new[]{1,1},new[]{2,1},new[]{2,2}},
            new[]{new[]{1,0},new[]{1,1},new[]{1,2},new[]{2,0}},
            new[]{new[]{0,0},new[]{0,1},new[]{1,1},new[]{2,1}},
        },
    };
}
