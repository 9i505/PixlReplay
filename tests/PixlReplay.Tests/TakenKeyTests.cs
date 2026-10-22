using System.Windows.Forms;
using PixlReplay.App;
using PixlReplay.Ui;
using Hotkey = PixlReplay.App.Hotkey;

namespace PixlReplay.Tests;

/// <summary>A PixlReplay key another app holds: named (NVIDIA's are known), and swapped for a free one in one click.</summary>
public static class TakenKeyTests
{
    static byte[] Vk(int vk) => [(byte)vk, 0, 0, 0];

    [Test]
    public static void NVIDIAs_keys_are_read_from_its_settings()
    {
        var values = new Dictionary<string, object?>
        {
            ["DVRHKeyCount"] = Vk(2), ["DVRHKey0"] = Vk(0x12), ["DVRHKey1"] = Vk(0x79),                          // Alt+F10
            ["IRToggleHKeyCount"] = Vk(3), ["IRToggleHKey0"] = Vk(0x12), ["IRToggleHKey1"] = Vk(0x10), ["IRToggleHKey2"] = Vk(0x79),
            ["ManualHKeyCount"] = Vk(2), ["ManualHKey0"] = Vk(0x12), ["ManualHKey1"] = Vk(0x78),                 // Alt+F9
            ["FSToggleHKeyCount"] = Vk(0),                                                                        // none
            ["MicToggleHKeyCount"] = Vk(3), ["MicToggleHKey0"] = Vk(0x11), ["MicToggleHKey1"] = Vk(0x12), ["MicToggleHKey2"] = Vk(0x4D),
            ["SomethingElse"] = 5,
        };
        var keys = NvidiaHotkeys.Parse(values);
        Assert.True(keys.Contains(new Hotkey(Keys.F10, false, true, false, false)), "Alt+F10");
        Assert.True(keys.Contains(new Hotkey(Keys.F10, false, true, true, false)), "Alt+Shift+F10");
        Assert.True(keys.Contains(new Hotkey(Keys.F9, false, true, false, false)), "Alt+F9");
        Assert.True(keys.Contains(new Hotkey(Keys.M, true, true, false, false)), "Ctrl+Alt+M");
        Assert.Equal(4, keys.Count, "only real keys");
    }

    [Test]
    public static void A_free_key_is_chosen_for_a_taken_one()
    {
        var taken = new HashSet<string> { "Alt+F10", "Alt+F8" };
        bool Free(Hotkey h) => !taken.Contains(HotkeyParser.Format(h));
        Assert.Equal("Ctrl+F8", HotkeyDefaults.FreeChoice("save", [], Free), "the first free one after the taken ones");
        Assert.Equal("Ctrl+F8", HotkeyDefaults.FreeChoice("save", ["Alt+F10", "Alt+F8"], _ => true),
            "not its own taken key, nor one another of PixlReplay's actions uses");
        Assert.Equal(null, HotkeyDefaults.FreeChoice("save", [], _ => false), "none free: nothing to offer");
    }

    /// <summary>Every key PixlReplay offers instead is two keys (one modifier and one key), never F9, F10 or F11 (OBS's).</summary>
    [Test]
    public static void Suggestions_are_two_easy_keys()
    {
        foreach (var (action, choices) in HotkeyDefaults.Choices)
            foreach (string choice in choices.Skip(1))
            {
                Assert.True(HotkeyParser.TryParse(choice, out var h), choice);
                int modifiers = (h.Ctrl ? 1 : 0) + (h.Alt ? 1 : 0) + (h.Shift ? 1 : 0) + (h.Win ? 1 : 0);
                Assert.Equal(1, modifiers, $"{action}: {choice} is two keys");
                Assert.True(h.Key is not (Keys.F9 or Keys.F10 or Keys.F11), $"{action}: {choice} keeps off OBS's F9-F11");
            }
    }

    [Test]
    public static void OBS_keys_are_read_from_its_settings()
    {
        const string scenes = """{"hotkeys":{"libobs.mute":[{"key": "OBS_KEY_F9"}],"libobs.unmute":[{"key":"OBS_KEY_F9","alt":true}]},"x":[{"key":"OBS_KEY_F11"}]}""";
        const string profile = "[Hotkeys]\nReplayBuffer={\"ReplayBuffer.Save\":[{\"key\":\"OBS_KEY_F10\"}]}\nOther={\"a\":[{\"key\":\"OBS_KEY_M\",\"control\":true}]}";
        var keys = ObsHotkeys.Parse([scenes, profile]);
        Assert.True(keys.SetEquals([Keys.F9, Keys.F10, Keys.F11, Keys.M]), "F9, F10, F11 and M: " + string.Join(",", keys));
    }

