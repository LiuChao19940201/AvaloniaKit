using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaKit.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace AvaloniaKit.Controls;

// ══════════════════════════════════════════════════════════════════════════════
//  NavChartView — 自绘净值折线图
//  1. 贝塞尔平滑曲线
//  2. 鼠标悬停：竖向十字线 + 日期/净值气泡
//  3. 滚轮缩放（X 轴区间缩放，Y 轴自适应）
//  4. 鼠标拖拽平移
//  5. 渐变填充区域
// ══════════════════════════════════════════════════════════════════════════════
public class NavChartView : Control
{
    // ── AvaloniaProperty ─────────────────────────────────────────────────
    public static readonly StyledProperty<ObservableCollection<NavPoint>?> PointsProperty =
        AvaloniaProperty.Register<NavChartView, ObservableCollection<NavPoint>?>(nameof(Points));

    public static readonly StyledProperty<double> YMinProperty =
        AvaloniaProperty.Register<NavChartView, double>(nameof(YMin), 0);

    public static readonly StyledProperty<double> YMaxProperty =
        AvaloniaProperty.Register<NavChartView, double>(nameof(YMax), 1);

    public static readonly StyledProperty<string> LineColorProperty =
        AvaloniaProperty.Register<NavChartView, string>(nameof(LineColor), "#4080FF");

