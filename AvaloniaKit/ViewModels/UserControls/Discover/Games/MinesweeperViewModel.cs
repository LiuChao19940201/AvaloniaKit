using Avalonia.Threading;
using AvaloniaKit.Messages;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Timers;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

// ══════════════════════════════════════════════════════════════════════════════
//  MinesweeperViewModel — 扫雷（10×14 / 22 雷）
//  · 首点必安全（首次翻开后才布雷，排除点击位及其周围一圈）
//  · 插旗模式开关（移动端友好）+ 桌面右键插旗 + 数字格双击式 Chording
//  · 音效：翻格/插旗/踩雷爆炸+震动/胜利；最佳用时持久化（越小越好）
// ══════════════════════════════════════════════════════════════════════════════
public partial class MinesweeperViewModel : GameViewModelBase, ISubPageViewModel
{
    public const int Rows = 14;
    public const int Cols = 10;
    private const int MineCount = 22;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isGameOver;
    [ObservableProperty] private bool _isWin;
    [ObservableProperty] private bool _isFlagMode;
    [ObservableProperty] private int _minesLeft = MineCount;
    [ObservableProperty] private int _elapsed;
    [ObservableProperty] private int _bestTime;   // 0 = 暂无记录

    public ObservableCollection<MineCell> Cells { get; } = new();

    private readonly bool[,] _mines = new bool[Rows, Cols];
    private readonly int[,] _adjacent = new int[Rows, Cols];
    private readonly Random _rng = new();
    private Timer? _clock;
    private bool _minesPlaced;
    private int _revealedCount;

    protected override string ScoreKey => "minesweeper_best";

    public MinesweeperViewModel(GameSfx sfx, IGameScoreStore scoreStore)
        : base(sfx, scoreStore)
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                Cells.Add(new MineCell { Row = r, Col = c });

