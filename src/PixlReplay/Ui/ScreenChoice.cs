namespace PixlReplay.Ui;

/// <summary>A screen on the Capture page's "More screens" card: ticked, it is recorded beside the main picture.</summary>
public sealed class ScreenChoice(string label, bool isChecked, bool isEnabled, Action<bool> set)
{
    public string Label => label;
    public bool IsEnabled => isEnabled;

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (value != isChecked)
                set(value);
        }
    }
}
