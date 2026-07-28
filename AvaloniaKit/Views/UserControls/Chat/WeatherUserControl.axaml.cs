using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaKit.ViewModels.UserControls.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AvaloniaKit.Views.UserControls.Chat;

// ══════════════════════════════════════════════════════════════════════════════
//  WeatherUserControl — 天气动效引擎 v2（共享 code-behind，三端同一份逻辑）
//  · Sunny(昼)  → 双层柔光晕呼吸 + 微光斑；Sunny(夜) → 月亮 + 闪烁星空
//  · Cloudy/Overcast → 三层视差云（远小慢暗、近大快亮）
//  · Fog        → 横向漂移的半透明雾带
//  · Wind       → 云快速掠过 + 弧线飘叶
//  · Rain       → 渐变雨丝（近粗快、远细慢）+ 落地涟漪感由速度差呈现
//  · Thunder    → 雨丝 + 随机双连闪电
//  · Snow       → 大小景深雪花摇曳飘落
//  · 与播放器黑胶动画同款 DispatcherTimer 驱动（约 30fps），离开页面自动停表
// ══════════════════════════════════════════════════════════════════════════════
public partial class WeatherUserControl : UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly Random _rnd = new();
    private readonly List<Particle> _particles = new();

    private WeatherViewModel? _vm;
    private string _scene = "";           // 当前场景（含昼夜后缀）
    private double _tick;
    private int _flashCooldown = 100;
    private int _flashStage;              // 闪电阶段：0无 1亮 2暗 3亮 4淡出

    // 太阳/月亮专用元素
    private Ellipse? _glowOuter, _glowInner;

    private class Particle
    {
        public Control Visual = null!;
        public double X, Y, Vx, Vy;
        public double Phase, PhaseSpeed;
        public double Size;
        public double BaseOpacity = 1;
        public int Layer;                 // 0=远景 1=中景 2=近景
    }

    public WeatherUserControl()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;

        DataContextChanged += OnDataContextChanged;
        FxCanvas.SizeChanged += (_, _) => RebuildScene();

        // 城市弹层遮罩：点击空白处关闭
        CityScrim.PointerPressed += OnScrimPressed;
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        // 城市弹层遮罩点击 → 走 VM 命令关闭（视图不直接改写 VM 状态）
        if (_vm is { IsCityPanelOpen: true } vm)
        {
            vm.CloseCityPanelCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        RebuildScene();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as WeatherViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyBackground();
        RebuildScene();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WeatherViewModel.WeatherKind)
                          or nameof(WeatherViewModel.IsNight))
            RebuildScene();
        else if (e.PropertyName is nameof(WeatherViewModel.BgTopColor)
                              or nameof(WeatherViewModel.BgBottomColor))
            ApplyBackground();
    }

    // ── 背景渐变 ─────────────────────────────────────────────────────────────
    private void ApplyBackground()
    {
        if (_vm == null) return;
        try
        {
            RootGrid.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(_vm.BgTopColor), 0),
                    new GradientStop(Color.Parse(_vm.BgBottomColor), 1),
                },
            };
        }
        catch { /* 颜色串异常时保持上一次背景 */ }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  场景搭建
    // ══════════════════════════════════════════════════════════════════════════
    private void RebuildScene()
    {
        string kind = _vm?.WeatherKind ?? "";
        bool night = _vm?.IsNight ?? false;
        double w = FxCanvas.Bounds.Width, h = FxCanvas.Bounds.Height;
        if (w < 10 || h < 10) return;

        string scene = kind + (night ? "-N" : "");
        _scene = scene;

        FxCanvas.Children.Clear();
        _particles.Clear();
        _glowOuter = _glowInner = null;
        _flashStage = 0;
        FlashLayer.Opacity = 0;

        switch (kind)
        {
            case "Sunny":
                if (night) { BuildStars(w, h, 46); BuildMoon(w); }
                else BuildSun(w);
                break;

            case "Cloudy":
                if (night) BuildStars(w, h, 24);
                BuildCloudLayers(w, h, far: 2, mid: 2, near: 2, speed: 1);
                break;

            case "Overcast":
                BuildCloudLayers(w, h, far: 3, mid: 3, near: 2, speed: 0.7);
                break;

            case "Fog":
                BuildFogBands(w, h, 5);
                break;

            case "Wind":
                BuildCloudLayers(w, h, far: 1, mid: 2, near: 2, speed: 5);
                BuildLeaves(w, h, 12);
                break;

            case "Rain":
                BuildCloudLayers(w, h, far: 0, mid: 2, near: 1, speed: 1.4, dark: true);
                BuildRain(w, h, 70);
                break;

            case "Thunder":
                BuildCloudLayers(w, h, far: 0, mid: 2, near: 2, speed: 1.8, dark: true);
                BuildRain(w, h, 90);
                break;

            case "Snow":
                BuildSnow(w, h, 56);
                break;
        }
    }

    // ── 晴（昼）：双层柔光晕 + 太阳核心 ─────────────────────────────────────
    private void BuildSun(double w)
    {
        double cx = w - 78, cy = 100;

        _glowOuter = MakeGlow(210, "#55FFE9A0", "#00FFE9A0");
        Canvas.SetLeft(_glowOuter, cx - 105);
        Canvas.SetTop(_glowOuter, cy - 105);
        FxCanvas.Children.Add(_glowOuter);

        _glowInner = MakeGlow(120, "#88FFDF7E", "#00FFDF7E");
        Canvas.SetLeft(_glowInner, cx - 60);
        Canvas.SetTop(_glowInner, cy - 60);
        FxCanvas.Children.Add(_glowInner);

        var core = new Ellipse
        {
            Width = 54, Height = 54,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.Parse("#FFFFF3C4"), 0),
                    new GradientStop(Color.Parse("#FFFFD24E"), 1),
                },
            },
        };
        Canvas.SetLeft(core, cx - 27);
        Canvas.SetTop(core, cy - 27);
        FxCanvas.Children.Add(core);

        // 漂浮微光斑（增加空气感）
        for (int i = 0; i < 6; i++)
        {
            double size = 4 + _rnd.NextDouble() * 7;
            var mote = new Ellipse
            {
                Width = size, Height = size,
                Fill = new SolidColorBrush(Color.Parse("#4DFFF2C0")),
            };
            var p = new Particle
            {
                Visual = mote,
                X = _rnd.NextDouble() * w,
                Y = 60 + _rnd.NextDouble() * 260,
                Vx = 0.12 + _rnd.NextDouble() * 0.2,
                Phase = _rnd.NextDouble() * Math.PI * 2,
                PhaseSpeed = 0.012 + _rnd.NextDouble() * 0.02,
                Size = size,
                BaseOpacity = 0.5,
            };
            FxCanvas.Children.Add(mote);
            _particles.Add(p);
        }
    }

    private static Ellipse MakeGlow(double size, string inner, string outer) => new()
    {
        Width = size, Height = size,
        Fill = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.Parse(inner), 0),
                new GradientStop(Color.Parse(outer), 1),
            },
        },
    };

    // ── 晴（夜）：月亮 + 星空 ────────────────────────────────────────────────
    private void BuildMoon(double w)
    {
        double cx = w - 80, cy = 96;

        _glowOuter = MakeGlow(170, "#33D8E8FF", "#00D8E8FF");
        Canvas.SetLeft(_glowOuter, cx - 85);
        Canvas.SetTop(_glowOuter, cy - 85);
        FxCanvas.Children.Add(_glowOuter);

        // 满月 + 弦月阴影（两圆叠加）
        var moon = new Ellipse
        {
            Width = 52, Height = 52,
            Fill = new SolidColorBrush(Color.Parse("#FFF6EFD8")),
        };
        Canvas.SetLeft(moon, cx - 26);
        Canvas.SetTop(moon, cy - 26);
        FxCanvas.Children.Add(moon);

        var shade = new Ellipse
        {
            Width = 44, Height = 44,
            Fill = new SolidColorBrush(Color.Parse("#2A3A55")),
            Opacity = 0.92,
        };
        Canvas.SetLeft(shade, cx - 26 + 16);
        Canvas.SetTop(shade, cy - 26 - 8);
        FxCanvas.Children.Add(shade);
    }

    private void BuildStars(double w, double h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double size = 1.2 + _rnd.NextDouble() * 2.2;
            var star = new Ellipse
            {
                Width = size, Height = size,
                Fill = new SolidColorBrush(Colors.White),
            };
            var p = new Particle
            {
                Visual = star,
                X = _rnd.NextDouble() * w,
                Y = _rnd.NextDouble() * h * 0.55,
                Phase = _rnd.NextDouble() * Math.PI * 2,
                PhaseSpeed = 0.02 + _rnd.NextDouble() * 0.05,
                Size = size,
                BaseOpacity = 0.35 + _rnd.NextDouble() * 0.5,
                Layer = 9,                // 星星：仅闪烁不位移
            };
            FxCanvas.Children.Add(star);
            _particles.Add(p);
        }
    }

    // ── 云：三层视差（远小慢暗、近大快亮），柔和椭圆组合 ────────────────────
    private void BuildCloudLayers(double w, double h, int far, int mid, int near,
                                  double speed, bool dark = false)
    {
        for (int i = 0; i < far; i++) AddCloud(w, h, 0, speed, dark);
        for (int i = 0; i < mid; i++) AddCloud(w, h, 1, speed, dark);
        for (int i = 0; i < near; i++) AddCloud(w, h, 2, speed, dark);
    }

    private void AddCloud(double w, double h, int layer, double speed, bool dark)
    {
        double size = layer switch { 0 => 90, 1 => 150, _ => 220 } + _rnd.Next(0, 40);
        double opacity = layer switch { 0 => 0.30, 1 => 0.42, _ => 0.55 };
        string tint = dark ? "#8FA3B8" : "#FFFFFF";

        var cloud = MakeCloud(size, tint);
        var p = new Particle
        {
            Visual = cloud,
            X = _rnd.NextDouble() * (w + size) - size,
            Y = layer switch
            {
                0 => 30 + _rnd.NextDouble() * 60,
                1 => 60 + _rnd.NextDouble() * 90,
                _ => 20 + _rnd.NextDouble() * 150,
            },
            Vx = (layer switch { 0 => 0.10, 1 => 0.22, _ => 0.38 })
                 * speed * (0.8 + _rnd.NextDouble() * 0.4),
            Size = size,
            BaseOpacity = opacity,
            Layer = layer,
        };
        cloud.Opacity = opacity;
        FxCanvas.Children.Add(cloud);
        _particles.Add(p);
    }

    /// <summary>用 5 个渐变椭圆拼出蓬松云团（比旧版 3 圆更柔和自然）</summary>
    private Panel MakeCloud(double size, string tint)
    {
        var panel = new Panel { Width = size, Height = size * 0.4 };
        var color = Color.Parse(tint);

        void Add(double wRatio, double hRatio, double xRatio, double yRatio, byte alpha)
        {
            var e = new Ellipse
            {
                Width = size * wRatio,
                Height = size * hRatio,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0),
                        new GradientStop(Color.FromArgb((byte)(alpha / 3), color.R, color.G, color.B), 0.75),
                        new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
                    },
                },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(size * xRatio, size * yRatio, 0, 0),
            };
            panel.Children.Add(e);
        }

        Add(0.46, 0.30, 0.00, 0.10, 220);
        Add(0.40, 0.34, 0.18, 0.00, 235);
        Add(0.44, 0.30, 0.36, 0.04, 225);
        Add(0.38, 0.26, 0.54, 0.12, 210);
        Add(0.60, 0.22, 0.12, 0.18, 240);
        return panel;
    }

    // ── 雾：横向漂移的柔和雾带 ──────────────────────────────────────────────
    private void BuildFogBands(double w, double h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double bw = w * (0.7 + _rnd.NextDouble() * 0.6);
            double bh = 46 + _rnd.NextDouble() * 40;
            var band = new Ellipse
            {
                Width = bw, Height = bh,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#66E8EFF5"), 0),
                        new GradientStop(Color.Parse("#00E8EFF5"), 1),
                    },
                },
            };
            var p = new Particle
            {
                Visual = band,
                X = _rnd.NextDouble() * w - bw / 2,
                Y = h * 0.12 + i * (h * 0.16),
                Vx = (i % 2 == 0 ? 1 : -1) * (0.15 + _rnd.NextDouble() * 0.2),
                Size = bw,
                BaseOpacity = 0.6,
                Layer = 8,                // 雾带：左右往返
            };
            FxCanvas.Children.Add(band);
            _particles.Add(p);
        }
    }

    // ── 风：弧线飘叶（椭圆小叶带自转） ──────────────────────────────────────
    private void BuildLeaves(double w, double h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var leaf = new Ellipse
            {
                Width = 7 + _rnd.NextDouble() * 5,
                Height = 4 + _rnd.NextDouble() * 3,
                Fill = new SolidColorBrush(Color.Parse(i % 3 == 0 ? "#AAF5E6A8" : "#99D9F0C8")),
                RenderTransform = new RotateTransform(_rnd.Next(360)),
            };
            var p = new Particle
            {
                Visual = leaf,
                X = _rnd.NextDouble() * w,
                Y = _rnd.NextDouble() * h,
                Vx = 4.2 + _rnd.NextDouble() * 3,
                Phase = _rnd.NextDouble() * Math.PI * 2,
                PhaseSpeed = 0.1 + _rnd.NextDouble() * 0.06,
                Size = leaf.Width,
                Layer = 7,                // 飘叶
            };
            FxCanvas.Children.Add(leaf);
            _particles.Add(p);
        }
    }

    // ── 雨：渐变雨丝，近景粗且快、远景细且慢（景深感） ──────────────────────
    private void BuildRain(double w, double h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int layer = _rnd.Next(3);
            double len = layer switch { 0 => 9, 1 => 14, _ => 20 } + _rnd.NextDouble() * 5;
            double thick = layer switch { 0 => 1.0, 1 => 1.4, _ => 1.9 };
            byte alpha = layer switch { 0 => (byte)0x38, 1 => (byte)0x55, _ => (byte)0x77 };

            var drop = new Line
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(-len * 0.18, len),
                Stroke = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0xD8, 0xEA, 0xFF), 0),
                        new GradientStop(Color.FromArgb(alpha, 0xD8, 0xEA, 0xFF), 1),
                    },
                },
                StrokeThickness = thick,
                StrokeLineCap = PenLineCap.Round,
            };
            var p = new Particle
            {
                Visual = drop,
                X = _rnd.NextDouble() * (w + 80) - 40,
                Y = _rnd.NextDouble() * h,
                Vx = -1.2 - layer * 0.5,
                Vy = 7 + layer * 4.5 + _rnd.NextDouble() * 3,
                Size = len,
                Layer = layer,
            };
            FxCanvas.Children.Add(drop);
            _particles.Add(p);
        }
    }

    // ── 雪：大小景深雪花摇曳飘落 ────────────────────────────────────────────
    private void BuildSnow(double w, double h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int layer = _rnd.Next(3);
            double size = layer switch { 0 => 2.5, 1 => 4.5, _ => 7 } + _rnd.NextDouble() * 2;
            var flake = new Ellipse
            {
                Width = size, Height = size,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Colors.White, 0),
                        new GradientStop(Color.Parse("#66FFFFFF"), 1),
                    },
                },
                Opacity = layer switch { 0 => 0.45, 1 => 0.7, _ => 0.95 },
            };
            var p = new Particle
            {
                Visual = flake,
                X = _rnd.NextDouble() * w,
                Y = _rnd.NextDouble() * h,
                Vy = 0.5 + layer * 0.7 + _rnd.NextDouble() * 0.5,
                Phase = _rnd.NextDouble() * Math.PI * 2,
                PhaseSpeed = 0.02 + _rnd.NextDouble() * 0.025,
                Size = size,
                BaseOpacity = flake.Opacity,
                Layer = layer,
            };
            FxCanvas.Children.Add(flake);
            _particles.Add(p);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  每帧更新（≈30fps）
    // ══════════════════════════════════════════════════════════════════════════
    private void OnTick(object? sender, EventArgs e)
    {
        double w = FxCanvas.Bounds.Width, h = FxCanvas.Bounds.Height;
        if (w < 10 || h < 10 || _particles.Count == 0 && _glowOuter == null) return;
        _tick++;

        // 光晕呼吸（太阳/月亮共用）
        if (_glowOuter != null)
        {
            double breath = 1 + 0.08 * Math.Sin(_tick / 28.0);
            _glowOuter.RenderTransform = new ScaleTransform(breath, breath);
            if (_glowInner != null)
            {
                double b2 = 1 + 0.12 * Math.Sin(_tick / 20.0 + 1.3);
                _glowInner.RenderTransform = new ScaleTransform(b2, b2);
            }
        }

        // 雷暴：双连闪（亮→暗→再亮→淡出，更接近真实闪电）
        if (_scene.StartsWith("Thunder"))
            UpdateLightning();

        bool isRain = _scene.StartsWith("Rain") || _scene.StartsWith("Thunder");
        bool isSnow = _scene.StartsWith("Snow");
        bool isWind = _scene.StartsWith("Wind");

        foreach (var p in _particles)
        {
            switch (p.Layer)
            {
                case 9:  // 星星：正弦闪烁
                    p.Phase += p.PhaseSpeed;
                    p.Visual.Opacity = p.BaseOpacity * (0.55 + 0.45 * Math.Sin(p.Phase));
                    continue;

                case 8:  // 雾带：左右往返漂移
                    p.X += p.Vx;
                    if (p.X < -p.Size * 0.6 || p.X > w - p.Size * 0.2) p.Vx = -p.Vx;
                    break;

                case 7:  // 飘叶：弧线掠过 + 自转
                    p.Phase += p.PhaseSpeed;
                    p.X += p.Vx;
                    p.Y += Math.Sin(p.Phase) * 1.8 + 0.4;
                    if (p.Visual.RenderTransform is RotateTransform rot)
                        rot.Angle += 5;
                    if (p.X > w + 20 || p.Y > h + 20)
                    {
                        p.X = -20 - _rnd.NextDouble() * 60;
                        p.Y = _rnd.NextDouble() * h * 0.8;
                    }
                    break;

                default:
                    if (isRain)
                    {
                        p.X += p.Vx;
                        p.Y += p.Vy;
                        if (p.Y > h + 24)
                        {
                            p.Y = -p.Size - _rnd.NextDouble() * 40;
                            p.X = _rnd.NextDouble() * (w + 80) - 40;
                        }
                        if (p.X < -40) p.X = w + 30;
                    }
                    else if (isSnow)
                    {
                        p.Phase += p.PhaseSpeed;
                        p.X += Math.Sin(p.Phase) * (0.4 + p.Layer * 0.3);
                        p.Y += p.Vy;
                        if (p.Y > h + 10) { p.Y = -10; p.X = _rnd.NextDouble() * w; }
                        if (p.X < -12) p.X = w + 6;
                        else if (p.X > w + 12) p.X = -6;
                    }
                    else // 云 / 光斑
                    {
                        p.X += p.Vx;
                        if (p.PhaseSpeed > 0)
                        {
                            // 光斑：上下浮动 + 忽明忽暗
                            p.Phase += p.PhaseSpeed;
                            p.Y += Math.Sin(p.Phase) * 0.25;
                            p.Visual.Opacity = p.BaseOpacity * (0.5 + 0.5 * Math.Sin(p.Phase * 1.7));
                        }
                        if (p.X > w + p.Size * (isWind ? 0.2 : 0.1))
                            p.X = -p.Size - _rnd.NextDouble() * 100;
                    }
                    break;
            }
            Canvas.SetLeft(p.Visual, p.X);
            Canvas.SetTop(p.Visual, p.Y);
        }
    }

    // ── 闪电：亮(0.65) → 暗(0.12) → 再亮(0.5) → 快速淡出 ────────────────────
    private void UpdateLightning()
    {
        switch (_flashStage)
        {
            case 0:
                if (_flashCooldown-- <= 0) { _flashStage = 1; FlashLayer.Opacity = 0.65; }
                break;
            case 1: FlashLayer.Opacity = 0.12; _flashStage = 2; break;
            case 2: FlashLayer.Opacity = 0.50; _flashStage = 3; break;
            case 3:
            default:
                FlashLayer.Opacity = Math.Max(0, FlashLayer.Opacity - 0.10);
                if (FlashLayer.Opacity <= 0)
                {
                    _flashStage = 0;
                    _flashCooldown = 110 + _rnd.Next(160);
                }
                break;
        }
    }
}
