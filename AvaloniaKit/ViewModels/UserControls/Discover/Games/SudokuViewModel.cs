using Avalonia.Threading;
using AvaloniaKit.Tools.Helper;
using AvaloniaKit.ViewModels.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Timers;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

// ══════════════════════════════════════════════════════════════════════════════
//  SudokuViewModel — 数独（9×9）
//  · 随机回溯生成完整终盘 → 按难度挖洞（简单38/普通46/困难52）
//  · 点选格子 + 数字键盘/物理键盘 1-9 输入，实时冲突高亮，提示=按终盘填当前格
//  · 胜利判定按"约束全满足"而非硬对终盘（多解盘也算赢）
//  · 音效：选格/落子/冲突/提示/胜利；最快用时持久化
// ══════════════════════════════════════════════════════════════════════════════
public partial class SudokuViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isWin;
    [ObservableProperty] private int _elapsed;
    [ObservableProperty] private int _bestTime;      // 0 = 暂无记录
    [ObservableProperty] private int _hintsLeft;
    [ObservableProperty] private string _difficulty = "普通";

    public ObservableCollection<SudokuCell> Cells { get; } = new();

    private readonly int[,] _solution = new int[9, 9];
    private readonly Random _rng = new();
    private Timer? _clock;
    private SudokuCell? _selected;

    public SudokuViewModel()
    {
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                Cells.Add(new SudokuCell
                {
                    Row = r,
                    Col = c,
                    // 3×3 宫粗线：左/上按整宫边界，右/下只补最外圈
                    BoxBorder = new Avalonia.Thickness(
                        c % 3 == 0 ? 2 : 0.5,
                        r % 3 == 0 ? 2 : 0.5,
                        c == 8 ? 2 : 0.5,
                        r == 8 ? 2 : 0.5),
                });

        _ = LoadBestAsync();
    }

    private async System.Threading.Tasks.Task LoadBestAsync()
    {
        int best = await GameScoreStore.LoadAsync("sudoku_best");
        await Dispatcher.UIThread.InvokeAsync(() => BestTime = best);
    }

    // ════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void GoBack()
    {
        _clock?.Stop();
        IsRunning = false;
        IsWin = false;
        WeakReferenceMessenger.Default.Send(new NavigateBackFromGameBoxesMessage());
    }

    /// <summary>参数：easy / normal / hard</summary>
    [RelayCommand]
    private void Start(string? level)
    {
        int holes;
        (Difficulty, holes) = level switch
        {
            "easy" => ("简单", 38),
            "hard" => ("困难", 52),
            _ => ("普通", 46),
        };

        GenerateSolution();

        // 挖洞
        Span<int> order = stackalloc int[81];
        for (int i = 0; i < 81; i++) order[i] = i;
        for (int i = 80; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (int i = 0; i < 81; i++)
        {
            var cell = Cells[i];
            cell.Value = _solution[i / 9, i % 9];
            cell.IsFixed = true;
            cell.IsSelected = false;
            cell.IsConflict = false;
        }
        for (int i = 0; i < holes; i++)
        {
            var cell = Cells[order[i]];
            cell.Value = 0;
            cell.IsFixed = false;
        }

        _selected = null;
        HintsLeft = 3;
        Elapsed = 0;
        IsWin = false;
        IsRunning = true;

        GameSfx.Start();
        StartClock();
    }

    /// <summary>点选格子（View 层 pointer 命中后调用）</summary>
    public void SelectCell(SudokuCell cell)
    {
        if (!IsRunning) return;

        if (_selected != null) _selected.IsSelected = false;
        _selected = cell;
        cell.IsSelected = true;
        GameSfx.Move();
    }

    /// <summary>数字键盘输入：参数 "1"~"9"</summary>
    [RelayCommand]
    private void InputNumber(string? num)
    {
        if (!IsRunning || _selected is null || _selected.IsFixed) return;
        if (!int.TryParse(num, out int v) || v is < 1 or > 9) return;

        _selected.Value = v;
        RefreshConflicts();

        if (_selected.IsConflict)
            GameSfx.Error();
        else
            GameSfx.Rotate();

        CheckWin();
    }

    [RelayCommand]
    private void Erase()
    {
        if (!IsRunning || _selected is null || _selected.IsFixed || _selected.Value == 0) return;
        _selected.Value = 0;
        RefreshConflicts();
        GameSfx.Drop();
    }

    /// <summary>提示：按终盘填当前选中格（限 3 次）</summary>
    [RelayCommand]
    private void Hint()
    {
        if (!IsRunning || HintsLeft <= 0) return;

        // 未选格或选中固定格时，自动挑第一个空格
        var target = _selected is { IsFixed: false } ? _selected : null;
        if (target is null || target.Value != 0)
        {
            foreach (var cell in Cells)
                if (!cell.IsFixed && cell.Value == 0) { target = cell; break; }
        }
        if (target is null) return;

        target.Value = _solution[target.Row, target.Col];
        HintsLeft--;
        RefreshConflicts();
        GameSfx.Merge();
        CheckWin();
    }

    // ════════════════════════════════════════════════════════════
    // 终盘生成（随机回溯，9×9 毫秒级完成）
    // ════════════════════════════════════════════════════════════

    private void GenerateSolution()
    {
        Array.Clear(_solution, 0, _solution.Length);
        Fill(0);
    }

    private bool Fill(int idx)
    {
        if (idx == 81) return true;
        int r = idx / 9, c = idx % 9;

        Span<int> nums = stackalloc int[9];
        for (int i = 0; i < 9; i++) nums[i] = i + 1;
        for (int i = 8; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (nums[i], nums[j]) = (nums[j], nums[i]);
        }

        foreach (int v in nums)
        {
            if (!ValidPlace(r, c, v)) continue;
            _solution[r, c] = v;
            if (Fill(idx + 1)) return true;
            _solution[r, c] = 0;
        }
        return false;
    }

    private bool ValidPlace(int row, int col, int v)
    {
        for (int i = 0; i < 9; i++)
        {
            if (_solution[row, i] == v || _solution[i, col] == v) return false;
        }
        int br = row / 3 * 3, bc = col / 3 * 3;
        for (int r = br; r < br + 3; r++)
            for (int c = bc; c < bc + 3; c++)
                if (_solution[r, c] == v) return false;
        return true;
    }

    // ════════════════════════════════════════════════════════════
    // 冲突检测 / 胜利判定
    // ════════════════════════════════════════════════════════════

    private void RefreshConflicts()
    {
        foreach (var cell in Cells) cell.IsConflict = false;

        for (int i = 0; i < 81; i++)
        {
            var a = Cells[i];
            if (a.Value == 0) continue;
            for (int j = i + 1; j < 81; j++)
            {
                var b = Cells[j];
                if (b.Value != a.Value) continue;
                bool sameUnit = a.Row == b.Row || a.Col == b.Col ||
                                (a.Row / 3 == b.Row / 3 && a.Col / 3 == b.Col / 3);
                if (!sameUnit) continue;
                a.IsConflict = true;
                b.IsConflict = true;
            }
        }
    }

    private void CheckWin()
    {
        foreach (var cell in Cells)
            if (cell.Value == 0 || cell.IsConflict) return;

        _clock?.Stop();
        IsWin = true;
        IsRunning = false;

        if (BestTime == 0 || Elapsed < BestTime)
        {
            BestTime = Elapsed;
            GameScoreStore.Save("sudoku_best", BestTime);
        }

        GameSfx.Vibrate();
        GameSfx.Win();
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
}

/// <summary>数独格子</summary>
public partial class SudokuCell : ObservableObject
{
    public int Row { get; init; }
    public int Col { get; init; }
    public Avalonia.Thickness BoxBorder { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private int _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextColor))]
    private bool _isFixed;

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextColor))]
    private bool _isConflict;

    public string Text => Value == 0 ? "" : Value.ToString();

    /// <summary>题面数字白、玩家填入蓝、冲突红</summary>
    public string TextColor => IsConflict ? "#EF5350" : IsFixed ? "#E8EAED" : "#4FC3F7";
}
