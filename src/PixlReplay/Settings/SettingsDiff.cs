namespace PixlReplay.Settings;

/// <summary>Decides whether a settings change has to rebuild the capture pipeline (which empties the replay buffer).</summary>
public static class SettingsDiff
{
    public static bool NeedsRebuild(AppSettings before, AppSettings after) => PipelineKey(before) != PipelineKey(after);

    /// <summary>
    /// The settings with everything the pipeline ignores (hotkeys, looks, sounds, startup, voice sensitivity, sharing)
    /// reset to defaults. Smart Save on/off stays: it decides whether the microphone source exists.
    /// </summary>
    static string PipelineKey(AppSettings s)
    {
        var copy = SettingsStore.Deserialize(SettingsStore.Serialize(s));
        copy.Hotkeys = new HotkeySettings();
        copy.General = new GeneralSettings { OnlyWhenGaming = s.General.OnlyWhenGaming };
        copy.Voice = new VoiceSettings { Enabled = s.Voice.Enabled };
        copy.Share = new ShareSettings();
        // Instant replay on/off only starts or stops the buffer; a rebuild would also end a full recording.
        copy.Replay.Enabled = true;
        return SettingsStore.Serialize(copy);
    }
}
