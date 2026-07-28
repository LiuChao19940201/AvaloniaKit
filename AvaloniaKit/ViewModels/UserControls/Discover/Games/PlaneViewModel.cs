using Avalonia.Threading;
using AvaloniaKit.Messages;
using AvaloniaKit.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Timers;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

// ══════════════════════════════════════════════════════════════════════════════
//  PlaneViewModel — 飞机大战（逻辑区 320×480，30fps 实时循环）
//  · 操作：游戏区拖动跟手（移动端）/ 方向键 WASD（桌面），自动开火
//  · 敌机随分数提波次：刷新更快、下落更快；被击中有爆炸帧
//  · 命中后 1.5 秒无敌闪烁；3 条命；最高分持久化
//  · 音效：射击/命中/敌机爆炸/玩家中弹震动/波次提升/结束
// ══════════════════════════════════════════════════════════════════════════════
public partial class PlaneViewModel : GameViewModelBase, ISubPageViewModel
{
    public const double AreaW = 320;
    public const double AreaH = 480;

    private const int TickMs = 33;              // ~30fps
    private const double PlayerW = 36, PlayerH = 36;
    private const double BulletSpeed = 380;     // px/s
    private const double KeyStep = 14;          // 键盘步进

    [ObservableProperty] private int _score;
    [ObservableProperty] private int _highScore;
    [ObservableProperty] private int _lives;
    [ObservableProperty] private int _wave = 1;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isGameOver;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private double _playerX = (AreaW - PlayerW) / 2;
    [ObservableProperty] private double _playerY = AreaH - PlayerH - 24;
    [ObservableProperty] private double _playerOpacity = 1.0;

    public ObservableCollection<Sprite> Sprites { get; } = new();

    private readonly Random _rng = new();
    private Timer? _timer;
    private double _fireCooldown;
    private double _spawnCooldown;
    private int _invincibleTicks;

    protected override string ScoreKey => "plane";

    public PlaneViewModel(GameSfx sfx, IGameScoreStore scoreStore)
        : base(sfx, scoreStore)
    {
        LoadScore(v => HighScore = Math.Max(HighScore, v));
    }

