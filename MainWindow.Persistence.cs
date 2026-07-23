using System.Linq;

namespace DeskBoard;

/// <summary>Load/save orchestration over <see cref="Services.BoardStorage"/>.</summary>
public partial class MainWindow
{
    private void LoadBoard()
    {
        _loading = true;
        try
        {
            var strokes = _storage.LoadStrokes();
            Ink.Strokes = strokes;
            // Assigning a new collection drops the old subscription — re-wire it.
            Ink.Strokes.StrokesChanged += OnStrokesChanged;

            foreach (var model in _storage.LoadItems().OrderBy(m => m.Z))
                AttachItem(CreateView(model));

            Log($"Loaded {strokes.Count} strokes, {_items.Count} items");
        }
        finally
        {
            _loading = false;
        }
    }

    private void ScheduleSave()
    {
        if (_loading) return;
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void SaveAll()
    {
        _storage.SaveStrokes(Ink.Strokes);
        _storage.SaveItems(_items.Select(v => v.Model).OrderBy(m => m.Z).ToList());
    }
}
