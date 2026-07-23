using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeskBoard.Board;
using DeskBoard.Controls;
using DeskBoard.Native;
using DeskBoard.Rendering;
using DeskBoard.Services;

using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using MediaColor = System.Windows.Media.Color;

namespace DeskBoard;

/// <summary>
/// Shell: overlay window lifecycle, Ambient/Board mode switching, global hotkey,
/// tray icon, tool routing, and ink setup. Item management, input, and persistence
/// live in the sibling partials.
/// </summary>
public partial class MainWindow : Window, IBoardHost
{
    /// <summary>
    /// Board = full whiteboard. Ambient = ink and pinned content live on the desktop
    /// wallpaper, click-through. Hidden = nothing rendered at all (hotkey stays live).
    /// Ctrl+Alt+D toggles Board against whichever background mode is chosen.
    /// </summary>
    private enum OverlayMode { Hidden, Ambient, Board }
    private enum Tool { Select, Marker, Eraser, Text }

    private const int HotkeyBoardId = 0xB0A2;
    private const int HotkeyAmbientId = 0xB0A3;
    private const int HotkeyHiddenId = 0xB0A4;
    private const int HotkeyQuickNoteId = 0xB0A5;
    private const int HotkeySnipId = 0xB0A6;
    private const uint VK_D = 0x44;
    private const uint VK_A = 0x41;
    private const uint VK_H = 0x48;
    private const uint VK_N = 0x4E;
    private const uint VK_S = 0x53;

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "deskboard-log.txt");

    private IntPtr _hwnd;
    private OverlayMode _mode = OverlayMode.Ambient;
    private OverlayMode _backgroundMode = OverlayMode.Ambient; // what "hide the board" returns to
    private Tool _tool = Tool.Marker;
    private MediaColor _inkColor = MediaColor.FromRgb(0x1A, 0x1A, 0x1A);
    private bool _loading;

    private readonly BoardStorage _storage = new();
    private readonly UndoStack _undo = new();
    private readonly List<BoardItemView> _items = new();
    private readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _zoomHudTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };

    private readonly TranslateTransform _dockLift = new();
    private WinForms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _appIcon;
    private WinForms.ToolStripMenuItem _menuBoard = null!;
    private WinForms.ToolStripMenuItem _menuAmbient = null!;
    private WinForms.ToolStripMenuItem _menuHidden = null!;
    private WinForms.ToolStripMenuItem _menuPen = null!;
    private WinForms.ToolStripMenuItem _menuEraser = null!;

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}"); }
        catch { /* logging must never crash the app */ }
    }

    public MainWindow()
    {
        InitializeComponent();

        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveAll(); };
        _zoomHudTimer.Tick += (_, _) =>
        {
            _zoomHudTimer.Stop();
            Motion.Animate(ZoomHud, OpacityProperty, 0, Motion.Slow);
        };
        _storage.Error += msg => _trayIcon?.ShowBalloonTip(4000, "DeskBoard", msg, WinForms.ToolTipIcon.Warning);
        _undo.Changed += () => Dock.SetUndoRedo(_undo.CanUndo, _undo.CanRedo);

        Dock.RenderTransform = _dockLift;

        ConfigureInk();
        WireDock();
        WireInput();

        SetTool(Tool.Marker);
        Log("---- MainWindow constructed (v2) ----");
    }

    private void ConfigureInk()
    {
        // Chisel-nib marker: rectangular tip, slightly taller than wide, smoothed to
        // Beziers. Pressure stays on for stylus input.
        var da = Ink.DefaultDrawingAttributes;
        da.StylusTip = StylusTip.Rectangle;
        da.Width = 4.6;
        da.Height = 7.6;
        da.FitToCurve = true;
        da.IgnorePressure = false;
        da.Color = _inkColor;

        Ink.Strokes.StrokesChanged += OnStrokesChanged;
        Ink.StrokeCollected += OnStrokeCollected;
        Ink.PreviewMouseLeftButtonDown += Ink_PreviewMouseLeftButtonDown;
        Ink.PreviewStylusDown += (_, _) => _lastInputWasStylus = true;
        Ink.PreviewMouseDown += (_, e) => { if (e.StylusDevice is null) _lastInputWasStylus = false; };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        SetupTrayIcon();
        RegisterToggleHotkey();
        LoadBoard();
        Dock.ApplyPositions(_storage.LoadMagnets());
        StartReminderWatch();

        ApplyMode(App.StartInBoardMode ? OverlayMode.Board : OverlayMode.Ambient);
    }

    private void RegisterToggleHotkey()
    {
        const uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
        bool okBoard = NativeMethods.RegisterHotKey(_hwnd, HotkeyBoardId, mods, VK_D);
        bool okAmbient = NativeMethods.RegisterHotKey(_hwnd, HotkeyAmbientId, mods, VK_A);
        bool okHidden = NativeMethods.RegisterHotKey(_hwnd, HotkeyHiddenId, mods, VK_H);
        bool okNote = NativeMethods.RegisterHotKey(_hwnd, HotkeyQuickNoteId, mods, VK_N);
        bool okSnip = NativeMethods.RegisterHotKey(_hwnd, HotkeySnipId, mods, VK_S);
        Log($"RegisterHotKey D={okBoard} A={okAmbient} H={okHidden} N={okNote} S={okSnip}");
        if (!okBoard)
            _trayIcon?.ShowBalloonTip(4000, "DeskBoard",
                "Ctrl+Alt+D is already in use. Use the tray icon to toggle the board.",
                WinForms.ToolTipIcon.Warning);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case HotkeyBoardId: ToggleMode(); handled = true; break;
                case HotkeyAmbientId: SetBackgroundMode(OverlayMode.Ambient); handled = true; break;
                case HotkeyHiddenId: SetBackgroundMode(OverlayMode.Hidden); handled = true; break;
                case HotkeyQuickNoteId: OpenQuickNote(); handled = true; break;
                case HotkeySnipId: OpenSnip(); handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    private void ToggleMode()
        => ApplyMode(_mode == OverlayMode.Board ? _backgroundMode : OverlayMode.Board);

    /// <summary>Chooses what "not showing the board" means and switches to it now.</summary>
    private void SetBackgroundMode(OverlayMode mode)
    {
        _backgroundMode = mode;
        ApplyMode(mode);
    }

    private void ApplyMode(OverlayMode mode)
    {
        _mode = mode;
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

        if (mode != OverlayMode.Board)
        {
            CommitAnyEdit();
            ClearSelection();
            ResetView();

            ex |= NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
            Topmost = false;
            AllowDrop = false;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_FRAMECHANGED);

            // Board fades away, magnets drop out.
            Motion.Animate(BoardChrome, OpacityProperty, 0, Motion.Normal,
                completed: () => BoardChrome.Visibility = Visibility.Collapsed);
            Motion.Animate(Dock, OpacityProperty, 0, Motion.Fast,
                completed: () => Dock.Visibility = Visibility.Collapsed);
            Motion.Animate(_dockLift, TranslateTransform.YProperty, 12, Motion.Fast);
            Motion.Animate(EmptyHint, OpacityProperty, 0, Motion.Fast);

            if (mode == OverlayMode.Hidden)
            {
                Motion.Animate(Viewport, OpacityProperty, 0, Motion.Normal,
                    completed: () => Viewport.Visibility = Visibility.Collapsed);
            }
            else
            {
                Viewport.Visibility = Visibility.Visible;
                Motion.Animate(Viewport, OpacityProperty, 1, Motion.Normal);
            }
        }
        else
        {
            ex &= ~NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
            Topmost = true;
            AllowDrop = true;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED);
            Activate();

            Viewport.Visibility = Visibility.Visible;
            Motion.Animate(Viewport, OpacityProperty, 1, Motion.Normal);
            BoardChrome.Visibility = Visibility.Visible;
            Motion.Animate(BoardChrome, OpacityProperty, 1, Motion.Slow);

            Dock.Visibility = Visibility.Visible;
            _dockLift.Y = Motion.Enabled ? 12 : 0;
            Motion.Animate(Dock, OpacityProperty, 1, Motion.Slow);
            Motion.Animate(_dockLift, TranslateTransform.YProperty, 0, Motion.Slow);

            Ink.Focus();
            UpdateEmptyHint();
        }

        Log($"ApplyMode={mode}");
        UpdateTrayIconState();
        UpdateTodayStrip();
    }

    // ---- Quick capture (global, works from any mode) ----

    private QuickNoteWindow? _quickNote;
    private SnipWindow? _snip;

    private void OpenQuickNote()
    {
        if (_quickNote is not null) { _quickNote.Activate(); return; }

        _quickNote = new QuickNoteWindow();
        _quickNote.Committed += text =>
        {
            var center = ViewportCenterOnCanvas();
            double offset = (_noteCascade++ % 6) * 26;
            CreateNoteAt(new Point(center.X - 105 + offset, center.Y - 95 + offset),
                text, edit: false);
        };
        _quickNote.Closed += (_, _) => _quickNote = null;
        _quickNote.Show();
        _quickNote.Activate();
    }

    private void OpenSnip()
    {
        if (_snip is not null) return;

        _snip = new SnipWindow();
        _snip.Captured += bmp =>
        {
            AddImageFromBitmap(bmp);
            ApplyMode(OverlayMode.Board); // show the result where it landed
        };
        _snip.Closed += (_, _) => _snip = null;
        _snip.Show();
        _snip.Activate();
    }

    // ---- Tool routing ----

    private void SetTool(Tool tool)
    {
        if (_tool == Tool.Select && tool != Tool.Select)
            ClearSelection();

        _tool = tool;

        Ink.EditingMode = tool switch
        {
            Tool.Marker => InkCanvasEditingMode.Ink,
            Tool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };
        Ink.IsHitTestVisible = tool is Tool.Marker or Tool.Eraser or Tool.Text;
        ItemsCanvas.IsHitTestVisible = tool == Tool.Select;

        foreach (var item in _items)
            item.RefreshToolState();

        Dock.SetActiveTool(tool switch
        {
            Tool.Select => TrayTool.Select,
            Tool.Eraser => TrayTool.Eraser,
            Tool.Text => TrayTool.Text,
            _ => TrayTool.Marker,
        }, _inkColor);

        UpdateTrayIconState();
    }

    private void SetMarker(MediaColor color)
    {
        _inkColor = color;
        Ink.DefaultDrawingAttributes.Color = color;
        SetTool(Tool.Marker);
    }

    private void WireDock()
    {
        Dock.MarkerPicked += SetMarker;
        Dock.CustomColorRequested += _ => PickColor();
        Dock.EraserPicked += () => SetTool(Tool.Eraser);
        Dock.SelectPicked += () => SetTool(Tool.Select);
        Dock.TextPicked += () => SetTool(Tool.Text);
        Dock.NoteRequested += () => AddNoteAtCenter();
        Dock.ReminderRequested += () => AddReminderAtCenter();
        Dock.ImageRequested += PickImageFile;
        Dock.UndoRequested += () => _undo.Undo();
        Dock.RedoRequested += () => _undo.Redo();
        Dock.ClearRequested += ClearBoard;
        Dock.HideRequested += () => ApplyMode(_backgroundMode);
        Dock.PositionsChanged += p => _storage.SaveMagnets(p);
        Dock.SetUndoRedo(false, false);
    }

    // ---- System tray icon ----

    private void SetupTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        _menuBoard = new WinForms.ToolStripMenuItem("Board (Ctrl+Alt+D)", null, (_, _) => ToggleMode());
        _menuAmbient = new WinForms.ToolStripMenuItem("Ink on desktop (Ctrl+Alt+A)", null,
            (_, _) => SetBackgroundMode(OverlayMode.Ambient));
        _menuHidden = new WinForms.ToolStripMenuItem("Hide everything (Ctrl+Alt+H)", null,
            (_, _) => SetBackgroundMode(OverlayMode.Hidden));
        var noteItem = new WinForms.ToolStripMenuItem("Quick note (Ctrl+Alt+N)", null, (_, _) => OpenQuickNote());
        var snipItem = new WinForms.ToolStripMenuItem("Snip to board (Ctrl+Alt+S)", null, (_, _) => OpenSnip());
        _menuPen = new WinForms.ToolStripMenuItem("Marker", null, (_, _) => SetMarker(_inkColor));
        _menuEraser = new WinForms.ToolStripMenuItem("Eraser", null, (_, _) => SetTool(Tool.Eraser));
        var colorItem = new WinForms.ToolStripMenuItem("Marker color…", null, (_, _) => PickColor());
        var clearItem = new WinForms.ToolStripMenuItem("Clear board", null, (_, _) => ClearBoard());
        var exitItem = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        menu.Items.Add(_menuBoard);
        menu.Items.Add(_menuAmbient);
        menu.Items.Add(_menuHidden);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(noteItem);
        menu.Items.Add(snipItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_menuPen);
        menu.Items.Add(_menuEraser);
        menu.Items.Add(colorItem);
        menu.Items.Add(clearItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _appIcon = CreateTrayDrawingIcon();
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = _appIcon ?? Drawing.SystemIcons.Application,
            Visible = true,
            Text = "DeskBoard — Ctrl+Alt+ D board · A ink · H hide · N note",
            ContextMenuStrip = menu,
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) ToggleMode();
        };
        // A clicked reminder notification opens the board.
        _trayIcon.BalloonTipClicked += (_, _) => ApplyMode(OverlayMode.Board);
        UpdateTrayIconState();
    }

    /// <summary>Tiny whiteboard-with-red-stroke tray icon, drawn at runtime (no assets).</summary>
    private static Drawing.Icon? CreateTrayDrawingIcon()
    {
        try
        {
            using var bmp = new Drawing.Bitmap(16, 16);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using var board = new Drawing.SolidBrush(Drawing.Color.FromArgb(250, 252, 253));
                using var frame = new Drawing.Pen(Drawing.Color.FromArgb(140, 148, 156), 1.6f);
                g.FillRectangle(board, 1.5f, 2.5f, 13, 11);
                g.DrawRectangle(frame, 1.5f, 2.5f, 13, 11);
                using var stroke = new Drawing.Pen(Drawing.Color.FromArgb(229, 57, 53), 1.8f);
                g.DrawBezier(stroke, 4, 10.5f, 6.5f, 5f, 9f, 10f, 12f, 5.5f);
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }
        catch { return null; }
    }

    private void UpdateTrayIconState()
    {
        if (_trayIcon is null) return;
        _menuBoard.Checked = _mode == OverlayMode.Board;
        _menuAmbient.Checked = _backgroundMode == OverlayMode.Ambient;
        _menuHidden.Checked = _backgroundMode == OverlayMode.Hidden;
        _menuPen.Checked = _tool == Tool.Marker;
        _menuEraser.Checked = _tool == Tool.Eraser;
    }

    private void PickColor()
    {
        using var dlg = new WinForms.ColorDialog
        {
            Color = Drawing.Color.FromArgb(_inkColor.R, _inkColor.G, _inkColor.B),
            FullOpen = true,
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            SetMarker(MediaColor.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    private void ClearBoard()
    {
        CommitAnyEdit();
        ClearSelection();

        var strokes = new StrokeCollection(Ink.Strokes);
        var items = new List<BoardItemView>(_items);
        if (strokes.Count == 0 && items.Count == 0) return;

        _undo.Push("Clear board",
            undo: () =>
            {
                Ink.Strokes.Add(strokes);
                foreach (var v in items) AttachItem(v);
                UpdateEmptyHint();
                ScheduleSave();
            },
            redo: () => DoClear(strokes, items));

        DoClear(strokes, items);
    }

    private void DoClear(StrokeCollection strokes, List<BoardItemView> items)
    {
        Ink.Strokes.Remove(strokes);
        foreach (var v in items) DetachItem(v);
        UpdateEmptyHint();
        SaveAll();
    }

    private void ExitApp()
    {
        _saveDebounce.Stop();
        SaveAll();
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyBoardId);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyAmbientId);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyHiddenId);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyQuickNoteId);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeySnipId);
        if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        _appIcon?.Dispose();
        Application.Current.Shutdown();
    }
}