    public ObservableCollection<NavPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }
    public double YMin { get => GetValue(YMinProperty); set => SetValue(YMinProperty, value); }
    public double YMax { get => GetValue(YMaxProperty); set => SetValue(YMaxProperty, value); }
    public string LineColor { get => GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    // ── 缩放 / 平移状态 ──────────────────────────────────────────────────
    private double _viewStart = 0.0;   // 视口起始比例 [0,1]
    private double _viewEnd = 1.0;     // 视口终止比例 [0,1]
    private bool _isDragging = false;
    private double _dragStartX = 0;
    private double _dragViewStart = 0;

    // ── 悬停十字线 ────────────────────────────────────────────────────────
    private bool _showCrosshair = false;
    private double _crosshairX = 0;
    private int _hoverIndex = -1;

    // ── 布局常量 ─────────────────────────────────────────────────────────
    private const double PadLeft = 52;   // Y 轴标签区
    private const double PadRight = 8;
    private const double PadTop = 12;
    private const double PadBottom = 28;   // X 轴标签区

    // ── 属性变化监听 ─────────────────────────────────────────────────────
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PointsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCol)
                oldCol.CollectionChanged -= OnCollectionChanged;
            if (change.NewValue is INotifyCollectionChanged newCol)
                newCol.CollectionChanged += OnCollectionChanged;

            // 数据换了，重置视口
            _viewStart = 0.0;
            _viewEnd = 1.0;
            _showCrosshair = false;
            InvalidateVisual();
        }
        else if (change.Property == YMinProperty ||
                 change.Property == YMaxProperty ||
                 change.Property == LineColorProperty)
        {
            InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _viewStart = 0.0; _viewEnd = 1.0;
        InvalidateVisual();
    }

    // ── 鼠标事件 ─────────────────────────────────────────────────────────
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isDragging)
        {
            var pts = Points;
            if (pts == null || pts.Count < 2) return;

            double chartW = Bounds.Width - PadLeft - PadRight;
            double dx = (pos.X - _dragStartX) / chartW;
            double span = _viewEnd - _viewStart;

            double newStart = Math.Clamp(_dragViewStart - dx, 0, 1 - span);
            _viewStart = newStart;
            _viewEnd = newStart + span;
            InvalidateVisual();
        }
        else
        {
            double chartX = pos.X - PadLeft;
            double chartW = Bounds.Width - PadLeft - PadRight;
            if (chartX >= 0 && chartX <= chartW)
            {
                _showCrosshair = true;
                _crosshairX = pos.X;
                _hoverIndex = HitTestIndex(chartX, chartW);
            }
            else
            {
                _showCrosshair = false;
                _hoverIndex = -1;
            }
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _showCrosshair = false;
        _hoverIndex = -1;
        _isDragging = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        if (pos.X >= PadLeft)
        {
            _isDragging = true;
            _dragStartX = pos.X;
            _dragViewStart = _viewStart;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pts = Points;
        if (pts == null || pts.Count < 2) return;

        // 以鼠标位置为中心缩放
        double chartW = Bounds.Width - PadLeft - PadRight;
        double mouseRX = Math.Clamp((e.GetPosition(this).X - PadLeft) / chartW, 0, 1);
        double pivot = _viewStart + mouseRX * (_viewEnd - _viewStart);

        double factor = e.Delta.Y > 0 ? 0.80 : 1.25;
        double span = (_viewEnd - _viewStart) * factor;
        span = Math.Clamp(span, 2.0 / pts.Count, 1.0);  // 最少显示2个点，最多全部

        double newStart = Math.Clamp(pivot - mouseRX * span, 0, 1 - span);
        _viewStart = newStart;
        _viewEnd = newStart + span;
        InvalidateVisual();
        e.Handled = true;
    }

    // ── 命中检测：找最近的数据点索引 ────────────────────────────────────
    private int HitTestIndex(double chartX, double chartW)
    {
        var pts = Points;
        if (pts == null || pts.Count == 0) return -1;

        var (i0, i1) = ViewportIndices(pts.Count);
        int count = i1 - i0 + 1;
        if (count < 1) return -1;

        double ratio = chartX / chartW;
        int idx = i0 + (int)Math.Round(ratio * (count - 1));
        return Math.Clamp(idx, i0, i1);
    }

    // ── 视口起止下标 ─────────────────────────────────────────────────────
    private (int i0, int i1) ViewportIndices(int total)
    {
        int i0 = (int)Math.Floor(_viewStart * (total - 1));
        int i1 = (int)Math.Ceiling(_viewEnd * (total - 1));
        i0 = Math.Clamp(i0, 0, total - 1);
        i1 = Math.Clamp(i1, i0, total - 1);
        return (i0, i1);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Render
    // ═══════════════════════════════════════════════════════════════════
    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var pts = Points;
        if (pts == null || pts.Count < 2) return;

        // ── 布局 ───────────────────────────────────────────────────────
        double chartW = w - PadLeft - PadRight;
        double chartH = h - PadTop - PadBottom;
        if (chartW <= 0 || chartH <= 0) return;

        // ── 视口下标 ───────────────────────────────────────────────────
        var (i0, i1) = ViewportIndices(pts.Count);
        int count = i1 - i0 + 1;
        if (count < 2) return;

        // ── Y 轴范围（基于当前视口数据动态计算，更紧凑）─────────────────
        double vMin = double.MaxValue, vMax = double.MinValue;
        for (int i = i0; i <= i1; i++)
        {
            if (pts[i].Nav < vMin) vMin = pts[i].Nav;
            if (pts[i].Nav > vMax) vMax = pts[i].Nav;
        }
        // 允许局部 Y 收紧，但不超出全局 [YMin, YMax]
        double pad = (vMax - vMin) * 0.12;
        if (pad < 0.001) pad = 0.001;
        double yMin = Math.Max(YMin, vMin - pad);
        double yMax = Math.Min(YMax, vMax + pad);
        double yRange = yMax - yMin;
        if (yRange <= 0) yRange = 1;

        // ── 坐标映射 ───────────────────────────────────────────────────
        double PX(int globalIdx) =>
            PadLeft + (globalIdx - i0) / (double)(count - 1) * chartW;

        double PY(double nav) =>
            PadTop + (1.0 - (nav - yMin) / yRange) * chartH;

        // ── 颜色 ───────────────────────────────────────────────────────
        Color lineCol = TryParseColor(LineColor) ?? Color.Parse("#4080FF");
        Color gridCol = Color.Parse("#E8E8F0");
        Color labelCol = Color.Parse("#AAAAAA");

        var linePen = new Pen(new SolidColorBrush(lineCol), 2.2);
        var gridPen = new Pen(new SolidColorBrush(gridCol), 0.6) { DashStyle = DashStyle.Dash };
        var gridSolid = new Pen(new SolidColorBrush(gridCol), 0.6);

        // ── 布局矩形 ──────────────────────────────────────────────────
        var chartRect = new Rect(PadLeft, PadTop, chartW, chartH);
        var tf = new Typeface("Microsoft YaHei,PingFang SC,sans-serif");
        for (int g = 0; g <= 4; g++)
        {
            double ratio = g / 4.0;
            double navVal = yMin + ratio * yRange;
            double y = PadTop + (1.0 - ratio) * chartH;

            // 网格线（在图表区内）
            ctx.DrawLine(g == 0 ? gridSolid : gridPen,
                new Point(PadLeft, y), new Point(w - PadRight, y));

            // Y 轴标签
            var ft = new FormattedText(navVal.ToString("F4"),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, 10,
                new SolidColorBrush(labelCol));
            ctx.DrawText(ft, new Point(2, y - ft.Height / 2));
        }

        // ── X 轴日期标签 ──────────────────────────────────────────────
        int labelStep = Math.Max(1, count / 5);
        for (int i = i0; i <= i1; i += labelStep)
        {
            double x = PX(i);
            string lb = pts[i].Date.ToString("MM/dd");
            var ft = new FormattedText(lb,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, 10,
                new SolidColorBrush(labelCol));
            ctx.DrawText(ft, new Point(x - ft.Width / 2, PadTop + chartH + 4));
        }

        // ── 图表主体（clip 防止曲线溢出）─────────────────────────────
        using (ctx.PushClip(chartRect))
        {
            // 渐变填充
            var fillGeom = new StreamGeometry();
            using (var sgc = fillGeom.Open())
            {
                sgc.BeginFigure(new Point(PX(i0), PadTop + chartH), true);
                sgc.LineTo(new Point(PX(i0), PY(pts[i0].Nav)));
                // 贝塞尔平滑
                for (int i = i0 + 1; i <= i1; i++)
                {
                    var (cp1, cp2) = BezierControlPoints(pts, i0, i, PX, PY);
                    sgc.CubicBezierTo(cp1, cp2, new Point(PX(i), PY(pts[i].Nav)));
                }
                sgc.LineTo(new Point(PX(i1), PadTop + chartH));
                sgc.EndFigure(true);
            }
            // 渐变：线色 → 透明
            var grad = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(60, lineCol.R, lineCol.G, lineCol.B), 0),
                    new GradientStop(Color.FromArgb(5,  lineCol.R, lineCol.G, lineCol.B), 1),
                }
            };
            ctx.DrawGeometry(grad, null, fillGeom);

            // ── 贝塞尔折线 ────────────────────────────────────────────────
            var lineGeom = new StreamGeometry();
            using (var sgc = lineGeom.Open())
            {
                sgc.BeginFigure(new Point(PX(i0), PY(pts[i0].Nav)), false);
                for (int i = i0 + 1; i <= i1; i++)
                {
                    var (cp1, cp2) = BezierControlPoints(pts, i0, i, PX, PY);
                    sgc.CubicBezierTo(cp1, cp2, new Point(PX(i), PY(pts[i].Nav)));
                }
            }
            ctx.DrawGeometry(null, linePen, lineGeom);

            // ── 数据点圆圈（≤ 60 个时显示）────────────────────────────────
            if (count <= 60)
            {
                var markerFill = new SolidColorBrush(lineCol);
                var whiteFill = new SolidColorBrush(Colors.White);
                for (int i = i0; i <= i1; i++)
                {
                    var pt = new Point(PX(i), PY(pts[i].Nav));
                    ctx.DrawEllipse(markerFill, null, pt, 3.5, 3.5);
                    ctx.DrawEllipse(whiteFill, null, pt, 2.0, 2.0);
                }
            }

            // ── 十字线（竖线 + 高亮圆，画在 clip 内）────────────────────
            if (_showCrosshair && _hoverIndex >= i0 && _hoverIndex <= i1)
            {
                int idx = _hoverIndex;
                double cx = PX(idx);
                double cy = PY(pts[idx].Nav);

                ctx.DrawLine(
                    new Pen(new SolidColorBrush(
                        Color.FromArgb(100, lineCol.R, lineCol.G, lineCol.B)), 1)
                    { DashStyle = DashStyle.Dash },
                    new Point(cx, PadTop),
                    new Point(cx, PadTop + chartH));

                ctx.DrawEllipse(new SolidColorBrush(lineCol), null, new Point(cx, cy), 6, 6);
                ctx.DrawEllipse(new SolidColorBrush(Colors.White), null, new Point(cx, cy), 3.5, 3.5);
            }
        } // ← using 结束，自动 Pop clip

        // ── 气泡提示（clip 外，可显示在图表顶部边沿）─────────────────────
        if (_showCrosshair && _hoverIndex >= i0 && _hoverIndex <= i1)
        {
            int idx = _hoverIndex;
            double cx = PX(idx);
            string label = $"{pts[idx].Date:yyyy/MM/dd}  净值 {pts[idx].Nav:F4}";
            var labelFt = new FormattedText(label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, 12,
                new SolidColorBrush(Colors.White));

            double bw = labelFt.Width + 20;
            double bh = labelFt.Height + 12;
            double bx = Math.Clamp(cx - bw / 2, PadLeft, w - PadRight - bw);
            double by = PadTop - bh - 6;
            if (by < 2) by = PadTop + 8;

            var bubbleRect = new RoundedRect(new Rect(bx, by, bw, bh), 6);
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(220, 30, 30, 50)), null, bubbleRect);
            ctx.DrawText(labelFt, new Point(bx + 10, by + 6));
        }
    }

    // ── Catmull-Rom → 贝塞尔控制点 ──────────────────────────────────────
    private static (Point cp1, Point cp2) BezierControlPoints(
        IList<NavPoint> pts, int i0, int i,
        Func<int, double> PX, Func<double, double> PY)
    {
        int prev = Math.Max(i0, i - 1);
        int next = Math.Min(pts.Count - 1, i + 1);
        double tension = 0.4;

        double cp1x = PX(prev) + (PX(i) - PX(Math.Max(i0, i - 2))) * tension;
        double cp1y = PY(pts[prev].Nav) + (PY(pts[i].Nav) - PY(pts[Math.Max(i0, i - 2)].Nav)) * tension;

        double cp2x = PX(i) - (PX(next) - PX(prev)) * tension;
        double cp2y = PY(pts[i].Nav) - (PY(pts[next].Nav) - PY(pts[prev].Nav)) * tension;

        return (new Point(cp1x, cp1y), new Point(cp2x, cp2y));
    }

    // ── 工具方法 ─────────────────────────────────────────────────────────
    private static Color? TryParseColor(string? hex)
    {
        try { return Color.Parse(hex ?? ""); }
        catch { return null; }
    }
}
