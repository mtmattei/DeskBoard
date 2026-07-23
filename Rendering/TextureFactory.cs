using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskBoard.Rendering;

/// <summary>
/// Procedural texture tiles for the board surface and the metal tray, generated once
/// at startup with a fixed seed (identical look every run) and frozen. Bitmap tiles
/// keep the realism without shipping image assets, and stay sharp at any DPI because
/// they tile in device-independent pixels at low contrast.
/// </summary>
internal static class TextureFactory
{
    private const int Seed = 20260722;

    /// <summary>Fine porcelain-grain noise for the dry-erase surface (~2% contrast).</summary>
    public static ImageBrush BoardNoise { get; } = CreateNoise(160, baseAlpha: 10);

    /// <summary>Acrylic's signature fine noise, slightly stronger than the board grain.</summary>
    public static ImageBrush AcrylicNoise { get; } = CreateNoise(128, baseAlpha: 16);

    private static ImageBrush CreateNoise(int size, byte baseAlpha)
    {
        var rng = new Random(Seed);
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Mid-gray flecks at randomized very-low alpha; reads as tooth, not dirt.
            byte v = (byte)(120 + rng.Next(0, 60));
            pixels[i + 0] = v;
            pixels[i + 1] = v;
            pixels[i + 2] = v;
            pixels[i + 3] = (byte)rng.Next(0, baseAlpha + 1);
        }
        return ToTileBrush(pixels, size, size);
    }

    private static ImageBrush ToTileBrush(byte[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, w, h),
            Stretch = Stretch.Fill,
        };
        brush.Freeze();
        return brush;
    }
}
