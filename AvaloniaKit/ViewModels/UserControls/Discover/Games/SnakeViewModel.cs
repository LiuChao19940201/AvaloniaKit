using Avalonia.Threading;
using AvaloniaKit.Tools.Helper;
using AvaloniaKit.ViewModels.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Timers;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

public partial class SnakeViewModel : ObservableObject
{
    public const int Rows = 20;
    public const int Cols = 20;

    // 速度曲线：初始 160ms/格，每吃 3 个食物加速一档，下限 70ms
    private const int InitMs = 160;
    private const int MinMs = 70;
    private const int SpeedStep = 8;

    [ObservableProperty] private int _score;
    [ObservableProperty] private int _highScore;
    [ObservableProperty] private int _length;
    [ObservableProperty] private int _speedLevel = 1;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isGameOver;

    public ObservableCollection<SnakeCell> Cells { get; } = new();

    private readonly SnakeCell[,] _grid = new SnakeCell[Rows, Cols];
    private readonly LinkedList<(int r, int c)> _snake = new();

    private (int r, int c) _food;
    private (int r, int c) _dir = (0, 1);

    // 转向队列：一帧内最多缓存 2 次转向，彻底避免「快速连按两键 180° 掉头」
    private readonly Queue<(int r, int c)> _dirQueue = new();

    private Timer? _timer;
    private readonly Random _rng = new();
    private int _foodEaten;

    public SnakeViewModel()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                var cell = new SnakeCell { Row = r, Col = c };
                Cells.Add(cell);
                _grid[r, c] = cell;
            }

        // 最高分：从本地存储恢复（三端：SQLite / localStorage）
        _ = LoadHighScoreAsync();
    }

    private async Task LoadHighScoreAsync()
    {
        int hs = await GameScoreStore.LoadAsync("snake");
        await Dispatcher.UIThread.InvokeAsync(() => HighScore = Math.Max(HighScore, hs));
    }

    // ============================
    // Commands
    // ============================

    [RelayCommand]
    private void Start()
    {
        Reset();

        SpawnFood();   // ✅ 先生成食物
        Render();      // ✅ 再渲染（避免被覆盖）

        IsRunning = true;
        IsPaused = false;

        GameSfx.Start();
        StartTimer();
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsRunning || IsGameOver) return;

        IsPaused = !IsPaused;

        if (IsPaused)
            _timer?.Stop();
        else
            _timer?.Start();
    }

    [RelayCommand] private void Up() => EnqueueDir((-1, 0));
    [RelayCommand] private void Down() => EnqueueDir((1, 0));
    [RelayCommand] private void Left() => EnqueueDir((0, -1));
    [RelayCommand] private void Right() => EnqueueDir((0, 1));

    private void EnqueueDir((int r, int c) dir)
    {
        if (!IsRunning || IsPaused || IsGameOver) return;

        // 以队尾（若有）或当前方向为基准判定反向
        var basis = _dirQueue.Count > 0 ? _dirQueue.Peek() : _dir;
        if (basis == (-dir.r, -dir.c) || basis == dir) return;

        if (_dirQueue.Count < 2)
        {
            _dirQueue.Enqueue(dir);
            GameSfx.Move();
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _timer?.Stop();
        IsRunning = false;
        IsPaused = false;
        IsGameOver = false;

        WeakReferenceMessenger.Default.Send(new NavigateBackFromGameBoxesMessage());
    }

    // ============================
    // 核心循环
    // ============================

    private void StartTimer()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = new Timer(GetInterval()) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(Tick);

        _timer.Start();
    }

    private double GetInterval()
        => Math.Max(InitMs - (SpeedLevel - 1) * SpeedStep, MinMs);

    private void Tick()
    {
        if (!IsRunning || IsPaused || IsGameOver)
            return;

        if (_dirQueue.Count > 0)
            _dir = _dirQueue.Dequeue();

        var head = _snake.First!.Value;
        var next = (head.r + _dir.r, head.c + _dir.c);

        // 撞墙 or 撞自己（尾巴本帧会移走，不算碰撞；食物不会生成在蛇身上）
        bool hitTail = IsSnake(next) && next != _snake.Last!.Value;
        if (!InBounds(next) || hitTail)
        {
            GameOverNow();
            return;
        }

        _snake.AddFirst(next);

        if (next == _food)
        {
            Score += 10;
            _foodEaten++;
            GameSfx.Eat();

            if (Score > HighScore)
            {
                HighScore = Score;
                GameScoreStore.Save("snake", HighScore);
            }

            // 每 3 个食物提一档速度
            int newLevel = _foodEaten / 3 + 1;
            if (newLevel != SpeedLevel)
            {
                SpeedLevel = newLevel;
                if (_timer != null) _timer.Interval = GetInterval();
                GameSfx.LevelUp();
            }

            SpawnFood();
        }
        else
        {
            _snake.RemoveLast();
        }

        Length = _snake.Count;

        Render();
    }

    private void GameOverNow()
    {
        _timer?.Stop();
        IsGameOver = true;
        IsRunning = false;

        GameSfx.Vibrate();
        GameSfx.GameOver();
        if (Score > HighScore)
        {
            HighScore = Score;
            GameScoreStore.Save("snake", HighScore);
        }

        Render();
    }

    // ============================
    // 渲染
    // ============================

    private void Render()
    {
        // 1. 清空
        foreach (var c in Cells)
            c.Color = "#222222";

        // 2. 食物（先画）
        _grid[_food.r, _food.c].Color = "#FF5252";

        // 3. 蛇（后画，避免被覆盖；蛇身由深到浅渐变更有层次）
        int i = 0, n = _snake.Count;
        foreach (var s in _snake)
        {
            if (i == 0)
                _grid[s.r, s.c].Color = "#2E7D32"; // 蛇头（深绿更醒目）
            else
            {
                // 蛇身渐变：#4CAF50 → #A5D6A7
                double t = n <= 1 ? 0 : (double)i / (n - 1);
                byte rr = (byte)(0x4C + (0xA5 - 0x4C) * t);
                byte gg = (byte)(0xAF + (0xD6 - 0xAF) * t);
                byte bb = (byte)(0x50 + (0xA7 - 0x50) * t);
                _grid[s.r, s.c].Color = $"#{rr:X2}{gg:X2}{bb:X2}";
            }
            i++;
        }
    }

    private void SpawnFood()
    {
        if (_snake.Count >= Rows * Cols - 1)
            return;

        while (true)
        {
            var r = _rng.Next(Rows);
            var c = _rng.Next(Cols);

            if (!IsSnake((r, c)))
            {
                _food = (r, c);
                return;
            }
        }
    }

    private void Reset()
    {
        _timer?.Stop();

        _snake.Clear();
        _dirQueue.Clear();

        // ✅ 初始蛇长度 = 3（更自然）
        _snake.AddFirst((10, 10));
        _snake.AddLast((10, 9));
        _snake.AddLast((10, 8));

        Score = 0;
        Length = 3;
        SpeedLevel = 1;
        _foodEaten = 0;
        IsGameOver = false;

        _dir = (0, 1);

        foreach (var cell in Cells)
            cell.Color = "#222222";
    }

    // ============================
    // 工具
    // ============================

    private bool InBounds((int r, int c) p)
        => p.r >= 0 && p.r < Rows && p.c >= 0 && p.c < Cols;

    private bool IsSnake((int r, int c) p)
        => _snake.Contains(p);
}

// ============================
// Cell
// ============================

public partial class SnakeCell : ObservableObject
{
    public int Row { get; init; }
    public int Col { get; init; }

    [ObservableProperty]
    private string _color = "#222222";
}
