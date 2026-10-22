namespace PixlReplay.Engine.Edit;

/// <summary>The edits made, for undo and redo. A change after an undo drops what was undone.</summary>
public sealed class EditHistory(EditProject start)
{
    readonly List<EditProject> _steps = [start];
    int _at;

    public event Action? Changed;

    public EditProject Current => _steps[_at];
    public bool CanUndo => _at > 0;
    public bool CanRedo => _at < _steps.Count - 1;

    public void Push(EditProject next)
    {
        if (ReferenceEquals(next, Current))
            return;
        _steps.RemoveRange(_at + 1, _steps.Count - _at - 1);
        _steps.Add(next);
        _at++;
        Changed?.Invoke();
    }

    /// <summary>Replaces the current step (a slider still being dragged: one step to undo, not one per move).</summary>
    public void Amend(EditProject next)
    {
        if (_at == 0)
        {
            Push(next);
            return;
        }
        _steps.RemoveRange(_at + 1, _steps.Count - _at - 1);
        _steps[_at] = next;
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (!CanUndo)
            return false;
        _at--;
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
            return false;
        _at++;
        Changed?.Invoke();
        return true;
    }
}
