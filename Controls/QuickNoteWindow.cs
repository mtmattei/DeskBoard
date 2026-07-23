using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DeskBoard.Controls;

/// <summary>
/// Ctrl+Alt+N quick capture: a small sticky-note-styled input that floats over
/// whatever the user is doing. Enter pins the text to the board without ever
/// opening it; Shift+Enter inserts a newline; Esc cancels.
/// </summary>
public sealed class QuickNoteWindow : Window
{
    public event Action<string>? Committed;

    private readonly TextBox _box;
    private readonly TextBlock _hint;

    public QuickNoteWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var paper = new Border
        {
            Width = 340,
            MinHeight = 120,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0x8C)),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(18), // room for the shadow
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 5,
                Direction = 270,
                Color = Color.FromRgb(0x20, 0x24, 0x28),
                Opacity = 0.30,
            },
        };

        var grid = new Grid();

        // Adhesive band, same language as board notes.
        grid.Children.Add(new Border
        {
            Height = 20,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x12, 0, 0, 0), Color.FromArgb(0x00, 0, 0, 0), 90),
        });

        _box = new TextBox
        {
            FontFamily = new FontFamily("Ink Free, Segoe Print, Comic Sans MS, Segoe UI"),
            FontSize = 21,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x32, 0x37)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Margin = new Thickness(14, 16, 14, 24),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        grid.Children.Add(_box);

        _hint = new TextBlock
        {
            Text = "Enter pins to the board · Esc cancels",
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0x4A, 0x42, 0x30)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 6),
            IsHitTestVisible = false,
        };
        grid.Children.Add(_hint);

        paper.Child = grid;
        Content = paper;

        // Bottom-center of the primary work area, clear of the taskbar.
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - 376) / 2;
        Top = area.Bottom - 280;

        _box.PreviewKeyDown += OnKey;
        Deactivated += (_, _) => Close(); // click-away cancels
        Loaded += (_, _) => { _box.Focus(); };
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            string text = _box.Text.Trim();
            if (text.Length > 0) Committed?.Invoke(text);
            Close();
            e.Handled = true;
        }
    }
}
