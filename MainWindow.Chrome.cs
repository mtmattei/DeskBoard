using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskBoard.Board;
using DeskBoard.Rendering;

using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace DeskBoard;

/// <summary>
/// Floating chrome wiring: title pill, tool rail, pen/color/shape bar, zoom cluster,
/// and minimap. Chrome raises the same intents the magnet dock used to; behavior
/// stays in the sibling partials.
/// </summary>
public partial class MainWindow
{
    private enum PenKind { Fine, Chisel, Highlighter, Brush }

    private PenKind _pen = PenKind.Chisel;
    private bool _minimapDragging;

    private static readonly MediaColor[] SwatchColors =
    {
        MediaColor.FromRgb(0x26, 0x26, 0x26), // black
        MediaColor.FromRgb(0xD9, 0x70, 0x4C), // clay
        MediaColor.FromRgb(0x7E, 0x94, 0x64), // moss
        MediaColor.FromRgb(0x7F, 0xA3, 0xC6), // slate blue
        MediaColor.FromRgb(0xD9, 0xC7, 0xA6), // sand
    };

    private void WireChrome()
    {
        TitlePill.Click += (_, _) => OpenTitleMenu();
        BtnHideBoard.Click += (_, _) => ApplyMode(_backgroundMode);
        BtnShare.Click += (_, _) => CopyBoardSnapshot();

        RailSelect.Click += (_, _) => SetTool(Tool.Select);
        RailDraw.Click += (_, _) => SetTool(Tool.Marker);
        RailShape.Click += (_, _) => SetShapeTool(_shapeKind);
        RailNote.Click += (_, _) => AddNoteAtCenter();
        RailCalendar.Click += (_, _) => AddReminderAtCenter();
        RailImage.Click += (_, _) => PickImageFile();
        RailMore.Click += (_, _) => OpenMoreMenu();

        BarUndo.Click += (_, _) => _undo.Undo();

        PenFine.Click += (_, _) => SetPen(PenKind.Fine);
        PenChisel.Click += (_, _) => SetPen(PenKind.Chisel);
        PenHighlighter.Click += (_, _) => SetPen(PenKind.Highlighter);
        PenBrush.Click += (_, _) => SetPen(PenKind.Brush);

        Swatch0.Click += (_, _) => SetMarker(SwatchColors[0]);
        Swatch1.Click += (_, _) => SetMarker(SwatchColors[1]);
        Swatch2.Click += (_, _) => SetMarker(SwatchColors[2]);
        Swatch3.Click += (_, _) => SetMarker(SwatchColors[3]);
        Swatch4.Click += (_, _) => SetMarker(SwatchColors[4]);
        BtnCustomColor.Click += (_, _) => PickColor();

        BtnShapeRect.Click += (_, _) => SetShapeTool(ShapeKind.Rect);
        BtnShapeEllipse.Click += (_, _) => SetShapeTool(ShapeKind.Ellipse);
        BtnShapeArrow.Click += (_, _) => SetShapeTool(ShapeKind.Arrow);
        BtnTextTool.Click += (_, _) => SetTool(Tool.Text);

        BtnZoomOut.Click += (_, _) => ZoomAt(ViewportCenter(), _zoom / 1.2);
        BtnZoomIn.Click += (_, _) => ZoomAt(ViewportCenter(), _zoom * 1.2);
        BtnFit.Click += (_, _) => { ResetView(); UpdateZoomUi(); };
        BtnBento.Click += (_, _) => SnapToBentoGrid();

        MinimapArea.SizeChanged += (_, _) => UpdateMinimap();
        MinimapArea.MouseLeftButtonDown += Minimap_Down;
        MinimapArea.MouseMove += Minimap_Move;
        MinimapArea.MouseLeftButtonUp += Minimap_Up;
    }

    private Point ViewportCenter() =>
        new(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);

    // ---- Pens ----

    private void SetPen(PenKind pen)
    {
        _pen = pen;
        ApplyPenAttributes();
        SetTool(Tool.Marker);
    }

