using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskBoard.Board;
using DeskBoard.Rendering;
using DeskBoard.Services;

using MediaColor = System.Windows.Media.Color;

namespace DeskBoard;

/// <summary>Board-object management: creation, selection, transforms, context actions.</summary>
public partial class MainWindow
{
    private readonly HashSet<BoardItemView> _selection = new();
    private readonly Dictionary<BoardItemView, (double X, double Y, double W, double H, double Rot)>
        _transformSnapshot = new();
    private readonly Random _rng = new();
    private int _noteCascade;

    // ---- IBoardHost ----

    bool IBoardHost.IsSelectToolActive => _tool == Tool.Select;
    Panel IBoardHost.ItemsHost => ItemsCanvas;

    void IBoardHost.ItemPressed(BoardItemView item, bool additive)
    {
        var affected = ExpandGroup(item);
        if (additive)
        {
            bool select = !_selection.Contains(item);
            foreach (var v in affected)
            {
                if (select) _selection.Add(v);
                else _selection.Remove(v);
                v.SetSelected(select);
            }
        }
        else if (!_selection.Contains(item))
        {
            ClearSelection();
            foreach (var v in affected) { _selection.Add(v); v.SetSelected(true); }
        }
    }

    void IBoardHost.ItemDragDelta(Vector delta)
    {
        foreach (var v in _selection)
            if (!v.Model.Pinned)
                v.MoveBy(delta);
    }

    void IBoardHost.ItemTransformStart()
    {
        _transformSnapshot.Clear();
        foreach (var v in _selection)
            _transformSnapshot[v] = (v.Model.X, v.Model.Y, v.Model.W, v.Model.H, v.Model.Rotation);
    }

    void IBoardHost.ItemTransformEnd(string editName)
    {
        var changes = new List<(BoardItemView View,
            (double X, double Y, double W, double H, double Rot) Before,
            (double X, double Y, double W, double H, double Rot) After)>();

        foreach (var (view, before) in _transformSnapshot)
        {
            var after = (view.Model.X, view.Model.Y, view.Model.W, view.Model.H, view.Model.Rotation);
            if (before != after) changes.Add((view, before, after));
        }
        _transformSnapshot.Clear();
        if (changes.Count == 0) return;

        _undo.Push(editName,
            undo: () => { foreach (var c in changes) ApplyBounds(c.View, c.Before); },
            redo: () => { foreach (var c in changes) ApplyBounds(c.View, c.After); });
        ScheduleSave();
    }

    private static void ApplyBounds(BoardItemView view,
        (double X, double Y, double W, double H, double Rot) b)
    {
        view.Model.X = b.X; view.Model.Y = b.Y;
        view.Model.W = b.W; view.Model.H = b.H;
        view.Model.Rotation = b.Rot;
        view.ApplyModelBounds();
    }

    void IBoardHost.ItemContentChanged(BoardItemView item)
    {
        // A text item committed with nothing in it leaves an invisible hit-target —
        // drop it silently (blank sticky notes are legitimate and stay).
        if (item.Model.Kind == BoardItemKind.Text && !item.IsEditing
            && string.IsNullOrWhiteSpace(item.Model.Text))
        {
            DetachItem(item);
            UpdateEmptyHint();
        }
        ScheduleSave();
    }

