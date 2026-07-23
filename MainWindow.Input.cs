using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DeskBoard.Board;
using DeskBoard.Rendering;

using MediaColor = System.Windows.Media.Color;

namespace DeskBoard;

/// <summary>Keyboard, zoom/pan, marquee selection, clipboard, drag-drop, ink events.</summary>
public partial class MainWindow
{
    private const double MinZoom = 1.0;
    private const double MaxZoom = 3.0;

    private bool _lastInputWasStylus;
    private double _zoom = 1.0;
    private bool _spaceDown;
    private bool _panning;
    private Point _panStart;
    private Point _panOrigin;

    private bool _marqueeActive;
    private Point _marqueeStart;
    private Rectangle? _marqueeRect;

    private void WireInput()
    {
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewMouseDown += OnPreviewMouseDownForPan;
        PreviewMouseMove += OnPreviewMouseMoveForPan;
        PreviewMouseUp += OnPreviewMouseUpForPan;

        DragOver += OnBoardDragOver;
        DragLeave += (_, _) => DropRing.Visibility = Visibility.Collapsed;
        Drop += OnBoardDrop;

        ItemsCanvas.MouseLeftButtonDown += OnCanvasMouseLeftButtonDown;
        ItemsCanvas.MouseMove += OnCanvasMouseMove;
        ItemsCanvas.MouseLeftButtonUp += OnCanvasMouseLeftButtonUp;
        ItemsCanvas.MouseRightButtonUp += OnCanvasMouseRightButtonUp;

        Viewport.SizeChanged += (_, e) =>
        {
            BoardContent.Width = e.NewSize.Width;
            BoardContent.Height = e.NewSize.Height;
            UpdateFrameGeometry(e.NewSize.Width, e.NewSize.Height);
        };
    }

