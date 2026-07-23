using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DeskBoard.Services;

namespace DeskBoard.Board;

/// <summary>Visuals for one item kind, built once per view.</summary>
internal sealed class ItemContent
{
    public required FrameworkElement Root { get; init; }
    public FrameworkElement? Decor { get; init; }     // tape strips etc. — may overhang bounds
    public TextBox? Editor { get; init; }             // inline-editable text region
    public FrameworkElement? DateChip { get; init; }  // reminder items: opens the date picker
    public bool ProportionalResize { get; init; }
    public double MinWidth { get; init; } = 80;
    public double MinHeight { get; init; } = 48;
}

/// <summary>
/// Builds the per-kind content visuals. All static brushes frozen; every shadow is
/// low-opacity gray (2–8%, never pure black) with a hairline ring on cards, per the
/// design tokens.
/// </summary>
internal static class BoardItemContent
{
    public static readonly FontFamily Handwriting = new("Ink Free, Segoe Print, Comic Sans MS, Segoe UI");
    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static readonly Brush CardBg = Frozen(new SolidColorBrush(Color.FromRgb(0xFE, 0xFE, 0xFE)));
    private static readonly Brush CardRing = Frozen(new SolidColorBrush(Color.FromArgb(0x1C, 0, 0, 0)));
    private static readonly Brush InkText = Frozen(new SolidColorBrush(Color.FromRgb(0x2F, 0x32, 0x37)));
    private static readonly Brush DimText = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x76, 0x83)));
    private static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xE0)));

    public static ItemContent Build(BoardItemModel model) => model.Kind switch
    {
        BoardItemKind.Note => BuildNote(model),
        BoardItemKind.Reminder => BuildReminder(model),
        BoardItemKind.Text => BuildText(model),
        BoardItemKind.Image => BuildImage(model),
        BoardItemKind.Link => BuildCard(model, "\uE774", model.Title(), model.UrlDomain()),
        BoardItemKind.File => BuildCard(model, "\uE8A5", model.Title(), model.FileExt()),
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    public static Color PaperColor(NoteColor c) => c switch
    {
        NoteColor.Blue => Color.FromRgb(0xBE, 0xE3, 0xF5),
        NoteColor.Pink => Color.FromRgb(0xF8, 0xC8, 0xCF),
        NoteColor.Green => Color.FromRgb(0xCD, 0xEB, 0xC5),
        _ => Color.FromRgb(0xFF, 0xEE, 0x8C),
    };

    // ---- Sticky note: paper, adhesive band, bottom curl, folded corner ----

    private static ItemContent BuildNote(BoardItemModel model)
    {
        var root = new Grid();

        var paper = new Border
        {
            Background = new SolidColorBrush(PaperColor(model.Color)),
            Effect = SoftShadow(blur: 10, depth: 3, opacity: 0.16),
        };
        root.Children.Add(paper);

        // Adhesive band along the top — slightly darker, reads as the sticky strip.
        var adhesive = new Rectangle
        {
            Height = 22,
            VerticalAlignment = VerticalAlignment.Top,
            Fill = Frozen(new LinearGradientBrush(
                Color.FromArgb(0x12, 0, 0, 0), Color.FromArgb(0x00, 0, 0, 0), 90)),
            IsHitTestVisible = false,
        };
        root.Children.Add(adhesive);

        // Bottom curl: the paper lifts slightly off the board.
        var curl = new Rectangle
        {
            Height = 16,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = Frozen(new LinearGradientBrush(
                Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0x0E, 0, 0, 0), 90)),
            IsHitTestVisible = false,
        };
        root.Children.Add(curl);

        // Folded corner hint, bottom-right.
        var fold = new System.Windows.Shapes.Path
        {
            Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Data = Geometry.Parse("M 0,16 L 16,0 L 16,16 Z"),
            Fill = Frozen(new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0))),
            IsHitTestVisible = false,
        };
        root.Children.Add(fold);

        var editor = MakeEditor(model.Text, model.FontSize <= 0 ? 22 : model.FontSize, InkText);
        editor.Margin = new Thickness(12, 14, 12, 14);
        root.Children.Add(editor);

        return new ItemContent
        {
            Root = root,
            Editor = editor,
            MinWidth = 120,
            MinHeight = 100,
        };
    }

    // ---- Reminder: white card, handwriting text, tear-off calendar chip ----

    private static ItemContent BuildReminder(BoardItemModel model)
    {
        var card = new Border
        {
            Background = CardBg,
            BorderBrush = CardRing,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 12, 8),
            Effect = SoftShadow(blur: 12, depth: 2, opacity: 0.13),
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var chip = BuildCalendarChip(model.Due);
        Grid.SetColumn(chip, 0);
        row.Children.Add(chip);

        var editor = MakeEditor(model.Text, model.FontSize <= 0 ? 20 : model.FontSize, InkText);
        editor.Margin = new Thickness(11, 2, 0, 0);
        editor.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);

        card.Child = row;
        var root = new Grid();
        root.Children.Add(card);

        return new ItemContent
        {
            Root = root,
            Editor = editor,
            DateChip = chip,
            MinWidth = 210,
            MinHeight = 74,
        };
    }

    /// <summary>
    /// Tear-off-calendar chip: colored month band over a white day panel. No date yet
    /// shows a neutral "add date" calendar glyph. Overdue dates flush the band red.
    /// </summary>
    private static FrameworkElement BuildCalendarChip(DateTime? due)
    {
        var chip = new Border
        {
            Width = 46, Height = 50,
            CornerRadius = new CornerRadius(5),
            Background = CardBg,
            BorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0))),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = due is null ? "Add a date" : "Change the date",
            Effect = SoftShadow(blur: 5, depth: 1, opacity: 0.10),
        };

        if (due is null)
        {
            chip.Child = new TextBlock
            {
                Text = "\uE787",
                FontFamily = GlyphFont,
                FontSize = 19,
                Foreground = DimText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return chip;
        }

        bool overdue = due.Value.Date < DateTime.Today;
        var bandColor = overdue
            ? Color.FromRgb(0xC2, 0x32, 0x2A)
            : Color.FromRgb(0xE0, 0x5A, 0x33);

        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var band = new Border
        {
            Background = Frozen(new SolidColorBrush(bandColor)),
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Child = new TextBlock
            {
                Text = due.Value.ToString("MMM").ToUpperInvariant(),
                FontFamily = UiFont,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRow(band, 0);
        stack.Children.Add(band);

        var day = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        day.Children.Add(new TextBlock
        {
            Text = due.Value.Day.ToString(),
            FontFamily = UiFont,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = overdue ? Frozen(new SolidColorBrush(bandColor)) : InkText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -1, 0, -2),
        });
        day.Children.Add(new TextBlock
        {
            Text = due.Value.ToString("ddd").ToUpperInvariant(),
            FontFamily = UiFont,
            FontSize = 7.5,
            Foreground = DimText,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Grid.SetRow(day, 1);
        stack.Children.Add(day);

        chip.Child = stack;
        return chip;
    }

    // ---- Marker text: bare handwriting on the board ----

    private static ItemContent BuildText(BoardItemModel model)
    {
        var color = model.InkColor == 0
            ? Color.FromRgb(0x1A, 0x1A, 0x1A)
            : Color.FromArgb((byte)(model.InkColor >> 24), (byte)(model.InkColor >> 16),
                             (byte)(model.InkColor >> 8), (byte)model.InkColor);

        var editor = MakeEditor(model.Text, model.FontSize <= 0 ? 34 : model.FontSize,
            new SolidColorBrush(color));
        editor.FontWeight = FontWeights.SemiBold;

        var root = new Grid();
        root.Children.Add(editor);

        return new ItemContent { Root = root, Editor = editor, MinWidth = 80, MinHeight = 44 };
    }

    // ---- Image / screenshot: white mat, hairline ring, tape strips ----

    private static ItemContent BuildImage(BoardItemModel model)
    {
        var root = new Grid();
        var mat = new Border
        {
            Background = CardBg,
            BorderBrush = CardRing,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Effect = SoftShadow(blur: 14, depth: 3, opacity: 0.14),
        };

        FrameworkElement inner;
        BitmapImage? bmp = TryLoadBitmap(model);
        if (bmp is not null)
        {
            inner = new Image { Source = bmp, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(inner, BitmapScalingMode.HighQuality);
        }
        else
        {
            // Unavailable state: neutral panel with an error glyph, never a broken image.
            var panel = new Grid { Background = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF4))) };
            panel.Children.Add(new TextBlock
            {
                Text = "\uE783",
                FontFamily = GlyphFont,
                FontSize = 22,
                Foreground = DimText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            inner = panel;
        }

        mat.Child = inner;
        root.Children.Add(mat);

        return new ItemContent
        {
            Root = root,
            Decor = BuildTape(),
            ProportionalResize = true,
            MinWidth = 72,
            MinHeight = 72,
        };
    }

    public static BitmapImage? TryLoadBitmap(BoardItemModel model)
    {
        if (string.IsNullOrEmpty(model.Asset)) return null;
        string path = BoardStorage.AssetPath(model.Asset);
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // decode now, release the file
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static FrameworkElement BuildTape()
    {
        var layer = new Grid { IsHitTestVisible = false };
        layer.Children.Add(TapeStrip(-42, HorizontalAlignment.Left));
        layer.Children.Add(TapeStrip(42, HorizontalAlignment.Right));
        return layer;
    }

    private static FrameworkElement TapeStrip(double angle, HorizontalAlignment side)
    {
        var tape = new Border
        {
            Width = 52, Height = 17,
            Background = Frozen(new SolidColorBrush(Color.FromArgb(0x5E, 0xFF, 0xFD, 0xF2))),
            BorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF))),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = side == HorizontalAlignment.Left
                ? new Thickness(-14, -8, 0, 0)
                : new Thickness(0, -8, -14, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(angle),
            Effect = SoftShadow(blur: 5, depth: 1, opacity: 0.10),
        };
        return tape;
    }

    // ---- Link / file card ----

    private static ItemContent BuildCard(BoardItemModel model, string glyph, string title, string subtitle)
    {
        var card = new Border
        {
            Background = CardBg,
            BorderBrush = CardRing,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 14, 10),
            Effect = SoftShadow(blur: 12, depth: 2, opacity: 0.13),
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = Frozen(new SolidColorBrush(Color.FromArgb(0x1A, 0x2E, 0x6F, 0xE0))),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = GlyphFont,
                FontSize = 15,
                Foreground = Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(badge, 0);
        row.Children.Add(badge);

        var text = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkText,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontFamily = UiFont,
            FontSize = 11,
            Foreground = DimText,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        card.Child = row;
        var root = new Grid();
        root.Children.Add(card);

        return new ItemContent { Root = root, MinWidth = 170, MinHeight = 54 };
    }

    // ---- Push pin: shown on any pinned item ----

    public static FrameworkElement BuildPushPin()
    {
        var layer = new Grid
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -7, 0, 0),
        };

        // Contact shadow under the pin head.
        layer.Children.Add(new Ellipse
        {
            Width = 14, Height = 6,
            Fill = Frozen(new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0))),
            Margin = new Thickness(2, 9, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        });

        var head = new Ellipse
        {
            Width = 13, Height = 13,
            VerticalAlignment = VerticalAlignment.Top,
            Fill = Frozen(new RadialGradientBrush(
                Color.FromRgb(0xF0, 0x6A, 0x5E), Color.FromRgb(0xC2, 0x32, 0x2A))
            { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.4, 0.35) }),
        };
        layer.Children.Add(head);

        // Specular dot.
        layer.Children.Add(new Ellipse
        {
            Width = 4, Height = 4,
            Fill = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF))),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(-4, 2.5, 0, 0),
        });

        return layer;
    }

    // ---- Helpers ----

    private static TextBox MakeEditor(string text, double fontSize, Brush foreground)
    {
        return new TextBox
        {
            Text = text,
            FontFamily = Handwriting,
            FontSize = fontSize,
            Foreground = foreground,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsHitTestVisible = false,
            Focusable = false,
            Cursor = System.Windows.Input.Cursors.IBeam,
        };
    }

    public static DropShadowEffect SoftShadow(double blur, double depth, double opacity) => new()
    {
        BlurRadius = blur,
        ShadowDepth = depth,
        Direction = 270,
        Color = Color.FromRgb(0x20, 0x24, 0x28),
        Opacity = opacity,
    };

    private static TBrush Frozen<TBrush>(TBrush b) where TBrush : Brush
    {
        b.Freeze();
        return b;
    }

    private static string Title(this BoardItemModel m) =>
        string.IsNullOrWhiteSpace(m.Text) ? (m.Kind == BoardItemKind.Link ? "Link" : "File") : m.Text;

    private static string UrlDomain(this BoardItemModel m)
    {
        if (Uri.TryCreate(m.Url, UriKind.Absolute, out var uri)) return uri.Host;
        return m.Url ?? "";
    }

    private static string FileExt(this BoardItemModel m)
    {
        string ext = System.IO.Path.GetExtension(m.Path ?? "").TrimStart('.').ToUpperInvariant();
        return ext.Length == 0 ? "FILE" : ext + " file";
    }
}