    // ════════════════════════════════════════════════════════════
    // Commands
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void GoBack()
    {
        _timer?.Stop();
        IsRunning = false;
        IsGameOver = false;
        IsPaused = false;
        Sprites.Clear();
        WeakReferenceMessenger.Default.Send(new NavigateBackFromGameBoxesMessage());
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsRunning || IsGameOver) return;
        IsPaused = !IsPaused;
        if (IsPaused) _timer?.Stop();
        else _timer?.Start();
    }

    [RelayCommand]
    private void Start()
    {
        Sprites.Clear();
        Score = 0;
        Lives = 3;
        Wave = 1;
        PlayerX = (AreaW - PlayerW) / 2;
        PlayerY = AreaH - PlayerH - 24;
        PlayerOpacity = 1.0;
        _fireCooldown = 0;
        _spawnCooldown = 400;
        _invincibleTicks = 0;
        IsGameOver = false;
        IsPaused = false;
        IsRunning = true;

        Sfx.Start();
        StartTimer();
    }

    /// <summary>指针拖动：机身中心跟手（View 已换算为逻辑坐标）</summary>
    public void MovePlayerTo(double x, double y)
    {
        if (!IsRunning || IsPaused) return;
        PlayerX = Math.Clamp(x - PlayerW / 2, 0, AreaW - PlayerW);
        PlayerY = Math.Clamp(y - PlayerH / 2, 0, AreaH - PlayerH);
    }

    [RelayCommand] private void MoveLeft() => Nudge(-KeyStep, 0);
    [RelayCommand] private void MoveRight() => Nudge(KeyStep, 0);
    [RelayCommand] private void MoveUp() => Nudge(0, -KeyStep);
    [RelayCommand] private void MoveDown() => Nudge(0, KeyStep);

    private void Nudge(double dx, double dy)
    {
        if (!IsRunning || IsPaused) return;
        PlayerX = Math.Clamp(PlayerX + dx, 0, AreaW - PlayerW);
        PlayerY = Math.Clamp(PlayerY + dy, 0, AreaH - PlayerH);
    }

    // ════════════════════════════════════════════════════════════
    // 游戏循环
    // ════════════════════════════════════════════════════════════

    private void StartTimer()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = new Timer(TickMs) { AutoReset = true };
        _timer.Elapsed += (_, _) => Dispatcher.UIThread.Post(Tick);
        _timer.Start();
    }

    private void Tick()
    {
        if (!IsRunning || IsPaused) return;

        double dt = TickMs / 1000.0;

        // ── 无敌闪烁 ──
        if (_invincibleTicks > 0)
        {
            _invincibleTicks--;
            PlayerOpacity = _invincibleTicks == 0 ? 1.0
                : (_invincibleTicks / 4 % 2 == 0 ? 0.35 : 0.9);
        }

        // ── 自动开火 ──
        _fireCooldown -= TickMs;
        if (_fireCooldown <= 0)
        {
            _fireCooldown = 260;
            Sprites.Add(new Sprite
            {
                Kind = SpriteKind.Bullet,
                X = PlayerX + PlayerW / 2 - 2,
                Y = PlayerY - 10,
                W = 4,
                H = 12,
                Vy = -BulletSpeed,
            });
            Sfx.Shoot();
        }

        // ── 刷敌机（波次越高越快）──
        _spawnCooldown -= TickMs;
        if (_spawnCooldown <= 0)
        {
            _spawnCooldown = Math.Max(900 - (Wave - 1) * 70, 320);
            double size = _rng.Next(26, 40);
            Sprites.Add(new Sprite
            {
                Kind = SpriteKind.Enemy,
                X = _rng.NextDouble() * (AreaW - size),
                Y = -size,
                W = size,
                H = size,
                Vy = 80 + _rng.NextDouble() * 50 + (Wave - 1) * 12,
            });
        }

        // ── 移动 & 出界清理 & 爆炸帧衰减 ──
        for (int i = Sprites.Count - 1; i >= 0; i--)
        {
            var s = Sprites[i];
            s.Y += s.Vy * dt;

            if (s.Kind == SpriteKind.Boom)
            {
                s.Ttl--;
                if (s.Ttl <= 0) { Sprites.RemoveAt(i); continue; }
            }

            if (s.Y < -60 || s.Y > AreaH + 60)
                Sprites.RemoveAt(i);
        }

        // ── 子弹 × 敌机碰撞 ──
        for (int i = Sprites.Count - 1; i >= 0; i--)
        {
            if (i >= Sprites.Count) continue;
            var b = Sprites[i];
            if (b.Kind != SpriteKind.Bullet) continue;

            for (int j = Sprites.Count - 1; j >= 0; j--)
            {
                var e = Sprites[j];
                if (e.Kind != SpriteKind.Enemy) continue;
                if (!Overlap(b.X, b.Y, b.W, b.H, e.X, e.Y, e.W, e.H)) continue;

                // 爆炸帧
                Sprites.Add(new Sprite
                {
                    Kind = SpriteKind.Boom,
                    X = e.X, Y = e.Y, W = e.W, H = e.H,
                    Ttl = 6,
                });
                // 先删索引大的
                if (i > j) { Sprites.RemoveAt(i); Sprites.Remove(e); }
                else { Sprites.Remove(e); Sprites.RemoveAt(i); }

                Score += 10;
                Sfx.Hit();

                if (Score > HighScore)
                {
                    HighScore = Score;
                    SaveScore(HighScore);
                }

                int newWave = Score / 200 + 1;
                if (newWave != Wave)
                {
                    Wave = newWave;
                    Sfx.LevelUp();
                }
                break;
            }
        }

        // ── 敌机 × 玩家碰撞（收缩判定盒，手感更公平）──
        if (_invincibleTicks <= 0)
        {
            for (int i = Sprites.Count - 1; i >= 0; i--)
            {
                var e = Sprites[i];
                if (e.Kind != SpriteKind.Enemy) continue;
                if (!Overlap(PlayerX + 6, PlayerY + 6, PlayerW - 12, PlayerH - 12,
                             e.X, e.Y, e.W, e.H)) continue;

                Sprites.Add(new Sprite
                {
                    Kind = SpriteKind.Boom,
                    X = e.X, Y = e.Y, W = e.W, H = e.H,
                    Ttl = 6,
                });
                Sprites.RemoveAt(i);

                Lives--;
                Sfx.Vibrate();
                Sfx.Explode();

                if (Lives <= 0)
                {
                    GameOverNow();
                    return;
                }

                _invincibleTicks = 45;   // 1.5 秒无敌
                break;
            }
        }
    }

    private void GameOverNow()
    {
        _timer?.Stop();
        IsGameOver = true;
        IsRunning = false;
        PlayerOpacity = 1.0;

        if (Score > HighScore)
        {
            HighScore = Score;
            SaveScore(HighScore);
        }
        Sfx.GameOver();
    }

    private static bool Overlap(double x1, double y1, double w1, double h1,
                                double x2, double y2, double w2, double h2)
        => x1 < x2 + w2 && x2 < x1 + w1 && y1 < y2 + h2 && y2 < y1 + h1;
}

/// <summary>游戏区活动实体（子弹/敌机/爆炸帧）</summary>
public partial class Sprite : ObservableObject
{
    public SpriteKind Kind { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public double Vy { get; init; }
    public int Ttl { get; set; }        // 爆炸帧存活 tick 数

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;

    // 供 DataTemplate 按类型切换显示
    public bool IsBullet => Kind == SpriteKind.Bullet;
    public bool IsEnemy => Kind == SpriteKind.Enemy;
    public bool IsBoom => Kind == SpriteKind.Boom;
}

public enum SpriteKind { Bullet, Enemy, Boom }
