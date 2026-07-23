using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskBoard.Board;

using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace DeskBoard;

/// <summary>
/// Reminder follow-through: a periodic check fires a Windows notification the day a
/// reminder comes due (once per day, click opens the board), and Hidden mode shows a
/// compact click-through "today strip" of due/overdue reminders in the corner.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromMinutes(5) };

    private static readonly FontFamily StripFont = new("Segoe UI Variable Text, Segoe UI");

    private void StartReminderWatch()
    {
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();
        CheckReminders();
    }

    private IEnumerable<BoardItemModel> DueReminders() =>
        _items.Select(v => v.Model)
              .Where(m => m.Kind == BoardItemKind.Reminder && m.Due is not null
                          && m.Due.Value.Date <= DateTime.Today)
              .OrderBy(m => m.Due);

    private void CheckReminders()
    {
        var pending = DueReminders()
            .Where(m => m.LastNotified is null || m.LastNotified.Value.Date < DateTime.Today)
            .ToList();

        if (pending.Count > 0)
        {
            string title = pending.Count == 1 ? "Reminder" : $"{pending.Count} reminders";
            string body = pending.Count == 1
                ? DescribeReminder(pending[0])
                : string.Join("\n", pending.Take(3).Select(DescribeReminder))
                  + (pending.Count > 3 ? $"\n+{pending.Count - 3} more" : "");

            _trayIcon?.ShowBalloonTip(8000, title, body, WinForms.ToolTipIcon.Info);

            foreach (var m in pending) m.LastNotified = DateTime.Now;
            ScheduleSave();
        }

        UpdateTodayStrip();
    }

    private static string DescribeReminder(BoardItemModel m)
    {
        string text = string.IsNullOrWhiteSpace(m.Text) ? "(untitled reminder)" : m.Text.Trim();
        int overdueDays = (DateTime.Today - m.Due!.Value.Date).Days;
        return overdueDays switch
        {
            0 => $"{text} — due today",
            1 => $"{text} — due yesterday",
            _ => $"{text} — {overdueDays} days overdue",
        };
    }

    /// <summary>Hidden mode keeps due reminders glanceable on the wallpaper corner.</summary>
    private void UpdateTodayStrip()
    {
        TodayStrip.Children.Clear();

        var due = _mode == OverlayMode.Hidden ? DueReminders().ToList() : new List<BoardItemModel>();
        TodayStrip.Visibility = due.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (due.Count == 0) return;

        const int maxShown = 4;
        foreach (var m in due.Take(maxShown))
            TodayStrip.Children.Add(BuildStripCard(m));

        if (due.Count > maxShown)
        {
            TodayStrip.Children.Add(new TextBlock
            {
                Text = $"+{due.Count - maxShown} more on the board",
                FontFamily = StripFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(MediaColor.FromArgb(0xAA, 0x4A, 0x50, 0x58)),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 6, 0),
            });
        }
    }

    private static FrameworkElement BuildStripCard(BoardItemModel m)
    {
        bool overdue = m.Due!.Value.Date < DateTime.Today;
        var accent = overdue
            ? MediaColor.FromRgb(0xC2, 0x32, 0x2A)
            : MediaColor.FromRgb(0xE0, 0x5A, 0x33);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var chip = new Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chipStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chipStack.Children.Add(new TextBlock
        {
            Text = m.Due.Value.Day.ToString(),
            FontFamily = StripFont,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -1, 0, -3),
        });
        chipStack.Children.Add(new TextBlock
        {
            Text = m.Due.Value.ToString("MMM").ToUpperInvariant(),
            FontFamily = StripFont,
            FontSize = 7,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        chip.Child = chipStack;
        Grid.SetColumn(chip, 0);
        row.Children.Add(chip);

        var card = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0xF2, 0xFE, 0xFE, 0xFE)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x24, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 6, 12, 6),
            Margin = new Thickness(7, 0, 0, 0),
            MaxWidth = 260,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = BoardItemContent.SoftShadow(blur: 8, depth: 2, opacity: 0.12),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(m.Text) ? "(untitled reminder)" : m.Text.Trim(),
                FontFamily = StripFont,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(0x2F, 0x32, 0x37)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Grid.SetColumn(card, 1);
        row.Children.Add(card);

        return row;
    }
}
