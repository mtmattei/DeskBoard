using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using MediaColor = System.Windows.Media.Color;

namespace DeskBoard;

public enum ShapeKind { Arrow, Rect, Ellipse }

/// <summary>
/// Doodle shapes: drag on the board to place an arrow, rectangle, or ellipse drawn
/// as a single wobbly hand-drawn ink stroke in the current marker color. Because the
/// result is a regular Stroke, erase, undo, and ISF persistence all apply for free.
/// </summary>
public partial class MainWindow
{
    private ShapeKind _shapeKind = ShapeKind.Arrow;
    private bool _shapeDragging;
    private Point _shapeStart;
    private System.Windows.Shapes.Shape? _shapePreview;
    private readonly Random _doodleRng = new();

    private void WireShapeInput()
    {
        Ink.PreviewMouseLeftButtonDown += Shape_Down;
        Ink.PreviewMouseMove += Shape_Move;
        Ink.PreviewMouseLeftButtonUp += Shape_Up;
    }

    private void SetShapeTool(ShapeKind kind)
    {
        _shapeKind = kind;
        SetTool(Tool.Shape);
    }

    private void Shape_Down(object sender, MouseButtonEventArgs e)
    {
        if (_tool != Tool.Shape) return;
        _shapeDragging = true;
        _shapeStart = e.GetPosition(Ink);

        _shapePreview = _shapeKind switch
        {
            ShapeKind.Rect => new Rectangle(),
            ShapeKind.Ellipse => new Ellipse(),
            _ => new Line(),
        };
        _shapePreview.Stroke = new SolidColorBrush(MediaColor.FromArgb(0x99, _inkColor.R, _inkColor.G, _inkColor.B));
        _shapePreview.StrokeThickness = 2;
        _shapePreview.StrokeDashArray = new DoubleCollection { 4, 4 };
        _shapePreview.IsHitTestVisible = false;
        SelectionLayer.Children.Add(_shapePreview);

        Ink.CaptureMouse();
        e.Handled = true;
    }

    private void Shape_Move(object sender, MouseEventArgs e)
    {
        if (!_shapeDragging || _shapePreview is null) return;
        Point pos = Constrain(e.GetPosition(Ink));

        if (_shapePreview is Line line)
        {
            line.X1 = _shapeStart.X; line.Y1 = _shapeStart.Y;
            line.X2 = pos.X; line.Y2 = pos.Y;
        }
        else
        {
            var rect = new Rect(_shapeStart, pos);
            Canvas.SetLeft(_shapePreview, rect.X);
            Canvas.SetTop(_shapePreview, rect.Y);
            _shapePreview.Width = rect.Width;
            _shapePreview.Height = rect.Height;
        }
        e.Handled = true;
    }