    /// <summary>Frame band = screen rect minus a rounded inner opening (radius 12).</summary>
    private void UpdateFrameGeometry(double w, double h)
    {
        if (w <= 40 || h <= 40) return;
        var geometry = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, w, h)),
            new RectangleGeometry(new Rect(18, 18, w - 36, h - 36), 12, 12));
        geometry.Freeze();
        NeuFrame.Data = geometry;
        NeuFrameNoise.Data = geometry;
        NeuFrameShade.Data = geometry;
    }

    // ---- Keyboard ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_mode != OverlayMode.Board) return;

        // While a text editor has focus, let it own the keyboard (its Esc commits).
        if (Keyboard.FocusedElement is TextBox)
            return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.Space:
                if (!_spaceDown && _zoom > MinZoom)
                {
                    _spaceDown = true;
                    Viewport.Cursor = Cursors.Hand;
                }
                e.Handled = true;
                return;

            case Key.Escape:
                if (_selection.Count > 0) ClearSelection();
                else ApplyMode(_backgroundMode);
                e.Handled = true;
                return;

            case Key.Z when ctrl:
                _undo.Undo();
                e.Handled = true;
                return;
            case Key.Y when ctrl:
                _undo.Redo();
                e.Handled = true;
                return;

            case Key.Delete:
            case Key.Back:
                DeleteSelection();
                e.Handled = true;
                return;

            case Key.D when ctrl:
                DuplicateSelection();
                e.Handled = true;
                return;
            case Key.A when ctrl:
                SelectAllItems();
                e.Handled = true;
                return;
            case Key.G when ctrl && shift:
                UngroupSelection();
                e.Handled = true;
                return;
            case Key.G when ctrl:
                GroupSelection();
                e.Handled = true;
                return;
            case Key.V when ctrl:
                PasteFromClipboard();
                e.Handled = true;
                return;

            case Key.D0 when ctrl:
            case Key.NumPad0 when ctrl:
                ResetView();
                ShowZoomHud();
                e.Handled = true;
                return;
            case Key.OemPlus when ctrl:
            case Key.Add when ctrl:
                ZoomAt(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), _zoom * 1.2);
                e.Handled = true;
                return;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                ZoomAt(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), _zoom / 1.2);
                e.Handled = true;
                return;

            case Key.Left: NudgeSelection(-1, 0, shift); e.Handled = true; return;
            case Key.Right: NudgeSelection(1, 0, shift); e.Handled = true; return;
            case Key.Up: NudgeSelection(0, -1, shift); e.Handled = true; return;
            case Key.Down: NudgeSelection(0, 1, shift); e.Handled = true; return;

            case Key.V: SetTool(Tool.Select); e.Handled = true; return;
            case Key.B: SetTool(Tool.Marker); e.Handled = true; return;
            case Key.E: SetTool(Tool.Eraser); e.Handled = true; return;
            case Key.T: SetTool(Tool.Text); e.Handled = true; return;
            case Key.A: SetShapeTool(ShapeKind.Arrow); e.Handled = true; return;
            case Key.O: SetShapeTool(ShapeKind.Ellipse); e.Handled = true; return;
            case Key.N: AddNoteAtCenter(); e.Handled = true; return;
            case Key.R: AddReminderAtCenter(); e.Handled = true; return;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
            if (!_panning) Viewport.Cursor = null;
        }
    }

    private void NudgeSelection(double dx, double dy, bool large)
    {
        var movable = _selection.Where(v => !v.Model.Pinned).ToList();
        if (movable.Count == 0) return;

        double step = large ? 10 : 1;
        var delta = new Vector(dx * step, dy * step);
        var before = movable.Select(v => (v, v.Model.X, v.Model.Y)).ToList();

        foreach (var v in movable) v.MoveBy(delta);

        _undo.Push("Nudge",
            undo: () =>
            {
                foreach (var (v, x, y) in before)
                {
                    v.Model.X = x; v.Model.Y = y;
                    v.ApplyModelBounds();
                }
                ScheduleSave();
            },
            redo: () =>
            {
                foreach (var (v, x, y) in before)
                {
                    v.Model.X = x + delta.X; v.Model.Y = y + delta.Y;
                    v.ApplyModelBounds();
                }
                ScheduleSave();
            });
        ScheduleSave();
    }

    // ---- Zoom & pan ----

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_mode != OverlayMode.Board) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        ZoomAt(e.GetPosition(Viewport), _zoom * factor);
        e.Handled = true;
    }

    private void ZoomAt(Point viewportPoint, double newZoom)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001) return;

        double ratio = newZoom / _zoom;
        double tx = viewportPoint.X - (viewportPoint.X - PanTranslate.X) * ratio;
        double ty = viewportPoint.Y - (viewportPoint.Y - PanTranslate.Y) * ratio;

        _zoom = newZoom;
        ZoomScale.ScaleX = _zoom;
        ZoomScale.ScaleY = _zoom;
        SetPan(tx, ty);
        ShowZoomHud();
    }

    private void SetPan(double tx, double ty)
    {
        PanTranslate.X = Math.Clamp(tx, Viewport.ActualWidth * (1 - _zoom), 0);
        PanTranslate.Y = Math.Clamp(ty, Viewport.ActualHeight * (1 - _zoom), 0);
    }

    private void ResetView()
    {
        _zoom = 1.0;
        ZoomScale.ScaleX = ZoomScale.ScaleY = 1.0;
        PanTranslate.X = PanTranslate.Y = 0;
    }

    private void ShowZoomHud()
    {
        ZoomHudText.Text = $"{Math.Round(_zoom * 100)}%";
        Motion.Animate(ZoomHud, OpacityProperty, 1, Motion.Fast);
        _zoomHudTimer.Stop();
        _zoomHudTimer.Start();
    }

    private void OnPreviewMouseDownForPan(object sender, MouseButtonEventArgs e)
    {
        if (_mode != OverlayMode.Board || _zoom <= MinZoom) return;
        bool middle = e.ChangedButton == MouseButton.Middle;
        bool spaceLeft = e.ChangedButton == MouseButton.Left && _spaceDown;
        if (!middle && !spaceLeft) return;

        _panning = true;
        _panStart = e.GetPosition(Viewport);
        _panOrigin = new Point(PanTranslate.X, PanTranslate.Y);
        Viewport.Cursor = Cursors.SizeAll;
        Root.CaptureMouse();
        e.Handled = true;
    }

    private void OnPreviewMouseMoveForPan(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var pos = e.GetPosition(Viewport);
        SetPan(_panOrigin.X + (pos.X - _panStart.X), _panOrigin.Y + (pos.Y - _panStart.Y));
        e.Handled = true;
    }

    private void OnPreviewMouseUpForPan(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Root.ReleaseMouseCapture();
        Viewport.Cursor = _spaceDown ? Cursors.Hand : null;
        e.Handled = true;
    }

    // ---- Marquee selection & empty-board context menu ----

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool != Tool.Select || _spaceDown) return;
        if (!ReferenceEquals(e.OriginalSource, ItemsCanvas)) return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            ClearSelection();

        _marqueeActive = true;
        _marqueeStart = e.GetPosition(ItemsCanvas);
        _marqueeRect = new Rectangle
        {
            Stroke = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0x2E, 0x6F, 0xE0)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(MediaColor.FromArgb(0x14, 0x2E, 0x6F, 0xE0)),
        };
        SelectionLayer.Children.Add(_marqueeRect);
        Canvas.SetLeft(_marqueeRect, _marqueeStart.X);
        Canvas.SetTop(_marqueeRect, _marqueeStart.Y);
        ItemsCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_marqueeActive || _marqueeRect is null) return;
        var pos = e.GetPosition(ItemsCanvas);
        var rect = new Rect(_marqueeStart, pos);
        Canvas.SetLeft(_marqueeRect, rect.X);
        Canvas.SetTop(_marqueeRect, rect.Y);
        _marqueeRect.Width = rect.Width;
        _marqueeRect.Height = rect.Height;
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_marqueeActive) return;
        _marqueeActive = false;
        ItemsCanvas.ReleaseMouseCapture();

        var rect = new Rect(_marqueeStart, e.GetPosition(ItemsCanvas));
        if (_marqueeRect is not null) SelectionLayer.Children.Remove(_marqueeRect);
        _marqueeRect = null;

        if (rect.Width < 3 && rect.Height < 3) return; // plain click, selection already cleared

        foreach (var view in _items)
        {
            if (rect.IntersectsWith(RotatedBounds(view.Model)))
            {
                _selection.Add(view);
                view.SetSelected(true);
            }
        }
        e.Handled = true;
    }

    private void OnCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_tool != Tool.Select) return;
        if (!ReferenceEquals(e.OriginalSource, ItemsCanvas)) return;
        ShowBoardContextMenu(e.GetPosition(ItemsCanvas));
        e.Handled = true;
    }

    /// <summary>Axis-aligned bounds of a (possibly rotated) item, in canvas space.</summary>
    private static Rect RotatedBounds(BoardItemModel m)
    {
        double th = m.Rotation * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(th)), sin = Math.Abs(Math.Sin(th));
        double w = m.W * cos + m.H * sin;
        double h = m.W * sin + m.H * cos;
        double cx = m.X + m.W / 2, cy = m.Y + m.H / 2;
        return new Rect(cx - w / 2, cy - h / 2, w, h);
    }

    // ---- Text tool placement ----

    private void Ink_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool != Tool.Text) return;

        var pos = e.GetPosition(ItemsCanvas);
        if (ItemAt(pos) is not null) return; // don't spawn text on top of an existing item

        CreateTextAt(new Point(pos.X, pos.Y - 24));
        e.Handled = true;
    }

    private BoardItemView? ItemAt(Point canvasPos)
    {
        foreach (var view in _items.OrderByDescending(v => v.Model.Z))
        {
            var m = view.Model;
            double th = -m.Rotation * Math.PI / 180.0;
            double cx = m.X + m.W / 2, cy = m.Y + m.H / 2;
            double dx = canvasPos.X - cx, dy = canvasPos.Y - cy;
            double lx = dx * Math.Cos(th) - dy * Math.Sin(th) + m.W / 2;
            double ly = dx * Math.Sin(th) + dy * Math.Cos(th) + m.H / 2;
            if (lx >= 0 && lx <= m.W && ly >= 0 && ly <= m.H) return view;
        }
        return null;
    }

    // ---- Clipboard & drag-drop ----

    private void PasteFromClipboard(Point? at = null)
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource bmp)
            {
                AddImageFromBitmap(bmp, at);
                return;
            }
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                ImportFiles(files, at);
                return;
            }
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                if (text.Length == 0) return;
                if (IsHttpUrl(text)) AddLinkCard(text, at);
                else
                {
                    var pos = at ?? ViewportCenterOnCanvas();
                    CreateNoteAt(new Point(pos.X - 105, pos.Y - 95), text, edit: false);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Paste failed: {ex.Message}");
        }
    }

    private static bool IsHttpUrl(string text) =>
        !text.Contains('\n')
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private void ImportFiles(IEnumerable<string> paths, Point? at)
    {
        int offset = 0;
        foreach (string path in paths)
        {
            Point? pos = at.HasValue ? new Point(at.Value.X + offset, at.Value.Y + offset) : null;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ImageExtensions.Contains(ext)) AddImageFromFile(path, pos);
            else AddFileCard(path, pos);
            offset += 28;
        }
    }

    private void OnBoardDragOver(object sender, DragEventArgs e)
    {
        bool usable = e.Data.GetDataPresent(DataFormats.FileDrop)
                      || e.Data.GetDataPresent(DataFormats.Bitmap)
                      || e.Data.GetDataPresent(DataFormats.Text);
        e.Effects = usable ? DragDropEffects.Copy : DragDropEffects.None;
        DropRing.Visibility = usable ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnBoardDrop(object sender, DragEventArgs e)
    {
        DropRing.Visibility = Visibility.Collapsed;
        var pos = e.GetPosition(ItemsCanvas);
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)
                && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                ImportFiles(files, pos);
            }
            else if (e.Data.GetDataPresent(DataFormats.Bitmap)
                     && e.Data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
            {
                AddImageFromBitmap(bmp, pos);
            }
            else if (e.Data.GetDataPresent(DataFormats.Text)
                     && e.Data.GetData(DataFormats.Text) is string text)
            {
                if (IsHttpUrl(text.Trim())) AddLinkCard(text.Trim(), pos);
                else CreateNoteAt(pos, text.Trim(), edit: false);
            }
        }
        catch (Exception ex)
        {
            Log($"Drop failed: {ex.Message}");
        }
        e.Handled = true;
    }

    // ---- Ink events ----

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (!_loading && !_undo.IsApplying)
        {
            var added = new StrokeCollection(e.Added);
            var removed = new StrokeCollection(e.Removed);
            if (added.Count > 0 || removed.Count > 0)
            {
                _undo.Push("Ink",
                    undo: () =>
                    {
                        foreach (var s in added) Ink.Strokes.Remove(s);
                        Ink.Strokes.Add(removed);
                        UpdateEmptyHint();
                        ScheduleSave();
                    },
                    redo: () =>
                    {
                        foreach (var s in removed) Ink.Strokes.Remove(s);
                        Ink.Strokes.Add(added);
                        UpdateEmptyHint();
                        ScheduleSave();
                    });
            }
        }
        UpdateEmptyHint();
        ScheduleSave();
    }

    /// <summary>
    /// Mouse strokes get a light moving-average smooth (window of 3) — mouse deltas are
    /// jittery compared to a stylus. Stylus strokes pass through untouched; FitToCurve
    /// handles the Bezier fairing for both.
    /// </summary>
    private void OnStrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        if (_lastInputWasStylus) return;
        var pts = e.Stroke.StylusPoints;
        if (pts.Count < 5) return;

        var smoothed = new System.Windows.Input.StylusPointCollection(pts.Description, pts.Count);
        smoothed.Add(pts[0]);
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var p = pts[i];
            p.X = (pts[i - 1].X + pts[i].X + pts[i + 1].X) / 3.0;
            p.Y = (pts[i - 1].Y + pts[i].Y + pts[i + 1].Y) / 3.0;
            smoothed.Add(p);
        }
        smoothed.Add(pts[^1]);
        e.Stroke.StylusPoints = smoothed;
    }
}
