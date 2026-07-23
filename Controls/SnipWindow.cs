using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using Drawing = System.Drawing;

namespace DeskBoard.Controls;

/// <summary>
/// Ctrl+Alt+S region capture: dims the primary screen, drag a rectangle, and the
/// captured pixels arrive on the board as a taped image. Esc cancels.
/// </summary>
public sealed class SnipWindow : Window
{
    public event Action<BitmapSource>? Captured;

    private readonly Canvas _canvas;
    private readonly Rectangle _selection;
    private bool _dragging;
    private Point _start;
    private double _dpiScaleX = 1, _dpiScaleY = 1;

    public SnipWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = new SolidColorBrush(Color.FromArgb(0x30, 0x10, 0x14, 0x18));

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _canvas = new Canvas();
        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.5,
            Fill = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            Visibility = Visibility.Collapsed,
        };
        _canvas.Children.Add(_selection);
        Content = _canvas;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            Focus();
            if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            {
                _dpiScaleX = target.TransformToDevice.M11;
                _dpiScaleY = target.TransformToDevice.M22;
            }
        };
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(_canvas);
        _selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selection, _start.X);
        Canvas.SetTop(_selection, _start.Y);
        _selection.Width = 0;
        _selection.Height = 0;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var rect = new Rect(_start, e.GetPosition(_canvas));
        Canvas.SetLeft(_selection, rect.X);
        Canvas.SetTop(_selection, rect.Y);
        _selection.Width = rect.Width;
        _selection.Height = rect.Height;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var rect = new Rect(_start, e.GetPosition(_canvas));
        if (rect.Width < 6 || rect.Height < 6) { Close(); return; }

        // Hide, wait a beat for the compositor to drop this window, then grab pixels.
        Hide();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var bmp = Grab(rect);
                if (bmp is not null) Captured?.Invoke(bmp);
            }
            finally { Close(); }
        };
        timer.Start();
    }

    private BitmapSource? Grab(Rect dipRect)
    {
        try
        {
            int x = (int)Math.Round(dipRect.X * _dpiScaleX);
            int y = (int)Math.Round(dipRect.Y * _dpiScaleY);
            int w = Math.Max(1, (int)Math.Round(dipRect.Width * _dpiScaleX));
            int h = Math.Max(1, (int)Math.Round(dipRect.Height * _dpiScaleY));

            using var native = new Drawing.Bitmap(w, h);
            using (var g = Drawing.Graphics.FromImage(native))
                g.CopyFromScreen(x, y, 0, 0, new Drawing.Size(w, h));

            using var ms = new MemoryStream();
            native.Save(ms, Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = ms;
            result.EndInit();
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }
}
