using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskBoard.Board;

/// <summary>The host surface an item talks back to (implemented by MainWindow).</summary>
internal interface IBoardHost
{
    bool IsSelectToolActive { get; }
    Panel ItemsHost { get; }
    void ItemPressed(BoardItemView item, bool additive);
    void ItemDragDelta(Vector delta);
    void ItemTransformStart();
    void ItemTransformEnd(string editName);
    void ItemContentChanged(BoardItemView item);
    void ItemOpenRequested(BoardItemView item);
    void ItemDatePickRequested(BoardItemView item);
    void ShowItemMenu(BoardItemView item);
}

/// <summary>
/// Interactive container for one board object: hosts the kind-specific content, the
/// attachment decor, and the full selection/manipulation chrome (hover ring, selection
/// ring, corner resize handles, rotate handle, pin state). Move deltas route through
/// the host so multi-selection and groups move as one.
/// </summary>
internal sealed class BoardItemView : Grid
{
    private enum Gesture { None, Move, Resize, Rotate }

    public BoardItemModel Model { get; }

    private readonly IBoardHost _host;
    private readonly ItemContent _content;
    private readonly RotateTransform _rotate;
    private readonly Border _hoverRing;
    private readonly Border _selRing;
    private readonly Border[] _handles = new Border[4];
    private readonly FrameworkElement _rotateHandle;
    private readonly Rectangle _rotateStem;
    private readonly FrameworkElement _pin;

    private Gesture _gesture;
    private bool _editing;
    private Point _gestureStartCanvas;
    private Point _lastDragCanvas;
    private double _startRotation;
    private Rect _startBounds;
    private int _resizeDx, _resizeDy;      // dragged-corner signs in item space
    private double _rotatePointerOffset;   // pointer angle minus rotation at gesture start

    private static readonly (HorizontalAlignment H, VerticalAlignment V, int Dx, int Dy)[]
        HandleSpec =
        {
            (HorizontalAlignment.Left,  VerticalAlignment.Top,    -1, -1),
            (HorizontalAlignment.Right, VerticalAlignment.Top,     1, -1),
            (HorizontalAlignment.Left,  VerticalAlignment.Bottom, -1,  1),
            (HorizontalAlignment.Right, VerticalAlignment.Bottom,  1,  1),
        };

    public bool IsSelected { get; private set; }
    public bool IsEditing => _editing;
    public TextBox? Editor => _content.Editor;

    public BoardItemView(BoardItemModel model, IBoardHost host)
    {
        Model = model;
        _host = host;
        _content = BoardItemContent.Build(model);

        Width = Math.Max(model.W, _content.MinWidth);
        Height = Math.Max(model.H, _content.MinHeight);
        Model.W = Width;
        Model.H = Height;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotate = new RotateTransform(model.Rotation);
        Background = Brushes.Transparent; // whole bounds hit-testable
        Cursor = Cursors.SizeAll;
        ClipToBounds = false;

        Children.Add(_content.Root);
        if (_content.Decor is not null) Children.Add(_content.Decor);

        var accent = Color.FromRgb(0x2E, 0x6F, 0xE0);

        _hoverRing = MakeRing(Color.FromArgb(0x4D, accent.R, accent.G, accent.B), 1);
        _selRing = MakeRing(Color.FromArgb(0xCC, accent.R, accent.G, accent.B), 1);
        Children.Add(_hoverRing);
        Children.Add(_selRing);

        for (int i = 0; i < 4; i++)
        {
            var spec = HandleSpec[i];
            var h = new Border
            {
                Width = 10, Height = 10,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = spec.H,
                VerticalAlignment = spec.V,
                Margin = new Thickness(-5),
                Visibility = Visibility.Collapsed,
                Cursor = (spec.Dx * spec.Dy) > 0 ? Cursors.SizeNWSE : Cursors.SizeNESW,
                Tag = i,
            };
            _handles[i] = h;
            Children.Add(h);
        }

        _rotateStem = new Rectangle
        {
            Width = 1.2, Height = 18,
            Fill = new SolidColorBrush(Color.FromArgb(0x88, accent.R, accent.G, accent.B)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -18, 0, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Children.Add(_rotateStem);

        _rotateHandle = new Ellipse
        {
            Width = 12, Height = 12,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B)),
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -28, 0, 0),
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
        };
        Children.Add(_rotateHandle);