    /// <summary>Nib presets; color rides along so swatches and pens compose freely.</summary>
    private void ApplyPenAttributes()
    {
        var da = Ink.DefaultDrawingAttributes;
        da.FitToCurve = true;
        da.IgnorePressure = false;
        da.IsHighlighter = false;

        switch (_pen)
        {
            case PenKind.Fine:
                da.StylusTip = StylusTip.Ellipse;
                da.Width = 2.8; da.Height = 2.8;
                break;
            case PenKind.Chisel:
                da.StylusTip = StylusTip.Rectangle;
                da.Width = 4.6; da.Height = 7.6;
                break;
            case PenKind.Highlighter:
                da.StylusTip = StylusTip.Rectangle;
                da.Width = 11; da.Height = 22;
                da.IsHighlighter = true;
                break;
            case PenKind.Brush:
                da.StylusTip = StylusTip.Ellipse;
                da.Width = 7; da.Height = 7;
                break;
        }

        da.Color = _inkColor;
    }

    // ---- Selection / active states ----

    private void UpdateChromeToolState()
    {
        var active = (Brush)FindResource("ActiveTint");

        RailSelect.Background = _tool == Tool.Select ? active : Brushes.Transparent;
        RailDraw.Background = _tool == Tool.Marker ? active : Brushes.Transparent;
        RailShape.Background = _tool == Tool.Shape ? active : Brushes.Transparent;

        bool inking = _tool == Tool.Marker;
        PenFine.Tag = inking && _pen == PenKind.Fine ? "On" : null;
        PenChisel.Tag = inking && _pen == PenKind.Chisel ? "On" : null;
        PenHighlighter.Tag = inking && _pen == PenKind.Highlighter ? "On" : null;
        PenBrush.Tag = inking && _pen == PenKind.Brush ? "On" : null;

        Swatch0.Tag = _inkColor == SwatchColors[0] ? "On" : null;
        Swatch1.Tag = _inkColor == SwatchColors[1] ? "On" : null;
        Swatch2.Tag = _inkColor == SwatchColors[2] ? "On" : null;
        Swatch3.Tag = _inkColor == SwatchColors[3] ? "On" : null;
        Swatch4.Tag = _inkColor == SwatchColors[4] ? "On" : null;

        BtnShapeRect.Tag = _tool == Tool.Shape && _shapeKind == ShapeKind.Rect ? "On" : null;
        BtnShapeEllipse.Tag = _tool == Tool.Shape && _shapeKind == ShapeKind.Ellipse ? "On" : null;
        BtnShapeArrow.Tag = _tool == Tool.Shape && _shapeKind == ShapeKind.Arrow ? "On" : null;
        BtnTextTool.Tag = _tool == Tool.Text ? "On" : null;
    }

    private void UpdateUndoState()
    {
        BarUndo.IsEnabled = _undo.CanUndo;
    }

    // ---- Zoom & minimap ----

    private void UpdateZoomUi()
    {
        ZoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
        UpdateMinimap();
    }

    /// <summary>The white window shows the visible slice of the board; drag it to pan.</summary>
    private void UpdateMinimap()
    {
        double aw = MinimapArea.ActualWidth, ah = MinimapArea.ActualHeight;
        if (aw <= 0 || ah <= 0 || Viewport.ActualWidth <= 0) return;

        double w = aw / _zoom, h = ah / _zoom;
        double x = -PanTranslate.X / (Viewport.ActualWidth * _zoom) * aw;
        double y = -PanTranslate.Y / (Viewport.ActualHeight * _zoom) * ah;

        MinimapViewRect.Width = w;
        MinimapViewRect.Height = h;
        Canvas.SetLeft(MinimapViewRect, x);
        Canvas.SetTop(MinimapViewRect, y);
    }

    private void Minimap_Down(object sender, MouseButtonEventArgs e)
    {
        _minimapDragging = true;
        MinimapArea.CaptureMouse();
        PanMinimapTo(e.GetPosition(MinimapArea));
        e.Handled = true;
    }

    private void Minimap_Move(object sender, MouseEventArgs e)
    {
        if (!_minimapDragging) return;
        PanMinimapTo(e.GetPosition(MinimapArea));
        e.Handled = true;
    }

