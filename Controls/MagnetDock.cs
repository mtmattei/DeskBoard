using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeskBoard.Rendering;

namespace DeskBoard.Controls;

public enum TrayTool { Select, Marker, Eraser, Text }

/// <summary>
/// The tool set as bold 3D fridge magnets stuck to the board: click activates,
/// drag moves them anywhere (positions persist). The canvas has no background, so
/// everything between magnets stays click-through to the board. Raises intent
/// events only — behavior lives in the window.
/// </summary>
public sealed class MagnetDock : Canvas
{
    public event Action<Color>? MarkerPicked;
    public event Action<Color>? CustomColorRequested; // right-click a marker magnet
    public event Action? EraserPicked;
    public event Action? SelectPicked;
    public event Action? TextPicked;
    public event Action? NoteRequested;
    public event Action? ReminderRequested;
    public event Action? ImageRequested;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? ClearRequested;
    public event Action? HideRequested;
    public event Action<Dictionary<string, double[]>>? PositionsChanged;

    public static readonly Color[] MarkerColors =
    {
        Color.FromRgb(0x1A, 0x1A, 0x1A),
        Color.FromRgb(0xE5, 0x39, 0x35),
        Color.FromRgb(0x1E, 0x88, 0xE5),
        Color.FromRgb(0x43, 0xA0, 0x47),
    };

    private static readonly Color Graphite = Color.FromRgb(0x3E, 0x46, 0x50);

    private sealed class Magnet
    {
        public required string Id;
        public required Grid Root;
        public required Grid Body;
        public required ScaleTransform Scale;
        public required TranslateTransform Lift;
        public required Ellipse Shadow;
        public required Ellipse Ring;
        public required double Size;
        public Action? Click;
        public bool Disabled;
        public Color? MarkerColor;
        public bool Engaged;
    }

    private readonly List<Magnet> _magnets = new();
    private Dictionary<string, double[]>? _savedPositions;
    private bool _laidOut;

    // Drag state
    private Magnet? _dragging;
    private Point _pressPoint;
    private Point _dragOrigin;
    private bool _moved;

    public MagnetDock()
    {
        Background = null; // empty space stays click-through

        for (int i = 0; i < MarkerColors.Length; i++)
        {
            var c = MarkerColors[i];
            AddMagnet($"marker{i}", 54, c, "\uE70F", Colors.White, ToolTipFor(c),
                () => MarkerPicked?.Invoke(c), markerColor: c);
        }
        AddMagnet("eraser", 50, Color.FromRgb(0xEC, 0xEE, 0xF1), "\uE75C",
            Graphite, "Eraser (E)", () => EraserPicked?.Invoke());

        AddMagnet("select", 46, Graphite, "\uE7C9", Colors.White,
            "Select & move (V)", () => SelectPicked?.Invoke());
        AddMagnet("text", 46, Graphite, "\uE8D2", Colors.White,
            "Marker text — click the board to place (T)", () => TextPicked?.Invoke());
        AddMagnet("note", 46, Color.FromRgb(0xFF, 0xD3, 0x4D), "\uE70B",
            Color.FromRgb(0x4A, 0x42, 0x30), "Add a sticky note (N)", () => NoteRequested?.Invoke());
        AddMagnet("reminder", 46, Color.FromRgb(0xE0, 0x5A, 0x33), "\uE787", Colors.White,
            "Add a reminder (R)", () => ReminderRequested?.Invoke());
        AddMagnet("image", 46, Graphite, "\uE8B9", Colors.White,
            "Pin an image… (or paste / drop one)", () => ImageRequested?.Invoke());

        AddMagnet("undo", 42, Graphite, "\uE7A7", Colors.White, "Undo (Ctrl+Z)", () => UndoRequested?.Invoke());
        AddMagnet("redo", 42, Graphite, "\uE7A6", Colors.White, "Redo (Ctrl+Y)", () => RedoRequested?.Invoke());
        AddMagnet("clear", 42, Color.FromRgb(0xB0, 0x45, 0x3D), "\uE74D", Colors.White,
            "Clear the board", () => ClearRequested?.Invoke());
        AddMagnet("hide", 42, Graphite, "\uE70D", Colors.White,
            "Hide the board (Ctrl+Alt+D)", () => HideRequested?.Invoke());

        SizeChanged += (_, _) => EnsureLayout();
    }

    private static string ToolTipFor(Color c) =>
        c == MarkerColors[0] ? "Black marker (B)" : "Marker — right-click for a custom color";

    // ---- Building ----