    /// <summary>
    /// A key on offer is free in every way PixlReplay can know: no app holds it now, it is not one of NVIDIA's shortcuts
    /// (NVIDIA may take it after the next restart) and OBS does not use its key (OBS reacts to it with any modifiers).
    /// </summary>
    [Test]
    public static void A_key_on_offer_is_not_one_NVIDIA_or_OBS_uses() => OnSta(() =>
    {
        Text.Language = "en";
        var vm = new SettingsViewModel
        {
            SettingsPath = Path.Combine(Path.GetTempPath(), "pixlreplay-keys-" + Guid.NewGuid().ToString("N") + ".json"),
            Send = (_, _) => "ok",
            IsKeyFree = _ => true,
            NvidiaKeys = () => [new Hotkey(Keys.F10, false, true, false, false), new Hotkey(Keys.F8, false, true, false, false)],
            ObsKeys = () => new HashSet<Keys> { Keys.F6 },
        };
        vm.HkSave = "Alt+F10";
        vm.HkToggle = "Alt+Shift+F10";
        vm.SeedReplay(true);
        vm.SeedStatus(new StatusDto(null, new Dictionary<string, bool>
        {
            ["save"] = false, ["toggle"] = false, ["record"] = true, ["pauseRecord"] = true, ["screenshot"] = true,
        }, null, null, false, false, AppInfo.Version));
        Assert.Equal("Ctrl+F8", vm.SaveKey.FreeKey, "not Alt+F8: NVIDIA's");
        Assert.Equal(null, vm.ToggleKey.FreeKey, "neither Alt+F6 nor Ctrl+F6: OBS uses F6");
        Assert.True(vm.ToggleKey.Taken && !vm.ToggleKey.CanFix, "still said, with no button");
    });

    static void OnSta(Action test)
    {
        string? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                error = e.ToString();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.True(error == null, error ?? "");
    }

    /// <summary>
    /// The settings window says which keys another app holds (NVIDIA by name), and one click takes free keys for all of
    /// them: saved at once (only the keys; other unsaved changes stay pending) and sent to the engine.
    /// </summary>
    [Test]
    public static void Taken_keys_are_shown_and_swapped_in_one_click() => OnSta(() =>
    {
        Text.Language = "en";
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var sent = new List<string>();
            var vm = new SettingsViewModel
            {
                SettingsPath = Path.Combine(folder, "settings.json"),
                Send = (message, _) => { sent.Add(message); return "ok"; },
                IsKeyFree = _ => true,
                NvidiaKeys = () => [new Hotkey(Keys.F10, false, true, false, false)],
                ObsKeys = () => new HashSet<Keys>(),
            };
            vm.HkSave = "Alt+F10";
            vm.HkToggle = "Alt+Shift+F10";
            vm.HkRecord = "Alt+F9";
            vm.SeedReplay(true); // these keys as the saved ones (in memory)
            vm.SeedStatus(new StatusDto(null, new Dictionary<string, bool>
            {
                ["save"] = false, ["toggle"] = false, ["record"] = true, ["pauseRecord"] = true, ["screenshot"] = true,
            }, null, null, false, false, AppInfo.Version));

            Assert.True(vm.SaveKey.Taken && vm.SaveKey.TakenText.Contains("NVIDIA"), "save: held by NVIDIA, named");
            Assert.True(vm.ToggleKey.Taken && vm.ToggleKey.TakenText.Contains("another app"), "toggle: held by an app we cannot name");
            Assert.True(!vm.RecordKey.Taken, "record works");
            Assert.Equal("Alt+F8", vm.SaveKey.FreeKey, "an easy free key on offer");
            Assert.True(vm.ShowsFixAll, "two taken: one button for all");

            vm.HkScreenshot = "Ctrl+Shift+Alt+F1"; // an unsaved change elsewhere
            vm.FixAllCommand.Execute(null);
            Assert.True(vm.HkSave == "Alt+F8" && vm.HkToggle == "Alt+F6", $"swapped: {vm.HkSave}, {vm.HkToggle}");
            var saved = PixlReplay.Settings.SettingsStore.Load(vm.SettingsPath);
            Assert.True(saved.Hotkeys.Save == "Alt+F8" && saved.Hotkeys.ToggleBuffer == "Alt+F6", "saved at once");
            Assert.True(saved.Hotkeys.Screenshot != "Ctrl+Shift+Alt+F1" && vm.IsDirty, "the other change stays pending, not saved");
            Assert.True(SpinWait.SpinUntil(() => sent.Contains("apply"), 3000), "the engine takes the new keys");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });
}
