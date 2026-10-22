using System.ComponentModel;
using System.Windows.Input;
using PixlReplay.App;

namespace PixlReplay.Ui;

/// <summary>
/// One of PixlReplay's keys as the engine reports it: reserved, or held by another app (NVIDIA named, as its shortcuts are
/// known), with a free key to take instead in one click.
/// </summary>
public sealed class KeyState(string action) : INotifyPropertyChanged
{
    /// <summary>"save", "toggle", "record", "pauseRecord" or "screenshot" (the engine's names).</summary>
    public string Action => action;
    public bool Taken { get; private set; }
    /// <summary>"Held by NVIDIA" / "Held by another app".</summary>
    public string TakenText { get; private set; } = "";
    /// <summary>The app holding it, as said ("NVIDIA" or "another app").</summary>
    public string Owner { get; private set; } = "";
    /// <summary>A free key on offer, or null.</summary>
    public string? FreeKey { get; private set; }
    public bool CanFix => Taken && FreeKey != null;
    public string FixText => FreeKey == null ? "" : Text.Format("hotkeys.useFree", FreeKey);
    public ICommand? FixCommand { get; internal set; }

    internal void Update(bool taken, string owner, string? freeKey)
    {
        Taken = taken;
        Owner = taken ? owner : "";
        TakenText = taken ? Text.Format("hotkeys.takenBy", owner) : "";
        FreeKey = taken ? freeKey : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        CommandManager.InvalidateRequerySuggested();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