    private void AddMagnet(string id, double size, Color dome, string glyph, Color glyphColor,
        string tooltip, Action click, Color? markerColor = null)
    {
        int index = _magnets.Count;
        double rotation = ((index * 53) % 11) - 5; // deterministic fridge-magnet jitter

        var scale = new ScaleTransform(1, 1);
        var lift = new TranslateTransform();

        var shadow = new Ellipse
        {
            Width = size * 0.86, Height = size * 0.2,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            Fill = RadialShadow(0x40),
        };

        var body = new Grid
        {
            Width = size, Height = size,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, lift } },
        };

        // Dome: hot highlight top-left, saturated core, dark rim.
        body.Children.Add(new Ellipse
        {
            Fill = DomeBrush(dome),
            Stroke = new SolidColorBrush(Color.FromArgb(0x66,
                (byte)(dome.R * 0.4), (byte)(dome.G * 0.4), (byte)(dome.B * 0.4))),
            StrokeThickness = 1,
        });

        // Specular streak.
        body.Children.Add(new Ellipse
        {
            Width = size * 0.36, Height = size * 0.18,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(size * 0.17, size * 0.14, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-18),
            IsHitTestVisible = false,
            Fill = SpecularBrush(),
        });

        // Active ring.
        var ring = new Ellipse
        {
            Margin = new Thickness(3.5),
            Stroke = new SolidColorBrush(Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 2,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        body.Children.Add(ring);

        body.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = size * 0.36,
            Foreground = new SolidColorBrush(glyphColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(rotation),
        });

        var root = new Grid
        {
            Width = size,
            Height = size + 7, // room for the contact shadow under the dome
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = tooltip,
        };
        root.Children.Add(shadow);
        root.Children.Add(body);

        var magnet = new Magnet
        {
            Id = id, Root = root, Body = body, Scale = scale, Lift = lift,
            Shadow = shadow, Ring = ring, Size = size, Click = click, MarkerColor = markerColor,
        };
        _magnets.Add(magnet);
        Children.Add(root);

        root.MouseLeftButtonDown += (_, e) => OnMagnetDown(magnet, e);
        root.MouseMove += (_, e) => OnMagnetMove(magnet, e);
        root.MouseLeftButtonUp += (_, e) => OnMagnetUp(magnet, e);
        root.MouseEnter += (_, _) => { if (!magnet.Disabled && !magnet.Engaged) SetLift(magnet, hover: true); };
        root.MouseLeave += (_, _) => { if (!magnet.Disabled && !magnet.Engaged) SetLift(magnet, hover: false); };
        if (markerColor is not null)
            root.MouseRightButtonUp += (_, e) => { CustomColorRequested?.Invoke(markerColor.Value); e.Handled = true; };
    }

    // ---- Interaction ----

    private void OnMagnetDown(Magnet m, MouseButtonEventArgs e)
    {
        if (m.Disabled) { e.Handled = true; return; }
        _dragging = m;
        _moved = false;
        _pressPoint = e.GetPosition(this);
        _dragOrigin = new Point(GetLeft(m.Root), GetTop(m.Root));
        Motion.Animate(m.Scale, ScaleTransform.ScaleXProperty, 0.94, Motion.Fast);
        Motion.Animate(m.Scale, ScaleTransform.ScaleYProperty, 0.94, Motion.Fast);
        m.Root.CaptureMouse();
        e.Handled = true;
    }

    private void OnMagnetMove(Magnet m, MouseEventArgs e)
    {
        if (_dragging != m || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        var delta = pos - _pressPoint;
        if (!_moved && delta.Length < 4) return;
        _moved = true;

        double left = Math.Clamp(_dragOrigin.X + delta.X, 8, Math.Max(8, ActualWidth - m.Root.Width - 8));
        double top = Math.Clamp(_dragOrigin.Y + delta.Y, 8, Math.Max(8, ActualHeight - m.Root.Height - 8));
        SetLeft(m.Root, left);
        SetTop(m.Root, top);
        e.Handled = true;
    }

    private void OnMagnetUp(Magnet m, MouseButtonEventArgs e)
    {
        if (_dragging != m) return;
        _dragging = null;
        m.Root.ReleaseMouseCapture();

        double restScale = m.Engaged ? 1.08 : 1.0;
        Motion.Animate(m.Scale, ScaleTransform.ScaleXProperty, restScale, Motion.Fast);
        Motion.Animate(m.Scale, ScaleTransform.ScaleYProperty, restScale, Motion.Fast);

        if (_moved) PositionsChanged?.Invoke(GetPositions());
        else m.Click?.Invoke();
        e.Handled = true;
    }

    private static void SetLift(Magnet m, bool hover)
    {
        Motion.Animate(m.Lift, TranslateTransform.YProperty, hover ? -2 : 0, Motion.Fast);
        Motion.Animate(m.Shadow, OpacityProperty, hover ? 0.7 : 1.0, Motion.Fast);
    }

    // ---- State from the window ----

    public void SetActiveTool(TrayTool tool, Color? markerColor = null)
    {
        foreach (var m in _magnets)
        {
            bool engaged = m.Id switch
            {
                "eraser" => tool == TrayTool.Eraser,
                "select" => tool == TrayTool.Select,
                "text" => tool == TrayTool.Text,
                _ when m.MarkerColor is not null =>
                    tool == TrayTool.Marker && markerColor.HasValue && m.MarkerColor.Value == markerColor.Value,
                _ => false,
            };
            if (m.Engaged == engaged) continue;
            m.Engaged = engaged;

            double s = engaged ? 1.08 : 1.0;
            Motion.Animate(m.Scale, ScaleTransform.ScaleXProperty, s, Motion.Normal);
            Motion.Animate(m.Scale, ScaleTransform.ScaleYProperty, s, Motion.Normal);
            Motion.Animate(m.Ring, OpacityProperty, engaged ? 1 : 0, Motion.Normal);
            Motion.Animate(m.Lift, TranslateTransform.YProperty, 0, Motion.Fast);
            Motion.Animate(m.Shadow, OpacityProperty, engaged ? 0.85 : 1.0, Motion.Fast);
        }
    }

    public void SetUndoRedo(bool canUndo, bool canRedo)
    {
        SetDisabled("undo", !canUndo);
        SetDisabled("redo", !canRedo);
    }

    private void SetDisabled(string id, bool disabled)
    {
        var m = _magnets.First(x => x.Id == id);
        if (m.Disabled == disabled) return;
        m.Disabled = disabled;
        Motion.Animate(m.Root, OpacityProperty, disabled ? 0.4 : 1.0, Motion.Fast);
        m.Root.Cursor = disabled ? Cursors.Arrow : Cursors.Hand;
    }

    // ---- Layout & persistence ----

    public void ApplyPositions(Dictionary<string, double[]>? saved)
    {
        _savedPositions = saved;
        _laidOut = false;
        EnsureLayout();
    }

    public Dictionary<string, double[]> GetPositions() =>
        _magnets.ToDictionary(m => m.Id, m => new[] { GetLeft(m.Root), GetTop(m.Root) });

    private void EnsureLayout()
    {
        if (_laidOut || ActualWidth < 200 || ActualHeight < 200) return;
        _laidOut = true;

        // Saved positions win; anything unknown falls back to the default row.
        double totalWidth = _magnets.Sum(m => m.Root.Width + 14)
            + 18 * 2; // extra breathing room between the three groups
        double x = (ActualWidth - totalWidth) / 2;
        double baseY = ActualHeight - 128;

        for (int i = 0; i < _magnets.Count; i++)
        {
            var m = _magnets[i];
            if (i == 5 || i == 10) x += 18; // group gaps after eraser and image

            double jitterY = ((i * 37) % 13) - 6;
            double left = x;
            double top = baseY - m.Root.Height / 2 + jitterY;

            if (_savedPositions is not null && _savedPositions.TryGetValue(m.Id, out var p) && p.Length == 2)
            {
                left = Math.Clamp(p[0], 8, Math.Max(8, ActualWidth - m.Root.Width - 8));
                top = Math.Clamp(p[1], 8, Math.Max(8, ActualHeight - m.Root.Height - 8));
            }

            SetLeft(m.Root, left);
            SetTop(m.Root, top);
            x += m.Root.Width + 14;
        }
    }

    // ---- Brushes ----

    private static Brush DomeBrush(Color c)
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.32, 0.28),
            Center = new Point(0.42, 0.38),
            RadiusX = 0.78,
            RadiusY = 0.78,
            GradientStops =
            {
                new GradientStop(Lighten(c, 0.55), 0.0),
                new GradientStop(Lighten(c, 0.12), 0.38),
                new GradientStop(c, 0.72),
                new GradientStop(Darken(c, 0.52), 1.0),
            },
        };
        b.Freeze();
        return b;
    }

    private static Brush SpecularBrush()
    {
        var b = new LinearGradientBrush(
            Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90);
        b.Freeze();
        return b;
    }

    private static Brush RadialShadow(byte alpha)
    {
        var b = new RadialGradientBrush(
            Color.FromArgb(alpha, 0x20, 0x24, 0x28), Color.FromArgb(0x00, 0x20, 0x24, 0x28));
        b.Freeze();
        return b;
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
}
