using Avalonia.Threading;
using AvaloniaKit.Services;
using AvaloniaKit.ViewModels.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Timers;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

public partial class TetrisViewModel : ObservableObject
{
    // ── 常量 ────────────────────────────────────────────────────
    public const int Rows = 20;
    public const int Cols = 10;
    private const int InitMs = 800;
    private const int MinMs = 80;

    // ── 可观察属性 ──────────────────────────────────────────────
    [ObservableProperty] private int _score;
    [ObservableProperty] private int _level = 1;
    [ObservableProperty] private int _lines;
    [ObservableProperty] private bool _isGameOver;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isGhost;
    [ObservableProperty] private TetrominoType _nextType;

    // ── 格子集合（绑定给 ItemsControl）─────────────────────────
    public ObservableCollection<CellViewModel> Cells { get; } = new();
    public ObservableCollection<CellViewModel> PreviewCells { get; } = new();

    // ── 内部状态 ────────────────────────────────────────────────
    private readonly TetrominoType[,] _board = new TetrominoType[Rows, Cols];
    private TetrominoType _curType;
    private int _curRot, _curRow, _curCol;
    private TetrominoType _nextQueued;
    private readonly Random _rng = new();
    private Timer? _timer;

    private DateTime _lastInputTime = DateTime.MinValue;
    private const int InputIntervalMs = 60; // 60~100 最合适

    private bool CanInput()
    {
        var now = DateTime.Now;
        if ((now - _lastInputTime).TotalMilliseconds < InputIntervalMs)
            return false;

        _lastInputTime = now;
        return true;
    }

    // ════════════════════════════════════════════════════════════
    public TetrisViewModel()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                Cells.Add(new CellViewModel { Row = r, Col = c });

        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                PreviewCells.Add(new CellViewModel { Row = r, Col = c });