        _pin = BoardItemContent.BuildPushPin();
        _pin.Visibility = model.Pinned ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_pin);

        if (_content.Editor is TextBox editor)
        {
            editor.TextChanged += (_, _) =>
            {
                if (_editing) { Model.Text = editor.Text; _host.ItemContentChanged(this); }
            };
            editor.LostFocus += (_, _) => CommitEdit();
            editor.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { CommitEdit(); e.Handled = true; }
            };
        }

        MouseEnter += (_, _) => UpdateChrome(hover: true);
        MouseLeave += (_, _) => UpdateChrome(hover: false);
        UpdateChrome(hover: false);
    }

    private static Border MakeRing(Color c, double thickness)
    {
        var ring = new Border
        {
            BorderBrush = new SolidColorBrush(c),
            BorderThickness = new Thickness(thickness),
            Margin = new Thickness(-2),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        return ring;
    }

    // ---- Chrome state ----

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (!selected && _editing) CommitEdit();
        UpdateChrome(IsMouseOver);
    }

    public void SetPinned(bool pinned)
    {
        Model.Pinned = pinned;
        _pin.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
        Cursor = pinned ? Cursors.Arrow : Cursors.SizeAll;
        UpdateChrome(IsMouseOver);
    }

    private void UpdateChrome(bool hover)
    {
        bool select = _host.IsSelectToolActive;
        _selRing.Visibility = IsSelected && select ? Visibility.Visible : Visibility.Collapsed;
        _hoverRing.Visibility = !IsSelected && hover && select ? Visibility.Visible : Visibility.Collapsed;

        bool handles = IsSelected && select && !Model.Pinned && !_editing;
        foreach (var h in _handles)
            h.Visibility = handles ? Visibility.Visible : Visibility.Collapsed;
        _rotateHandle.Visibility = handles ? Visibility.Visible : Visibility.Collapsed;
        _rotateStem.Visibility = handles ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Called by the host when the active tool changes.</summary>
    public void RefreshToolState() => UpdateChrome(IsMouseOver);

    // ---- Editing ----

    public void BeginEdit()
    {
        if (_content.Editor is not TextBox editor) return;
        _editing = true;
        editor.IsHitTestVisible = true;
        editor.Focusable = true;
        editor.Focus();
        editor.CaretIndex = editor.Text.Length;
        UpdateChrome(IsMouseOver);
    }

    public void CommitEdit()
    {
        if (!_editing || _content.Editor is not TextBox editor) return;
        _editing = false;
        Model.Text = editor.Text;
        editor.IsHitTestVisible = false;
        editor.Focusable = false;
        Keyboard.ClearFocus();
        _host.ItemContentChanged(this);
        UpdateChrome(IsMouseOver);
    }

    // ---- Geometry sync ----

    public void ApplyModelBounds()
    {
        Canvas.SetLeft(this, Model.X);
        Canvas.SetTop(this, Model.Y);
        Width = Model.W;
        Height = Model.H;
        _rotate.Angle = Model.Rotation;
        Panel.SetZIndex(this, Model.Z);
    }

    public void MoveBy(Vector delta)
    {
        Model.X += delta.X;
        Model.Y += delta.Y;
        Canvas.SetLeft(this, Model.X);
        Canvas.SetTop(this, Model.Y);
    }

    // ---- Gestures ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_host.IsSelectToolActive || _editing) return;

        // The calendar chip on reminders opens the date picker instead of dragging.
        if (_content.DateChip is not null && IsOn(_content.DateChip, e.OriginalSource))
        {
            _host.ItemPressed(this, additive: false);
            _host.ItemDatePickRequested(this);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            if (Model.Kind is BoardItemKind.Link or BoardItemKind.File)
                _host.ItemOpenRequested(this);
            else if (Editor is not null)
                BeginEdit();
            e.Handled = true;
            return;
        }

        bool additive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _host.ItemPressed(this, additive);

        _gestureStartCanvas = _lastDragCanvas = e.GetPosition(_host.ItemsHost);
        _startBounds = new Rect(Model.X, Model.Y, Model.W, Model.H);
        _startRotation = Model.Rotation;

        if (!Model.Pinned && FindHandle(e.OriginalSource) is int handleIndex)
        {
            _gesture = Gesture.Resize;
            _resizeDx = HandleSpec[handleIndex].Dx;
            _resizeDy = HandleSpec[handleIndex].Dy;
            _host.ItemTransformStart();
        }
        else if (!Model.Pinned && IsOn(_rotateHandle, e.OriginalSource))
        {
            _gesture = Gesture.Rotate;
            var c = CenterOnCanvas();
            _rotatePointerOffset = AngleDeg(c, _gestureStartCanvas) - Model.Rotation;
            _host.ItemTransformStart();
        }
        else if (!Model.Pinned)
        {
            _gesture = Gesture.Move;
            _host.ItemTransformStart();
        }
        else
        {
            _gesture = Gesture.None;
            e.Handled = true;
            return;
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_gesture == Gesture.None || e.LeftButton != MouseButtonState.Pressed) return;

        Point pos = e.GetPosition(_host.ItemsHost);
        switch (_gesture)
        {
            case Gesture.Move:
                _host.ItemDragDelta(pos - _lastDragCanvas);
                _lastDragCanvas = pos;
                break;

            case Gesture.Resize:
                ApplyResize(pos);
                break;

            case Gesture.Rotate:
                double angle = AngleDeg(CenterOnCanvas(), pos) - _rotatePointerOffset;
                angle = NormalizeDeg(angle);
                double snapped = Math.Round(angle / 15.0) * 15.0;
                if (Math.Abs(NormalizeDeg(angle - snapped)) <= 3) angle = NormalizeDeg(snapped);
                Model.Rotation = angle;
                _rotate.Angle = angle;
                break;
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_gesture == Gesture.None) return;

        var name = _gesture switch
        {
            Gesture.Resize => "Resize",
            Gesture.Rotate => "Rotate",
            _ => "Move",
        };
        _gesture = Gesture.None;
        ReleaseMouseCapture();
        _host.ItemTransformEnd(name);
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!_host.IsSelectToolActive) return;
        if (!IsSelected) _host.ItemPressed(this, additive: false);
        _host.ShowItemMenu(this);
        e.Handled = true;
    }

    // ---- Resize math (anchored at the corner opposite the dragged handle) ----

    private void ApplyResize(Point mouseCanvas)
    {
        double th = _startRotation * Math.PI / 180.0;
        double cos = Math.Cos(th), sin = Math.Sin(th);

        Point c0 = new(_startBounds.X + _startBounds.Width / 2, _startBounds.Y + _startBounds.Height / 2);
        // Anchor = opposite corner, in world space.
        double ax = -_resizeDx * _startBounds.Width / 2;
        double ay = -_resizeDy * _startBounds.Height / 2;
        Point anchor = new(
            c0.X + ax * cos - ay * sin,
            c0.Y + ax * sin + ay * cos);

        // Mouse vector from anchor, rotated back into item space.
        double dxw = mouseCanvas.X - anchor.X;
        double dyw = mouseCanvas.Y - anchor.Y;
        double lx = dxw * cos + dyw * sin;
        double ly = -dxw * sin + dyw * cos;

        double w = Math.Max(_content.MinWidth, lx * _resizeDx);
        double h = Math.Max(_content.MinHeight, ly * _resizeDy);

        bool proportional = _content.ProportionalResize
            ? (Keyboard.Modifiers & ModifierKeys.Shift) == 0
            : (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (proportional && _startBounds.Width > 0 && _startBounds.Height > 0)
        {
            double s = Math.Max(w / _startBounds.Width, h / _startBounds.Height);
            w = Math.Max(_content.MinWidth, _startBounds.Width * s);
            h = Math.Max(_content.MinHeight, _startBounds.Height * s);
        }

        // New center = anchor + rotated local vector to center.
        double vx = _resizeDx * w / 2;
        double vy = _resizeDy * h / 2;
        Point c1 = new(
            anchor.X + vx * cos - vy * sin,
            anchor.Y + vx * sin + vy * cos);

        Model.X = c1.X - w / 2;
        Model.Y = c1.Y - h / 2;
        Model.W = w;
        Model.H = h;
        ApplyModelBounds();
    }

    private Point CenterOnCanvas() => new(Model.X + Model.W / 2, Model.Y + Model.H / 2);

    private static double AngleDeg(Point center, Point p)
        => Math.Atan2(p.Y - center.Y, p.X - center.X) * 180.0 / Math.PI + 90.0;

    private static double NormalizeDeg(double a)
    {
        a %= 360.0;
        if (a > 180) a -= 360;
        if (a < -180) a += 360;
        return a;
    }

    private int? FindHandle(object source)
    {
        var d = source as DependencyObject;
        while (d is not null && d != this)
        {
            if (d is Border b && b.Tag is int i && ReferenceEquals(_handles[i], b)) return i;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private bool IsOn(FrameworkElement target, object source)
    {
        var d = source as DependencyObject;
        while (d is not null && d != this)
        {
            if (ReferenceEquals(d, target)) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }
}