    private void Minimap_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_minimapDragging) return;
        _minimapDragging = false;
        MinimapArea.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PanMinimapTo(Point p)
    {
        double aw = MinimapArea.ActualWidth, ah = MinimapArea.ActualHeight;
        if (aw <= 0 || ah <= 0) return;

        double w = aw / _zoom, h = ah / _zoom;
        SetPan(-(p.X - w / 2) / aw * Viewport.ActualWidth * _zoom,
               -(p.Y - h / 2) / ah * Viewport.ActualHeight * _zoom);
    }

    // ---- Bento grid snap ----

    /// <summary>
    /// Arranges every unpinned item on a bento grid: sizes snap to 1–3 grid cells,
    /// big items place first, images keep their aspect inside their cell. Undoable;
    /// items glide to their slots (280ms, house curve).
    /// </summary>
    private void SnapToBentoGrid()
    {
        CommitAnyEdit();
        ClearSelection();

        var movable = _items.Where(v => !v.Model.Pinned).ToList();
        if (movable.Count == 0) return;

        // Keep clear of the floating chrome: title pill, tool rail, bottom bar.
        const double left = 90, top = 96, right = 130, bottom = 120, gutter = 18;
        double availW = Viewport.ActualWidth - left - right;
        double availH = Viewport.ActualHeight - top - bottom;
        if (availW < 260 || availH < 260) return;

        double pitch = 128;
        var plan = PackBento(movable, availW, pitch, gutter);

        // One shrink pass if the grid runs past the bottom of the screen.
        double needH = plan.Rows * pitch;
        if (needH > availH)
        {
            pitch = Math.Max(76, pitch * Math.Sqrt(availH / needH));
            plan = PackBento(movable, availW, pitch, gutter);
        }

        double originX = left + Math.Max(0, (availW - plan.Cols * pitch) / 2);
        double originY = top;

        var before = movable.Select(v =>
            (v, v.Model.X, v.Model.Y, v.Model.W, v.Model.H, v.Model.Rotation)).ToList();

        for (int i = 0; i < movable.Count; i++)
        {
            var m = movable[i].Model;
            var box = plan.Boxes[i];
            double bx = originX + box.X, by = originY + box.Y;
            double bw = box.Width, bh = box.Height;

            if (m.Kind == BoardItemKind.Image && m.W > 0 && m.H > 0)
            {
                // Aspect-fit inside the cell, centered — images never distort.
                double s = Math.Min(bw / m.W, bh / m.H);
                double w = m.W * s, h = m.H * s;
                m.X = bx + (bw - w) / 2; m.Y = by + (bh - h) / 2;
                m.W = w; m.H = h;
            }
            else
            {
                m.X = bx; m.Y = by; m.W = bw; m.H = bh;
            }
            m.Rotation = 0;
        }

        var after = movable.Select(v =>
            (v, v.Model.X, v.Model.Y, v.Model.W, v.Model.H, v.Model.Rotation)).ToList();

        _undo.Push("Bento grid",
            undo: () => RestoreItemBounds(before),
            redo: () => RestoreItemBounds(after));

        foreach (var v in movable) GlideToModelBounds(v);
        ScheduleSave();
    }

    private void RestoreItemBounds(
        List<(Board.BoardItemView v, double X, double Y, double W, double H, double Rotation)> state)
    {
        foreach (var (v, x, y, w, h, r) in state)
        {
            v.Model.X = x; v.Model.Y = y; v.Model.W = w; v.Model.H = h; v.Model.Rotation = r;
            v.ApplyModelBounds();
        }
        ScheduleSave();
    }

    private readonly record struct BentoBox(double X, double Y, double Width, double Height);
    private readonly record struct BentoPlan(List<BentoBox> Boxes, int Cols, int Rows);

    /// <summary>Greedy first-fit packing on a cell grid; span 1–3 cells per axis.</summary>
    private static BentoPlan PackBento(
        List<Board.BoardItemView> items, double availW, double pitch, double gutter)
    {
        int cols = Math.Max(2, (int)(availW / pitch));
        var occupied = new List<bool[]>();
        var boxes = new BentoBox[items.Count];

        // Big items first so they anchor the grid; index order preserved in the result.
        var order = Enumerable.Range(0, items.Count)
            .OrderByDescending(i => items[i].Model.W * items[i].Model.H).ToList();

        int rowsUsed = 0;
        foreach (int i in order)
        {
            var m = items[i].Model;
            int spanW = Math.Clamp((int)Math.Round(m.W / pitch), 1, Math.Min(3, cols));
            int spanH = Math.Clamp((int)Math.Round(m.H / pitch), 1, 3);

            (int row, int col) = FindSlot(occupied, cols, spanW, spanH);
            for (int r = row; r < row + spanH; r++)
            {
                while (occupied.Count <= r) occupied.Add(new bool[cols]);
                for (int c = col; c < col + spanW; c++) occupied[r][c] = true;
            }
            rowsUsed = Math.Max(rowsUsed, row + spanH);

            boxes[i] = new BentoBox(col * pitch, row * pitch,
                spanW * pitch - gutter, spanH * pitch - gutter);
        }

        return new BentoPlan(boxes.ToList(), cols, rowsUsed);
    }

    private static (int Row, int Col) FindSlot(List<bool[]> occupied, int cols, int spanW, int spanH)
    {
        for (int row = 0; ; row++)
        {
            for (int col = 0; col + spanW <= cols; col++)
            {
                bool free = true;
                for (int r = row; r < row + spanH && free; r++)
                {
                    if (r >= occupied.Count) continue; // rows below the grid are empty
                    for (int c = col; c < col + spanW; c++)
                        if (occupied[r][c]) { free = false; break; }
                }
                if (free) return (row, col);
            }
        }
    }

    /// <summary>Size/rotation snap immediately; position glides on the house curve.</summary>
    private void GlideToModelBounds(Board.BoardItemView v)
    {
        double fromX = Canvas.GetLeft(v), fromY = Canvas.GetTop(v);
        v.ApplyModelBounds();

        if (!Rendering.Motion.Enabled || double.IsNaN(fromX) || double.IsNaN(fromY)) return;

        Canvas.SetLeft(v, fromX);
        Canvas.SetTop(v, fromY);
        Motion.Animate(v, Canvas.LeftProperty, v.Model.X, Motion.Slow,
            completed: () => { v.BeginAnimation(Canvas.LeftProperty, null); Canvas.SetLeft(v, v.Model.X); });
        Motion.Animate(v, Canvas.TopProperty, v.Model.Y, Motion.Slow,
            completed: () => { v.BeginAnimation(Canvas.TopProperty, null); Canvas.SetTop(v, v.Model.Y); });
    }

    // ---- Snapshot export ----

    private void CopyBoardSnapshot()
    {
        int w = (int)Math.Round(Root.ActualWidth);
        int h = (int)Math.Round(Root.ActualHeight);
        if (w <= 0 || h <= 0) return;

        try
        {
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(BoardVisual);
            Clipboard.SetImage(rtb);
            _trayIcon?.ShowBalloonTip(2500, "DeskBoard",
                "Board snapshot copied to the clipboard.", WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log($"Snapshot failed: {ex.Message}");
        }
    }

    // ---- Menus ----

    private void OpenTitleMenu()
    {
        var menu = NewBoardMenu();
        menu.Items.Add(Item("Quick note", "Ctrl+Alt+N", OpenQuickNote));
        menu.Items.Add(Item("Snip to board", "Ctrl+Alt+S", OpenSnip));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Ink on desktop when hidden", "Ctrl+Alt+A",
            () => SetBackgroundMode(OverlayMode.Ambient), _backgroundMode == OverlayMode.Ambient));
        menu.Items.Add(Item("Hide everything", "Ctrl+Alt+H",
            () => SetBackgroundMode(OverlayMode.Hidden), _backgroundMode == OverlayMode.Hidden));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Clear board", null, ClearBoard));
        menu.Items.Add(Item("Exit DeskBoard", null, ExitApp));
        Open(menu, TitlePill, PlacementMode.Bottom);
    }

    private void OpenMoreMenu()
    {
        var menu = NewBoardMenu();
        menu.Items.Add(Item("Eraser", "E", () => SetTool(Tool.Eraser), _tool == Tool.Eraser));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quick note", "Ctrl+Alt+N", OpenQuickNote));
        menu.Items.Add(Item("Snip to board", "Ctrl+Alt+S", OpenSnip));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Clear board", null, ClearBoard));
        menu.Items.Add(Item("Hide the board", "Ctrl+Alt+D", () => ApplyMode(_backgroundMode)));
        Open(menu, RailMore, PlacementMode.Left);
    }

    private ContextMenu NewBoardMenu() =>
        new() { Style = (Style)FindResource("BoardContextMenu") };

    private static MenuItem Item(string header, string? gesture, Action action, bool isChecked = false)
    {
        var mi = new MenuItem
        {
            Header = header,
            InputGestureText = gesture ?? string.Empty,
            IsChecked = isChecked,
        };
        mi.Click += (_, _) => action();
        return mi;
    }

    private static void Open(ContextMenu menu, UIElement anchor, PlacementMode placement)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = placement;
        menu.IsOpen = true;
    }
}
