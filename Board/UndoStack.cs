using System;
using System.Collections.Generic;

namespace DeskBoard.Board;

/// <summary>
/// Simple bounded undo/redo stack over delegate edits. Every mutation the user can
/// perceive as "one action" (a stroke, a drag gesture, a delete, a clear) pushes one
/// edit. Applying undo/redo runs inside a suppression scope so re-entrant change
/// notifications (StrokesChanged etc.) don't record echo edits.
/// </summary>
public sealed class UndoStack
{
    private const int Capacity = 200;

    private readonly List<Edit> _undo = new();
    private readonly List<Edit> _redo = new();
    private int _suppress;

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsApplying => _suppress > 0;

    private sealed record Edit(string Name, Action Undo, Action Redo);

    public void Push(string name, Action undo, Action redo)
    {
        if (_suppress > 0) return;
        _undo.Add(new Edit(name, undo, redo));
        if (_undo.Count > Capacity) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(e.Undo);
        _redo.Add(e);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(e.Redo);
        _undo.Add(e);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private void Apply(Action action)
    {
        _suppress++;
        try { action(); }
        finally { _suppress--; }
    }
}
