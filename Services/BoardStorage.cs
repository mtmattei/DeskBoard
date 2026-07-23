using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Ink;
using System.Windows.Media.Imaging;
using DeskBoard.Board;

namespace DeskBoard.Services;

/// <summary>
/// Owns everything under %AppData%\DeskBoard: strokes (board.isf), items (board.json v2),
/// and pasted/dropped image assets (assets\{guid}.png). Migrates the v1 bare-array
/// board.json on first load. All IO is best-effort: a corrupt file is renamed .bak and
/// the app starts clean rather than crashing.
/// </summary>
public sealed class BoardStorage
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskBoard");
    public static readonly string StrokesPath = Path.Combine(Dir, "board.isf");
    public static readonly string ItemsPath = Path.Combine(Dir, "board.json");
    public static readonly string MagnetsPath = Path.Combine(Dir, "magnets.json");
    public static readonly string AssetsDir = Path.Combine(Dir, "assets");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class BoardDocument
    {
        public int Version { get; set; } = 2;
        public List<BoardItemModel> Items { get; set; } = new();
    }

    // v1 record shape (bare array of these) — kept only for migration.
    private sealed class V1Item
    {
        public string Type { get; set; } = "text";
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public string Text { get; set; } = "";
        public uint Color { get; set; }
        public double FontSize { get; set; }
    }

    public event Action<string>? Error;

    public StrokeCollection LoadStrokes()
    {
        try
        {
            if (File.Exists(StrokesPath))
            {
                using var fs = File.OpenRead(StrokesPath);
                return new StrokeCollection(fs);
            }
        }
        catch (Exception ex)
        {
            Quarantine(StrokesPath);
            Error?.Invoke($"Saved ink could not be read ({ex.GetType().Name}); starting with a clean board.");
        }
        return new StrokeCollection();
    }

    public void SaveStrokes(StrokeCollection strokes)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            using var fs = File.Create(StrokesPath);
            strokes.Save(fs);
        }
        catch { /* best-effort */ }
    }

    public List<BoardItemModel> LoadItems()
    {
        try
        {
            if (!File.Exists(ItemsPath)) return new List<BoardItemModel>();
            string json = File.ReadAllText(ItemsPath);

            using (var probe = JsonDocument.Parse(json))
            {
                if (probe.RootElement.ValueKind == JsonValueKind.Array)
                    return MigrateV1(json);
            }

            var doc = JsonSerializer.Deserialize<BoardDocument>(json, JsonOpts);
            return doc?.Items ?? new List<BoardItemModel>();
        }
        catch (Exception ex)
        {
            Quarantine(ItemsPath);
            Error?.Invoke($"Saved board items could not be read ({ex.GetType().Name}); starting clean.");
            return new List<BoardItemModel>();
        }
    }

    public void SaveItems(List<BoardItemModel> items)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ItemsPath,
                JsonSerializer.Serialize(new BoardDocument { Items = items }, JsonOpts));
        }
        catch { /* best-effort */ }
    }

    private static List<BoardItemModel> MigrateV1(string json)
    {
        var result = new List<BoardItemModel>();
        var old = JsonSerializer.Deserialize<List<V1Item>>(json);
        if (old is null) return result;

        int z = 0;
        foreach (var it in old)
        {
            if (it.Type == "note")
            {
                result.Add(new BoardItemModel
                {
                    Kind = BoardItemKind.Note,
                    X = it.X, Y = it.Y,
                    W = it.W <= 0 ? 210 : it.W,
                    H = it.H <= 0 ? 180 : it.H,
                    Text = it.Text,
                    FontSize = it.FontSize,
                    Z = z++,
                });
            }
            else
            {
                result.Add(new BoardItemModel
                {
                    Kind = BoardItemKind.Text,
                    X = it.X, Y = it.Y,
                    Text = it.Text,
                    InkColor = it.Color,
                    FontSize = it.FontSize,
                    Z = z++,
                });
            }
        }
        return result;
    }

    /// <summary>Tool-magnet positions (id → [left, top]); null when never saved.</summary>
    public Dictionary<string, double[]>? LoadMagnets()
    {
        try
        {
            if (!File.Exists(MagnetsPath)) return null;
            return JsonSerializer.Deserialize<Dictionary<string, double[]>>(
                File.ReadAllText(MagnetsPath));
        }
        catch
        {
            Quarantine(MagnetsPath);
            return null;
        }
    }

    public void SaveMagnets(Dictionary<string, double[]> positions)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(MagnetsPath, JsonSerializer.Serialize(positions));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Writes a BitmapSource to assets\ and returns the stored file name.</summary>
    public string? SaveAsset(BitmapSource image)
    {
        try
        {
            Directory.CreateDirectory(AssetsDir);
            string name = Guid.NewGuid().ToString("N") + ".png";
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            using var fs = File.Create(Path.Combine(AssetsDir, name));
            enc.Save(fs);
            return name;
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Image could not be saved ({ex.GetType().Name}).");
            return null;
        }
    }

    /// <summary>Copies an image file into assets\ and returns the stored file name.</summary>
    public string? ImportAsset(string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(AssetsDir);
            string name = Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant();
            File.Copy(sourcePath, Path.Combine(AssetsDir, name));
            return name;
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Image could not be imported ({ex.GetType().Name}).");
            return null;
        }
    }

    public static string AssetPath(string assetName) => Path.Combine(AssetsDir, assetName);

    private static void Quarantine(string path)
    {
        try { File.Move(path, path + ".bak", overwrite: true); } catch { /* ignore */ }
    }
}