    void IBoardHost.ItemOpenRequested(BoardItemView item)
    {
        string? target = item.Model.Kind switch
        {
            BoardItemKind.Link => item.Model.Url,
            BoardItemKind.File => item.Model.Path,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log($"Open failed: {ex.Message}");
            _trayIcon?.ShowBalloonTip(3000, "DeskBoard",
                "Could not open the target.", System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    void IBoardHost.ItemDatePickRequested(BoardItemView item) => ShowDatePicker(item);

    void IBoardHost.ShowItemMenu(BoardItemView item) => ShowItemContextMenu(item);

    // ---- Reminder dates ----

    private System.Windows.Controls.Primitives.Popup? _datePopup;

    private void ShowDatePicker(BoardItemView view)
    {
        _datePopup?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, false);

        var calendar = new Calendar
        {
            SelectedDate = view.Model.Due ?? DateTime.Today,
            DisplayDate = view.Model.Due ?? DateTime.Today,
            Background = Brushes.White,
        };
        var host = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x30, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Effect = BoardItemContent.SoftShadow(blur: 16, depth: 4, opacity: 0.18),
            Child = calendar,
        };

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = host,
            PlacementTarget = view,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            VerticalOffset = 6,
        };
        _datePopup = popup;

        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is not DateTime picked) return;
            popup.IsOpen = false;
            SetReminderDate(view, picked.Date);
        };
        // Calendar swallows the first click after opening unless capture is released.
        calendar.PreviewMouseUp += (_, e) =>
        {
            if (Mouse.Captured is CalendarItem) Mouse.Capture(null);
        };

        popup.IsOpen = true;
    }

    private void SetReminderDate(BoardItemView view, DateTime? date)
    {
        var model = view.Model;
        var before = model.Due;
        if (before == date) return;

        _undo.Push("Reminder date",
            undo: () => { model.Due = before; RebuildItem(model); ScheduleSave(); },
            redo: () => { model.Due = date; RebuildItem(model); ScheduleSave(); });

        model.Due = date;
        RebuildItem(model);
        ScheduleSave();
    }

    // ---- Selection ----

    private List<BoardItemView> ExpandGroup(BoardItemView item)
    {
        if (string.IsNullOrEmpty(item.Model.GroupId)) return new List<BoardItemView> { item };
        return _items.Where(v => v.Model.GroupId == item.Model.GroupId).ToList();
    }

    private void ClearSelection()
    {
        foreach (var v in _selection) v.SetSelected(false);
        _selection.Clear();
    }

    private void SelectAllItems()
    {
        if (_tool != Tool.Select) SetTool(Tool.Select);
        foreach (var v in _items) { _selection.Add(v); v.SetSelected(true); }
    }

    private void CommitAnyEdit()
    {
        foreach (var v in _items)
            if (v.IsEditing) v.CommitEdit();
    }

    // ---- Item lifecycle ----

    private BoardItemView CreateView(BoardItemModel model)
    {
        if (model.Kind == BoardItemKind.Text)
            SizeTextItemToContent(model);
        var view = new BoardItemView(model, this);
        view.ApplyModelBounds();
        return view;
    }

    /// <summary>Grows a text item to fit its content so nothing clips (never shrinks).</summary>
    private void SizeTextItemToContent(BoardItemModel model)
    {
        if (string.IsNullOrEmpty(model.Text)) return;
        double fontSize = model.FontSize <= 0 ? 34 : model.FontSize;
        var ft = new FormattedText(
            model.Text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(BoardItemContent.Handwriting, FontStyles.Normal,
                FontWeights.SemiBold, FontStretches.Normal),
            fontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        model.W = Math.Max(model.W, ft.WidthIncludingTrailingWhitespace + 26);
        model.H = Math.Max(model.H, ft.Height + 18);
    }

    private void AttachItem(BoardItemView view)
    {
        if (!_items.Contains(view)) _items.Add(view);
        if (!ItemsCanvas.Children.Contains(view)) ItemsCanvas.Children.Add(view);
        view.ApplyModelBounds();
        view.RefreshToolState();
    }

    private void DetachItem(BoardItemView view)
    {
        if (view.IsEditing) view.CommitEdit();
        _selection.Remove(view);
        view.SetSelected(false);
        _items.Remove(view);
        ItemsCanvas.Children.Remove(view);
    }

    private void AddItemWithUndo(BoardItemView view, string editName, bool select = true)
    {
        AttachItem(view);
        _undo.Push(editName,
            undo: () => { DetachItem(view); UpdateEmptyHint(); ScheduleSave(); },
            redo: () => { AttachItem(view); UpdateEmptyHint(); ScheduleSave(); });

        if (select)
        {
            if (_tool != Tool.Select) SetTool(Tool.Select);
            ClearSelection();
            _selection.Add(view);
            view.SetSelected(true);
        }
        UpdateEmptyHint();
        ScheduleSave();
    }

    private void DeleteSelection()
    {
        if (_selection.Count == 0) return;
        var removed = _selection.ToList();

        _undo.Push("Delete",
            undo: () => { foreach (var v in removed) AttachItem(v); UpdateEmptyHint(); ScheduleSave(); },
            redo: () => { foreach (var v in removed) DetachItem(v); UpdateEmptyHint(); ScheduleSave(); });

        foreach (var v in removed) DetachItem(v);
        UpdateEmptyHint();
        ScheduleSave();
    }

    private void DuplicateSelection()
    {
        if (_selection.Count == 0) return;
        var groupMap = new Dictionary<string, string>();
        var clones = new List<BoardItemView>();

        foreach (var source in _selection.OrderBy(v => v.Model.Z))
        {
            var m = source.Model.Clone();
            m.Id = Guid.NewGuid().ToString("N");
            m.X += 24; m.Y += 24;
            m.Z = NextZ();
            m.Pinned = false;
            if (!string.IsNullOrEmpty(m.GroupId))
            {
                if (!groupMap.TryGetValue(m.GroupId, out var mapped))
                    groupMap[m.GroupId] = mapped = Guid.NewGuid().ToString("N");
                m.GroupId = mapped;
            }
            clones.Add(CreateView(m));
        }

        _undo.Push("Duplicate",
            undo: () => { foreach (var v in clones) DetachItem(v); UpdateEmptyHint(); ScheduleSave(); },
            redo: () => { foreach (var v in clones) AttachItem(v); UpdateEmptyHint(); ScheduleSave(); });

        ClearSelection();
        foreach (var v in clones)
        {
            AttachItem(v);
            _selection.Add(v);
            v.SetSelected(true);
        }
        UpdateEmptyHint();
        ScheduleSave();
    }

    private void GroupSelection()
    {
        var members = _selection.ToList();
        if (members.Count < 2) return;
        var before = members.Select(v => (v, v.Model.GroupId)).ToList();
        string groupId = Guid.NewGuid().ToString("N");

        _undo.Push("Group",
            undo: () => { foreach (var (v, g) in before) v.Model.GroupId = g; ScheduleSave(); },
            redo: () => { foreach (var v in members) v.Model.GroupId = groupId; ScheduleSave(); });

        foreach (var v in members) v.Model.GroupId = groupId;
        ScheduleSave();
    }

    private void UngroupSelection()
    {
        var members = _selection.Where(v => !string.IsNullOrEmpty(v.Model.GroupId)).ToList();
        if (members.Count == 0) return;
        var before = members.Select(v => (v, v.Model.GroupId)).ToList();

        _undo.Push("Ungroup",
            undo: () => { foreach (var (v, g) in before) v.Model.GroupId = g; ScheduleSave(); },
            redo: () => { foreach (var v in members) v.Model.GroupId = null; ScheduleSave(); });

        foreach (var v in members) v.Model.GroupId = null;
        ScheduleSave();
    }

    private void TogglePin(BoardItemView view)
    {
        bool newPinned = !view.Model.Pinned;
        _undo.Push(newPinned ? "Pin" : "Unpin",
            undo: () => { view.SetPinned(!newPinned); ScheduleSave(); },
            redo: () => { view.SetPinned(newPinned); ScheduleSave(); });
        view.SetPinned(newPinned);
        ScheduleSave();
    }

    private void RecolorNote(BoardItemView view, NoteColor color)
    {
        var model = view.Model;
        var before = model.Color;
        if (before == color) return;

        _undo.Push("Note color",
            undo: () => { model.Color = before; RebuildItem(model); ScheduleSave(); },
            redo: () => { model.Color = color; RebuildItem(model); ScheduleSave(); });

        model.Color = color;
        RebuildItem(model);
        ScheduleSave();
    }

    /// <summary>
    /// Recreates the attached view for a model (used after recolor). Resolves by model
    /// reference so undo/redo closures stay valid across rebuilds.
    /// </summary>
    private void RebuildItem(BoardItemModel model)
    {
        var current = _items.FirstOrDefault(v => ReferenceEquals(v.Model, model));
        if (current is null) return;

        bool wasSelected = _selection.Contains(current);
        DetachItem(current);

        var fresh = CreateView(model);
        AttachItem(fresh);
        if (wasSelected) { _selection.Add(fresh); fresh.SetSelected(true); }
    }

    private int NextZ() => _items.Count == 0 ? 0 : _items.Max(v => v.Model.Z) + 1;

    // ---- Z-order ----

    private void Reorder(BoardItemView view, int direction, bool toEnd)
    {
        var ordered = _items.OrderBy(v => v.Model.Z).ToList();
        int index = ordered.IndexOf(view);
        if (index < 0) return;

        int target = toEnd
            ? (direction > 0 ? ordered.Count - 1 : 0)
            : Math.Clamp(index + direction, 0, ordered.Count - 1);
        if (target == index) return;

        var before = ordered.Select(v => (v, v.Model.Z)).ToList();
        ordered.RemoveAt(index);
        ordered.Insert(target, view);
        var after = ordered.Select((v, i) => (v, i)).ToList();

        _undo.Push("Reorder",
            undo: () => { foreach (var (v, z) in before) { v.Model.Z = z; v.ApplyModelBounds(); } ScheduleSave(); },
            redo: () => { foreach (var (v, z) in after) { v.Model.Z = z; v.ApplyModelBounds(); } ScheduleSave(); });

        foreach (var (v, z) in after) { v.Model.Z = z; v.ApplyModelBounds(); }
        ScheduleSave();
    }

    // ---- Creation ----

    private Point ViewportCenterOnCanvas()
    {
        var p = new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);
        return Viewport.TranslatePoint(p, ItemsCanvas);
    }

    private double JitterRotation() => (_rng.NextDouble() - 0.5) * 5.0; // ±2.5°

    private void AddNoteAtCenter()
    {
        var center = ViewportCenterOnCanvas();
        double offset = (_noteCascade++ % 6) * 26;
        CreateNoteAt(new Point(center.X - 105 + offset, center.Y - 130 + offset), "", edit: true);
    }

    private void CreateNoteAt(Point pos, string text, bool edit)
    {
        var model = new BoardItemModel
        {
            Kind = BoardItemKind.Note,
            X = pos.X, Y = pos.Y, W = 210, H = 190,
            Rotation = JitterRotation(),
            Z = NextZ(),
            Text = text,
            FontSize = 22,
            Color = NoteColor.Yellow,
        };
        var view = CreateView(model);
        AddItemWithUndo(view, "Add note");
        if (edit) Dispatcher.BeginInvoke(new Action(view.BeginEdit),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AddReminderAtCenter()
    {
        var center = ViewportCenterOnCanvas();
        double offset = (_noteCascade++ % 6) * 26;
        CreateReminderAt(new Point(center.X - 150 + offset, center.Y - 40 + offset));
    }

    private void CreateReminderAt(Point pos)
    {
        var model = new BoardItemModel
        {
            Kind = BoardItemKind.Reminder,
            X = pos.X, Y = pos.Y,
            W = 300, H = 78,
            Rotation = JitterRotation() * 0.5,
            Z = NextZ(),
            FontSize = 20,
        };
        var view = CreateView(model);
        AddItemWithUndo(view, "Add reminder");
        Dispatcher.BeginInvoke(new Action(view.BeginEdit),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CreateTextAt(Point pos)
    {
        var model = new BoardItemModel
        {
            Kind = BoardItemKind.Text,
            X = pos.X, Y = pos.Y, W = 300, H = 70,
            Z = NextZ(),
            InkColor = ((uint)_inkColor.A << 24) | ((uint)_inkColor.R << 16)
                     | ((uint)_inkColor.G << 8) | _inkColor.B,
            FontSize = 34,
        };
        var view = CreateView(model);
        AddItemWithUndo(view, "Add text", select: false);
        Dispatcher.BeginInvoke(new Action(view.BeginEdit),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AddImageFromBitmap(BitmapSource bitmap, Point? at = null)
    {
        string? asset = _storage.SaveAsset(bitmap);
        if (asset is null) return;
        AddImageItem(asset, bitmap.PixelWidth, bitmap.PixelHeight, at);
    }

    private void AddImageFromFile(string path, Point? at = null)
    {
        string? asset = _storage.ImportAsset(path);
        if (asset is null) return;

        var probe = BoardItemContent.TryLoadBitmap(new BoardItemModel { Asset = asset });
        AddImageItem(asset, probe?.PixelWidth ?? 480, probe?.PixelHeight ?? 360, at);
    }

    private void AddImageItem(string asset, int pxW, int pxH, Point? at)
    {
        const double maxSide = 480;
        double scale = Math.Min(1.0, maxSide / Math.Max(pxW, pxH));
        double w = Math.Max(72, pxW * scale) + 14; // + mat padding
        double h = Math.Max(72, pxH * scale) + 14;

        Point pos = at ?? ViewportCenterOnCanvas();
        var model = new BoardItemModel
        {
            Kind = BoardItemKind.Image,
            X = pos.X - w / 2, Y = pos.Y - h / 2, W = w, H = h,
            Rotation = JitterRotation() * 0.6,
            Z = NextZ(),
            Asset = asset,
        };
        AddItemWithUndo(CreateView(model), "Add image");
    }

    private void AddLinkCard(string url, Point? at = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        string label = uri.Host + uri.AbsolutePath.TrimEnd('/');
        if (label.Length > 60) label = label[..60] + "…";

        Point pos = at ?? ViewportCenterOnCanvas();
        var model = new BoardItemModel
        {
            Kind = BoardItemKind.Link,
            X = pos.X - 130, Y = pos.Y - 30, W = 260, H = 60,
            Z = NextZ(),
            Text = label,
            Url = uri.AbsoluteUri,
        };
        AddItemWithUndo(CreateView(model), "Add link");
    }

    private void AddFileCard(string path, Point? at = null)
    {
        Point pos = at ?? ViewportCenterOnCanvas();
        var model = new BoardItemModel
        {
            Kind = BoardItemKind.File,
            X = pos.X - 130, Y = pos.Y - 30, W = 260, H = 60,
            Z = NextZ(),
            Text = System.IO.Path.GetFileName(path),
            Path = path,
        };
        AddItemWithUndo(CreateView(model), "Add file");
    }

    private void PickImageFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pin an image to the board",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog(this) == true)
            AddImageFromFile(dlg.FileName);
    }

    // ---- Context menus ----

    private void ShowItemContextMenu(BoardItemView view)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("BoardContextMenu") };

        if (view.Model.Kind is BoardItemKind.Link or BoardItemKind.File)
            menu.Items.Add(MenuItemFor("Open", () => ((IBoardHost)this).ItemOpenRequested(view)));
        else if (view.Editor is not null)
            menu.Items.Add(MenuItemFor("Edit", view.BeginEdit));

        if (view.Model.Kind == BoardItemKind.Reminder)
        {
            menu.Items.Add(MenuItemFor(view.Model.Due is null ? "Set date…" : "Change date…",
                () => ShowDatePicker(view)));
            if (view.Model.Due is not null)
                menu.Items.Add(MenuItemFor("Remove date", () => SetReminderDate(view, null)));
        }

        menu.Items.Add(MenuItemFor(view.Model.Pinned ? "Unpin" : "Pin to board", () => TogglePin(view)));
        menu.Items.Add(MenuItemFor("Duplicate\tCtrl+D", DuplicateSelection));

        if (view.Model.Kind == BoardItemKind.Note)
        {
            var colors = new MenuItem { Header = "Paper color" };
            foreach (NoteColor c in Enum.GetValues<NoteColor>())
            {
                var swatch = new System.Windows.Shapes.Rectangle
                {
                    Width = 14, Height = 14, RadiusX = 2, RadiusY = 2,
                    Fill = new SolidColorBrush(BoardItemContent.PaperColor(c)),
                    Stroke = new SolidColorBrush(MediaColor.FromArgb(0x33, 0, 0, 0)),
                    StrokeThickness = 1,
                };
                var mi = new MenuItem { Header = c.ToString(), Icon = swatch };
                var captured = c;
                mi.Click += (_, _) => RecolorNote(view, captured);
                colors.Items.Add(mi);
            }
            menu.Items.Add(colors);
        }

        var order = new MenuItem { Header = "Order" };
        order.Items.Add(MenuItemFor("Bring to front", () => Reorder(view, +1, toEnd: true)));
        order.Items.Add(MenuItemFor("Bring forward", () => Reorder(view, +1, toEnd: false)));
        order.Items.Add(MenuItemFor("Send backward", () => Reorder(view, -1, toEnd: false)));
        order.Items.Add(MenuItemFor("Send to back", () => Reorder(view, -1, toEnd: true)));
        menu.Items.Add(order);

        if (_selection.Count > 1)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("Group\tCtrl+G", GroupSelection));
        }
        if (_selection.Any(v => !string.IsNullOrEmpty(v.Model.GroupId)))
            menu.Items.Add(MenuItemFor("Ungroup\tCtrl+Shift+G", UngroupSelection));

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Delete\tDel", DeleteSelection));

        menu.PlacementTarget = view;
        menu.IsOpen = true;
    }

    private void ShowBoardContextMenu(Point canvasPos)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("BoardContextMenu") };

        var paste = MenuItemFor("Paste\tCtrl+V", () => PasteFromClipboard(canvasPos));
        paste.IsEnabled = Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()
                          || Clipboard.ContainsText();
        menu.Items.Add(paste);

        menu.Items.Add(MenuItemFor("Add note here", () => CreateNoteAt(canvasPos, "", edit: true)));
        menu.Items.Add(MenuItemFor("Add reminder here", () => CreateReminderAt(canvasPos)));
        menu.Items.Add(MenuItemFor("Add text here", () => CreateTextAt(canvasPos)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Select all\tCtrl+A", SelectAllItems));
        menu.Items.Add(MenuItemFor("Clear board", ClearBoard));

        menu.PlacementTarget = ItemsCanvas;
        menu.IsOpen = true;
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        string[] parts = header.Split('\t');
        var mi = new MenuItem { Header = parts[0] };
        if (parts.Length > 1) mi.InputGestureText = parts[1];
        mi.Click += (_, _) => action();
        return mi;
    }

    // ---- Empty state ----

    private void UpdateEmptyHint()
    {
        bool empty = _mode == OverlayMode.Board && Ink.Strokes.Count == 0 && _items.Count == 0;
        Motion.Animate(EmptyHint, OpacityProperty, empty ? 1 : 0, Motion.Slow);
    }
}