    private void Shape_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_shapeDragging) return;
        _shapeDragging = false;
        Ink.ReleaseMouseCapture();

        if (_shapePreview is not null) SelectionLayer.Children.Remove(_shapePreview);
        _shapePreview = null;

        Point end = Constrain(e.GetPosition(Ink));
        if ((end - _shapeStart).Length < 8) return; // a click, not a drag

        var points = _shapeKind switch
        {
            ShapeKind.Rect => DoodleRect(_shapeStart, end),
            ShapeKind.Ellipse => DoodleEllipse(_shapeStart, end),
            _ => DoodleArrow(_shapeStart, end),
        };

        var da = Ink.DefaultDrawingAttributes.Clone();
        da.FitToCurve = false; // keep the wobble — that's the doodle
        da.IgnorePressure = true;
        var spc = new StylusPointCollection();
        foreach (var p in points) spc.Add(new StylusPoint(p.X, p.Y, 0.55f));
        Ink.Strokes.Add(new Stroke(spc, da)); // StrokesChanged wires undo + save
        e.Handled = true;
    }

    /// <summary>Shift constrains: square/circle bounds, 45°-snapped arrows.</summary>
    private Point Constrain(Point pos)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return pos;

        var d = pos - _shapeStart;
        if (_shapeKind == ShapeKind.Arrow)
        {
            double angle = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4);
            double len = d.Length;
            return new Point(_shapeStart.X + Math.Cos(angle) * len, _shapeStart.Y + Math.Sin(angle) * len);
        }
        double side = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
        return new Point(_shapeStart.X + Math.Sign(d.X) * side, _shapeStart.Y + Math.Sign(d.Y) * side);
    }

    // ---- Doodle generators: anchored ends, low-frequency wobble, human overshoot ----

    private double Wobble(double t, double amp, double phase) =>
        (Math.Sin(t * Math.PI * 2 * 1.7 + phase) * 0.6
         + Math.Sin(t * Math.PI * 2 * 3.3 + phase * 2.1) * 0.4) * amp
        + (_doodleRng.NextDouble() - 0.5) * amp * 0.35;

    private List<Point> SegmentWithWobble(Point a, Point b, double amp, bool anchorEnds)
    {
        var d = b - a;
        double len = d.Length;
        if (len < 1) return new List<Point> { a, b };
        var perp = new Vector(-d.Y / len, d.X / len);
        double phase = _doodleRng.NextDouble() * Math.PI * 2;

        int n = Math.Clamp((int)(len / 7), 8, 80);
        var pts = new List<Point>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            double taper = anchorEnds ? Math.Pow(Math.Sin(t * Math.PI), 0.6) : 1.0;
            double w = Wobble(t, amp, phase) * taper;
            pts.Add(a + d * t + perp * w);
        }
        return pts;
    }

    private List<Point> DoodleArrow(Point start, Point end)
    {
        var d = end - start;
        double len = d.Length;
        var dir = d / len;
        double amp = Math.Clamp(len * 0.015, 1.2, 4.0);

        var pts = SegmentWithWobble(start, end, amp, anchorEnds: true);

        // Barbs: drawn without lifting the pen — out, back to the tip, out again.
        double barbLen = Math.Clamp(len * 0.18, 13, 32);
        double theta = Math.Atan2(dir.Y, dir.X);
        foreach (double sign in new[] { 1.0, -1.0 })
        {
            double a = theta + Math.PI + sign * 0.46; // ~26° off the reversed shaft
            var barbEnd = new Point(end.X + Math.Cos(a) * barbLen, end.Y + Math.Sin(a) * barbLen);
            pts.AddRange(SegmentWithWobble(end, barbEnd, amp * 0.5, anchorEnds: false));
            if (sign > 0) // return to the tip before the second barb
                pts.AddRange(SegmentWithWobble(barbEnd, end, amp * 0.5, anchorEnds: false));
        }
        return pts;
    }

    private List<Point> DoodleRect(Point p1, Point p2)
    {
        var rect = new Rect(p1, p2);
        double amp = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.02, 1.0, 3.5);

        // Slightly jittered corners so no edge is perfectly aligned.
        Point J(double x, double y) => new(
            x + (_doodleRng.NextDouble() - 0.5) * 5,
            y + (_doodleRng.NextDouble() - 0.5) * 5);
        var c0 = J(rect.Left, rect.Top);
        var c1 = J(rect.Right, rect.Top);
        var c2 = J(rect.Right, rect.Bottom);
        var c3 = J(rect.Left, rect.Bottom);

        var pts = new List<Point>();
        pts.AddRange(SegmentWithWobble(c0, c1, amp, anchorEnds: false));
        pts.AddRange(SegmentWithWobble(c1, c2, amp, anchorEnds: false));
        pts.AddRange(SegmentWithWobble(c2, c3, amp, anchorEnds: false));
        pts.AddRange(SegmentWithWobble(c3, c0, amp, anchorEnds: false));
        // Overshoot past the starting corner, like a real closing stroke.
        var overshoot = c0 + (c1 - c0) * Math.Min(0.12, 10 / Math.Max(1, (c1 - c0).Length));
        pts.AddRange(SegmentWithWobble(c0, overshoot, amp * 0.6, anchorEnds: false));
        return pts;
    }

    private List<Point> DoodleEllipse(Point p1, Point p2)
    {
        var rect = new Rect(p1, p2);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        double rx = Math.Max(6, rect.Width / 2), ry = Math.Max(6, rect.Height / 2);
        double phase = _doodleRng.NextDouble() * Math.PI * 2;
        double startAngle = _doodleRng.NextDouble() * Math.PI * 2;

        int n = Math.Clamp((int)((rx + ry) * Math.PI / 8), 24, 120);
        var pts = new List<Point>(n + 1);
        double sweep = Math.PI * 2 + 0.55; // overlap the join, doodle-style
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            double a = startAngle + t * sweep;
            double w = 1 + (Math.Sin(a * 3 + phase) * 0.014 + Math.Sin(a * 7 + phase * 2.3) * 0.010)
                       + (_doodleRng.NextDouble() - 0.5) * 0.008;
            pts.Add(new Point(center.X + Math.Cos(a) * rx * w, center.Y + Math.Sin(a) * ry * w));
        }
        return pts;
    }
}
