using AvaloniaKit.Messages;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

// ══════════════════════════════════════════════════════════════════════════════
//  Game2048ViewModel — 2048（4×4 滑动合并）
//  · 操作：方向键/WASD/棋盘滑动手势/底部按钮，三端一致
//  · 音效：滑动/合并/大数字合并/胜利/失败（GameSfx 合成音，三端一致）
//  · 最高分持久化：IGameScoreStore（SQLite / localStorage）
// ══════════════════════════════════════════════════════════════════════════════
public partial class Game2048ViewModel : GameViewModelBase, ISubPageViewModel
{
    public const int Size = 4;

    [ObservableProperty] private int _score;
    [ObservableProperty] private int _highScore;
    [ObservableProperty] private bool _isGameOver;
    [ObservableProperty] private bool _isWin;          // 首次到 2048 弹提示
    [ObservableProperty] private bool _isRunning;

    public ObservableCollection<TileCell> Tiles { get; } = new();

    private readonly int[,] _board = new int[Size, Size];
    private readonly Random _rng = new();
    private bool _winNotified;   // 到 2048 后继续玩不再重复提示

    protected override string ScoreKey => "2048";

    public Game2048ViewModel(GameSfx sfx, IGameScoreStore scoreStore)
        : base(sfx, scoreStore)
    {
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                Tiles.Add(new TileCell { Row = r, Col = c });

        LoadScore(v => HighScore = Math.Max(HighScore, v));
    }

    // ════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void GoBack()
    {
        IsRunning = false;
        IsWin = false;
        WeakReferenceMessenger.Default.Send(new NavigateBackFromGameBoxesMessage());
    }

    [RelayCommand]
    private void Start()
    {
        Array.Clear(_board, 0, _board.Length);
        Score = 0;
        IsGameOver = false;
        IsWin = false;
        _winNotified = false;
        IsRunning = true;

        Sfx.Start();
        AddRandomTile();
        AddRandomTile();
        Render();
    }

    /// <summary>到达 2048 后选择继续挑战更大数字</summary>
    [RelayCommand]
    private void ContinuePlaying() => IsWin = false;

    [RelayCommand] private void MoveUp() => Move(-1, 0);
    [RelayCommand] private void MoveDown() => Move(1, 0);
    [RelayCommand] private void MoveLeft() => Move(0, -1);
    [RelayCommand] private void MoveRight() => Move(0, 1);

    // ════════════════════════════════════════════════════════════
    // 核心逻辑
    // ════════════════════════════════════════════════════════════

    private void Move(int dr, int dc)
    {
        if (!IsRunning || IsGameOver || IsWin) return;

        bool moved = false;
        int gained = 0;
        int maxMerged = 0;

        // 行/列压缩缓冲（循环外分配一次，避免 CA2014）
        Span<int> vals = stackalloc int[Size];
        Span<int> outVals = stackalloc int[Size];

        // 逐条线处理：把每行/列压缩 + 合并（一次移动每格最多合并一次）
        for (int line = 0; line < Size; line++)
        {
            // 取出该线上的非零值（按移动方向的先后顺序）
            int n = 0;
            for (int i = 0; i < Size; i++)
            {
                var (r, c) = LineCell(line, i, dr, dc);
                if (_board[r, c] != 0) vals[n++] = _board[r, c];
            }

            // 合并相邻同值
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                if (i + 1 < n && vals[i] == vals[i + 1])
                {
                    int merged = vals[i] * 2;
                    outVals[m++] = merged;
                    gained += merged;
                    if (merged > maxMerged) maxMerged = merged;
                    i++;
                }
                else
                {
                    outVals[m++] = vals[i];
                }
            }

            // 写回并检测变化
            for (int i = 0; i < Size; i++)
            {
                var (r, c) = LineCell(line, i, dr, dc);
                int nv = i < m ? outVals[i] : 0;
                if (_board[r, c] != nv) moved = true;
                _board[r, c] = nv;
            }
        }

        if (!moved)
        {
            Sfx.Error();
            return;
        }

        Score += gained;
        if (Score > HighScore)
        {
            HighScore = Score;
            SaveScore(HighScore);
        }

        // 音效分档：普通滑动 / 合并 / 大数字合并
        if (maxMerged >= 256) { Sfx.Combo(); Sfx.Vibrate(); }
        else if (maxMerged > 0) Sfx.Merge();
        else Sfx.Move();

        AddRandomTile();
        Render();

        if (!_winNotified && maxMerged >= 2048)
        {
            _winNotified = true;
            IsWin = true;
            Sfx.Win();
        }

        if (!CanMove())
        {
            IsGameOver = true;
            IsRunning = false;
            Sfx.GameOver();
        }
    }

    /// <summary>第 line 条线上、移动方向第 i 个格子的坐标</summary>
    private static (int r, int c) LineCell(int line, int i, int dr, int dc)
    {
        // 垂直移动：line=列。向上：从上往下取；向下：从下往上取
        if (dr != 0) return (dr < 0 ? i : Size - 1 - i, line);
        // 水平移动：line=行。向左：从左往右取；向右：从右往左取
        return (line, dc < 0 ? i : Size - 1 - i);
    }

    private void AddRandomTile()
    {
        Span<int> empty = stackalloc int[Size * Size];
        int n = 0;
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                if (_board[r, c] == 0) empty[n++] = r * Size + c;

        if (n == 0) return;
        int idx = empty[_rng.Next(n)];
        _board[idx / Size, idx % Size] = _rng.NextDouble() < 0.9 ? 2 : 4;
    }

    private bool CanMove()
    {
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
            {
                if (_board[r, c] == 0) return true;
                if (r + 1 < Size && _board[r, c] == _board[r + 1, c]) return true;
                if (c + 1 < Size && _board[r, c] == _board[r, c + 1]) return true;
            }
        return false;
    }

    private void Render()
    {
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                Tiles[r * Size + c].Value = _board[r, c];
    }
}

/// <summary>2048 棋盘格子</summary>
public partial class TileCell : ObservableObject
{
    public int Row { get; init; }
    public int Col { get; init; }

    [ObservableProperty] private int _value;
}