        LoadScore(v => BestTime = v);
    }

    // ════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void GoBack()
    {
        StopClock();
        IsRunning = false;
        IsGameOver = false;
        IsWin = false;
        WeakReferenceMessenger.Default.Send(new NavigateBackFromGameBoxesMessage());
    }

    [RelayCommand]
    private void Start()
    {
        StopClock();
        Array.Clear(_mines, 0, _mines.Length);
        Array.Clear(_adjacent, 0, _adjacent.Length);
        _minesPlaced = false;
        _revealedCount = 0;

        foreach (var cell in Cells)
        {
            cell.IsRevealed = false;
            cell.IsFlagged = false;
            cell.Text = "";
            cell.TextColor = "#FFFFFF";
            cell.IsMineHit = false;
        }

        MinesLeft = MineCount;
        Elapsed = 0;
        IsGameOver = false;
        IsWin = false;
        IsFlagMode = false;
        IsRunning = true;

        Sfx.Start();
    }

    /// <summary>插旗模式切换（ToggleButton 双向绑定触发）</summary>
    partial void OnIsFlagModeChanged(bool value)
    {
        if (IsRunning) Sfx.Move();
    }

    /// <summary>格子点按：插旗模式→插旗；已翻开数字→Chording；否则翻开</summary>
    [RelayCommand]
    private void CellTap(MineCell? cell)
    {
        if (cell is null || !IsRunning) return;

        if (cell.IsRevealed)
        {
            Chord(cell);
            return;
        }

        if (IsFlagMode)
            ToggleFlag(cell);
        else
            Reveal(cell);
    }

    /// <summary>插旗（桌面右键 / 插旗模式点按共用入口）</summary>
    public void ToggleFlag(MineCell cell)
    {
        if (!IsRunning || cell.IsRevealed) return;

        cell.IsFlagged = !cell.IsFlagged;
        MinesLeft += cell.IsFlagged ? -1 : 1;
        Sfx.Flag();
    }

    // ════════════════════════════════════════════════════════════
    // 核心逻辑
    // ════════════════════════════════════════════════════════════

    private void Reveal(MineCell cell)
    {
        if (cell.IsFlagged || cell.IsRevealed) return;

        // 首点必安全：此时才布雷，排除点击位及周围一圈
        if (!_minesPlaced)
        {
            PlaceMines(cell.Row, cell.Col);
            _minesPlaced = true;
            StartClock();
        }

        if (_mines[cell.Row, cell.Col])
        {
            cell.IsMineHit = true;
            Lose();
            return;
        }

        FloodReveal(cell.Row, cell.Col);
        Sfx.Move();
        CheckWin();
    }

    /// <summary>Chording：已翻开数字格周围旗数等于数字时，一键翻开其余邻格</summary>
    private void Chord(MineCell cell)
    {
        int adj = _adjacent[cell.Row, cell.Col];
        if (adj == 0) return;

        int flags = 0;
        foreach (var (r, c) in Neighbors(cell.Row, cell.Col))
            if (CellAt(r, c).IsFlagged) flags++;

        if (flags != adj) { Sfx.Error(); return; }

        bool hitMine = false;
        foreach (var (r, c) in Neighbors(cell.Row, cell.Col))
        {
            var n = CellAt(r, c);
            if (n.IsRevealed || n.IsFlagged) continue;
            if (_mines[r, c])
            {
                n.IsMineHit = true;
                hitMine = true;
            }
            else
            {
                FloodReveal(r, c);
            }
        }

        if (hitMine) { Lose(); return; }
        Sfx.Move();
        CheckWin();
    }

    private void PlaceMines(int safeR, int safeC)
    {
        var candidates = new List<int>(Rows * Cols);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                // 排除首点及其周围一圈
                if (Math.Abs(r - safeR) <= 1 && Math.Abs(c - safeC) <= 1) continue;
                candidates.Add(r * Cols + c);
            }

        // Fisher-Yates 取前 MineCount 个
        for (int i = 0; i < MineCount && i < candidates.Count; i++)
        {
            int j = _rng.Next(i, candidates.Count);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            _mines[candidates[i] / Cols, candidates[i] % Cols] = true;
        }

        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                if (_mines[r, c]) continue;
                int count = 0;
                foreach (var (nr, nc) in Neighbors(r, c))
                    if (_mines[nr, nc]) count++;
                _adjacent[r, c] = count;
            }
    }

    /// <summary>洪泛翻开：0 格自动扩散到周围</summary>
    private void FloodReveal(int row, int col)
    {
        var stack = new Stack<(int r, int c)>();
        stack.Push((row, col));

        while (stack.Count > 0)
        {
            var (r, c) = stack.Pop();
            var cell = CellAt(r, c);
            if (cell.IsRevealed || cell.IsFlagged || _mines[r, c]) continue;

            cell.IsRevealed = true;
            _revealedCount++;

            int adj = _adjacent[r, c];
            if (adj > 0)
            {
                cell.Text = adj.ToString();
                cell.TextColor = AdjColor(adj);
            }
            else
            {
                foreach (var n in Neighbors(r, c))
                    stack.Push(n);
            }
        }
    }

    private static string AdjColor(int n) => n switch
    {
        1 => "#42A5F5",
        2 => "#66BB6A",
        3 => "#EF5350",
        4 => "#7E57C2",
        5 => "#EF6C00",
        6 => "#26A69A",
        7 => "#8D6E63",
        _ => "#B0BEC5",
    };

    private void Lose()
    {
        StopClock();
        IsGameOver = true;
        IsRunning = false;

        // 揭示所有雷
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_mines[r, c])
                {
                    var cell = CellAt(r, c);
                    cell.IsRevealed = true;
                    cell.Text = "💣";
                }

        Sfx.Vibrate();
        Sfx.Explode();
    }

    private void CheckWin()
    {
        if (_revealedCount < Rows * Cols - MineCount) return;

        StopClock();
        IsWin = true;
        IsRunning = false;
        MinesLeft = 0;

        // 最佳用时（越小越好，0 表示暂无记录）
        if (BestTime == 0 || Elapsed < BestTime)
        {
            BestTime = Elapsed;
            SaveScore(BestTime);
        }

        Sfx.Win();
    }

    // ── 计时 ──────────────────────────────────────────────────

    private void StartClock()
    {
        _clock?.Stop();
        _clock?.Dispose();
        _clock = new Timer(1000) { AutoReset = true };
        _clock.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => { if (IsRunning) Elapsed++; });
        _clock.Start();
    }

    private void StopClock()
    {
        _clock?.Stop();
    }

    // ── 工具 ──────────────────────────────────────────────────

    private MineCell CellAt(int r, int c) => Cells[r * Cols + c];

    private static IEnumerable<(int r, int c)> Neighbors(int row, int col)
    {
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int r = row + dr, c = col + dc;
                if (r >= 0 && r < Rows && c >= 0 && c < Cols)
                    yield return (r, c);
            }
    }
}

/// <summary>扫雷格子</summary>
public partial class MineCell : ObservableObject
{
    public int Row { get; init; }
    public int Col { get; init; }

    [ObservableProperty] private bool _isRevealed;
    [ObservableProperty] private bool _isFlagged;
    [ObservableProperty] private bool _isMineHit;   // 踩中的那颗雷标红
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _textColor = "#FFFFFF";
}