        _nextQueued = RandomType();
    }

    // ════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void GoBack()
    {
        // 停止计时器，清除暂停/运行状态，避免返回后再次打开时同时出现「未开始」与「已暂停」遮罩
        _timer?.Stop();
        IsPaused = false;
        IsRunning = false;

        WeakReferenceMessenger.Default.Send(new NavigateBackFromGameBoxesMessage());
    }

    [RelayCommand]
    private void Start()
    {
        ResetBoard();
        Score = 0; Level = 1; Lines = 0;
        IsGameOver = false; IsPaused = false; IsRunning = true;
        _nextQueued = RandomType();
        SpawnPiece();
        StartTimer();
    }

    [RelayCommand]
    private void Restart() => Start();

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsRunning || IsGameOver) return;
        IsPaused = !IsPaused;
        if (IsPaused) _timer?.Stop();
        else _timer?.Start();
    }

    [RelayCommand]
    public async Task MoveLeft()
    {
        if (!CanAct() || !CanInput()) return;

        //按键音效（三端一致：服务未注册时静默跳过）
        ServiceLocator.DeviceService?.PlaySound();

        // 从游戏循环线程更新 Cells 时
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (CanPlace(_curType, _curRot, _curRow, _curCol - 1))
            {
                _curCol--;
                Render();
            }
        });
    }

    [RelayCommand]
    public async Task MoveRight()
    {
        if (!CanAct() || !CanInput()) return;

        ServiceLocator.DeviceService?.PlaySound();

        // 从游戏循环线程更新 Cells 时
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (CanPlace(_curType, _curRot, _curRow, _curCol + 1))
            {
                _curCol++;
                Render();
            }
        });
    }

    [RelayCommand]
    public async Task SoftDrop()
    {
        if (!CanAct() || !CanInput()) return;

        ServiceLocator.DeviceService?.PlaySound();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (CanPlace(_curType, _curRot, _curRow + 1, _curCol))
            {
                _curRow++;
                Score += 1;
                Render();
            }
            else
            {
                LockPiece();
            }
        });
    }

    [RelayCommand]
    public async Task Rotate()
    {
        if (!CanAct() || !CanInput()) return;

        ServiceLocator.DeviceService?.PlaySound();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            int nextRot = (_curRot + 1) % 4;
            // Wall-kick: 原位 → 左1 → 右1 → 左2 → 右2
            foreach (int k in new[] { 0, -1, 1, -2, 2 })
            {
                if (CanPlace(_curType, nextRot, _curRow, _curCol + k))
                {
                    _curCol += k;
                    _curRot = nextRot;
                    Render();
                    return;
                }
            }
        });
    }

    [RelayCommand]
    public async Task HardDrop()
    {
        if (!CanAct() || !CanInput()) return;

        ServiceLocator.DeviceService?.PlaySound();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            int drop = 0;
            while (CanPlace(_curType, _curRot, _curRow + 1 + drop, _curCol))
                drop++;
            Score += drop * 2;
            _curRow += drop;
            LockPiece();
        });
    }

    // ════════════════════════════════════════════════════════════
    // 内部逻辑
    // ════════════════════════════════════════════════════════════

    private bool CanAct() => IsRunning && !IsPaused && !IsGameOver;

    private void StartTimer()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = new Timer(GetInterval()) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(Tick);
        _timer.Start();
    }

    private void UpdateTimerInterval()
    {
        if (_timer != null) _timer.Interval = GetInterval();
    }

    private double GetInterval()
        => Math.Max(InitMs * Math.Pow(0.85, Level - 1), MinMs);

    private void Tick()
    {
        if (!CanAct()) return;
        if (CanPlace(_curType, _curRot, _curRow + 1, _curCol))
        {
            _curRow++;
            Render();
        }
        else
        {
            LockPiece();
        }
    }

    private void LockPiece()
    {
        // 将当前方块写入 board
        foreach (var cell in GetShape(_curType, _curRot))
        {
            int r = _curRow + cell[0];
            int c = _curCol + cell[1];
            if (InBounds(r, c))
                _board[r, c] = _curType;
        }

        int cleared = ClearLines();
        if (cleared > 0)
        {
            AddScore(cleared);
        }

        SpawnPiece();
    }

    private int ClearLines()
    {
        int count = 0;
        for (int r = Rows - 1; r >= 0; r--)
        {
            bool full = true;
            for (int c = 0; c < Cols; c++)
            {
                if (_board[r, c] == TetrominoType.Empty)
                {
                    full = false;
                    break;
                }
            }
            if (!full) continue;

            //消行震动和音效（三端一致：桌面/Web 端 Vibrate 为空实现，静默跳过）
            ServiceLocator.DeviceService?.Vibrate();
            ServiceLocator.DeviceService?.PlaySound();

            // 向下移动上方所有行
            for (int rr = r; rr > 0; rr--)
                for (int c = 0; c < Cols; c++)
                    _board[rr, c] = _board[rr - 1, c];
            for (int c = 0; c < Cols; c++)
                _board[0, c] = TetrominoType.Empty;

            count++;
            r++; // 重新检查当前行（已被上方行替换）
        }
        return count;
    }

    private void AddScore(int cleared)
    {
        int[] pts = { 0, 100, 300, 500, 800 };
        Score += pts[Math.Min(cleared, 4)] * Level;
        Lines += cleared;
        int newLevel = Lines / 10 + 1;
        if (newLevel != Level)
        {
            Level = newLevel;
            UpdateTimerInterval();
        }
    }

    private void SpawnPiece()
    {
        _curType = _nextQueued;
        _nextQueued = RandomType();
        NextType = _nextQueued;
        _curRot = 0;
        _curRow = 0;
        _curCol = Cols / 2;

        if (!CanPlace(_curType, _curRot, _curRow, _curCol))
        {
            // 游戏结束
            IsGameOver = true;
            IsRunning = false;
            _timer?.Stop();
            Render();
            return;
        }

        RefreshPreview();
        Render();
    }

    // ── 渲染 ──────────────────────────────────────────────────

    private void Render()
    {
        // 1. 复制 board
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                CellAt(r, c).Type = _board[r, c];

        if (IsGameOver) return;

        // 2. 幽灵：找最低可落行
        if (IsGhost)
        {
            int ghostRow = _curRow;
            while (CanPlace(_curType, _curRot, ghostRow + 1, _curCol))
                ghostRow++;

            if (ghostRow != _curRow)
            {
                foreach (var cell in GetShape(_curType, _curRot))
                {
                    int gr = ghostRow + cell[0];
                    int gc = _curCol + cell[1];
                    if (InBounds(gr, gc) && _board[gr, gc] == TetrominoType.Empty)
                        CellAt(gr, gc).Type = TetrominoType.Ghost;
                }
            }
        }

        // 3. 当前方块（覆盖幽灵）
        foreach (var cell in GetShape(_curType, _curRot))
        {
            int dr = _curRow + cell[0];
            int dc = _curCol + cell[1];
            if (InBounds(dr, dc))
                CellAt(dr, dc).Type = _curType;
        }
    }

    private void RefreshPreview()
    {
        foreach (var c in PreviewCells) c.Type = TetrominoType.Empty;

        var cells = GetShape(_nextQueued, 0);
        if (cells.Length == 0) return;

        int minR = int.MaxValue, minC = int.MaxValue;
        int maxR = int.MinValue, maxC = int.MinValue;
        foreach (var cell in cells)
        {
            if (cell[0] < minR) minR = cell[0];
            if (cell[0] > maxR) maxR = cell[0];
            if (cell[1] < minC) minC = cell[1];
            if (cell[1] > maxC) maxC = cell[1];
        }

        int offR = (4 - (maxR - minR + 1)) / 2 - minR;
        int offC = (4 - (maxC - minC + 1)) / 2 - minC;

        foreach (var cell in cells)
        {
            int r = cell[0] + offR;
            int c = cell[1] + offC;
            if (r >= 0 && r < 4 && c >= 0 && c < 4)
                PreviewCells[r * 4 + c].Type = _nextQueued;
        }
    }

    // ── 辅助 ──────────────────────────────────────────────────

    /// <summary>
    /// 核心碰撞检测：判断指定类型/旋转/位置的方块是否可以放置。
    /// 注意：使用传入的 type 和 rot 参数，而非 _curType/_curRot，
    /// 避免旋转预测时误用当前状态。
    /// </summary>
    private bool CanPlace(TetrominoType type, int rot, int row, int col)
    {
        foreach (var cell in GetShape(type, rot))
        {
            int r = row + cell[0];
            int c = col + cell[1];
            if (r < 0 || r >= Rows || c < 0 || c >= Cols)
                return false;
            if (_board[r, c] != TetrominoType.Empty)
                return false;
        }
        return true;
    }

    private CellViewModel CellAt(int row, int col)
        => Cells[row * Cols + col];

    private static int[][] GetShape(TetrominoType type, int rot)
        => type == TetrominoType.Empty
            ? Array.Empty<int[]>()
            : TetrominoShapes.All[(int)type][rot];

    private static bool InBounds(int r, int c)
        => r >= 0 && r < Rows && c >= 0 && c < Cols;

    private void ResetBoard()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _board[r, c] = TetrominoType.Empty;
        foreach (var cell in Cells)
            cell.Type = TetrominoType.Empty;
    }

    private TetrominoType RandomType()
        => (TetrominoType)(_rng.Next(7) + 1);
}

// ═══════════════════════════════════════════════════════════════
// 数据模型
// ═══════════════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════════════
// TetrisViewModel
// ═══════════════════════════════════════════════════════════════

