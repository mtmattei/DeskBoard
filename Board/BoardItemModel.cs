using System;

namespace DeskBoard.Board;

public enum BoardItemKind
{
    Note,
    Text,
    Image,
    Link,
    File,
    Reminder,
}

/// <summary>Note paper colors — values are the paper fill; borders derive from them.</summary>
public enum NoteColor
{
    Yellow,
    Blue,
    Pink,
    Green,
}

/// <summary>
/// Serializable state of one board object. The view (<see cref="BoardItemView"/>) reads
/// and writes this directly; persistence serializes the list as board.json v2.
/// </summary>
public sealed class BoardItemModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public BoardItemKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Rotation { get; set; }
    public int Z { get; set; }
    public bool Pinned { get; set; }
    public string? GroupId { get; set; }

    // Kind-specific payload (unused fields stay default and serialize compactly).
    public string Text { get; set; } = "";
    public NoteColor Color { get; set; } = NoteColor.Yellow;
    public uint InkColor { get; set; }      // text items: ARGB of the marker color
    public double FontSize { get; set; }
    public string? Asset { get; set; }      // image items: file name under assets\
    public string? Url { get; set; }        // link items
    public string? Path { get; set; }       // file items: absolute path
    public DateTime? Due { get; set; }      // reminder items: target date

    public BoardItemModel Clone() => (BoardItemModel)MemberwiseClone();
}
