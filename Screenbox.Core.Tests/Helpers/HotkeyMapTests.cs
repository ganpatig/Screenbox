using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Windows.System;

namespace Screenbox.Core.Tests.Helpers;

public class HotkeyMapTests
{
    // The test generator reads these constants from its own class, so they cannot be private.
    internal const VirtualKey OemPlus = (VirtualKey)0xBB;
    internal const VirtualKey OemMinus = (VirtualKey)0xBD;

    [Test]
    [Arguments(OemPlus, VirtualKeyModifiers.Shift, PlayerHotkeyAction.Faster)]
    [Arguments(VirtualKey.Add, VirtualKeyModifiers.None, PlayerHotkeyAction.Faster)]
    [Arguments(OemPlus, VirtualKeyModifiers.None, PlayerHotkeyAction.NormalRate)]
    [Arguments(OemMinus, VirtualKeyModifiers.None, PlayerHotkeyAction.Slower)]
    [Arguments((VirtualKey)0xDD, VirtualKeyModifiers.None, PlayerHotkeyAction.FasterFine)]
    [Arguments((VirtualKey)0xDB, VirtualKeyModifiers.None, PlayerHotkeyAction.SlowerFine)]
    [Arguments(OemPlus, VirtualKeyModifiers.Control, PlayerHotkeyAction.GrowWindow)]
    [Arguments(VirtualKey.Space, VirtualKeyModifiers.None, PlayerHotkeyAction.PlayPause)]
    [Arguments(VirtualKey.S, VirtualKeyModifiers.None, PlayerHotkeyAction.Stop)]
    [Arguments(VirtualKey.S, VirtualKeyModifiers.Shift, PlayerHotkeyAction.SaveFrame)]
    [Arguments(VirtualKey.S, VirtualKeyModifiers.Control, PlayerHotkeyAction.ToggleRandom)]
    [Arguments(VirtualKey.PageDown, VirtualKeyModifiers.None, PlayerHotkeyAction.Next)]
    [Arguments(VirtualKey.N, VirtualKeyModifiers.Shift, PlayerHotkeyAction.NextChapter)]
    [Arguments(VirtualKey.Left, VirtualKeyModifiers.None, PlayerHotkeyAction.JumpBackwardShort)]
    [Arguments(VirtualKey.Left, VirtualKeyModifiers.Menu, PlayerHotkeyAction.JumpBackwardShort)]
    [Arguments(VirtualKey.Left, VirtualKeyModifiers.Shift, PlayerHotkeyAction.JumpBackwardExtraShort)]
    [Arguments(VirtualKey.Right, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, PlayerHotkeyAction.JumpForwardLong)]
    [Arguments(VirtualKey.NumberPad5, VirtualKeyModifiers.None, PlayerHotkeyAction.JumpTo50Percent)]
    [Arguments(VirtualKey.F, VirtualKeyModifiers.None, PlayerHotkeyAction.ToggleFullscreen)]
    [Arguments(VirtualKey.F11, VirtualKeyModifiers.None, PlayerHotkeyAction.ToggleFullscreen)]
    [Arguments(VirtualKey.Escape, VirtualKeyModifiers.None, PlayerHotkeyAction.ExitFullscreen)]
    [Arguments(VirtualKey.M, VirtualKeyModifiers.None, PlayerHotkeyAction.ToggleMute)]
    [Arguments(VirtualKey.M, VirtualKeyModifiers.Control, PlayerHotkeyAction.ToggleCompactOverlay)]
    [Arguments(VirtualKey.K, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, PlayerHotkeyAction.SubtitleSyncReset)]
    [Arguments(VirtualKey.Number4, VirtualKeyModifiers.Menu, PlayerHotkeyAction.ZoomDouble)]
    [Arguments(VirtualKey.Number4, VirtualKeyModifiers.None, PlayerHotkeyAction.ResizeWindowToScreen)]
    [Arguments(VirtualKey.F1, VirtualKeyModifiers.None, PlayerHotkeyAction.ShowKeyboardShortcuts)]
    public async Task Default_ShouldAssignVlcAndScreenboxKeys(VirtualKey key, VirtualKeyModifiers modifiers, PlayerHotkeyAction expected)
    {
        bool isShortcut = HotkeyMap.Default.TryGetAction(new HotkeyBinding(key, modifiers), out PlayerHotkeyAction action);

        await Assert.That(isShortcut).IsTrue();
        await Assert.That(action).IsEqualTo(expected);
    }

    [Test]
    public async Task Default_ShouldNotAssignAKeyTwice()
    {
        List<HotkeyBinding> bindings = HotkeyHelper.Actions.SelectMany(HotkeyHelper.GetDefaultBindings).ToList();

        await Assert.That(bindings.Distinct().Count()).IsEqualTo(bindings.Count);
    }

    [Test]
    public async Task Default_ShouldListEveryActionOnce()
    {
        PlayerHotkeyAction[] expected = Enum.GetValues<PlayerHotkeyAction>().Where(action => action is not PlayerHotkeyAction.None).ToArray();

        await Assert.That(HotkeyHelper.Actions.Count).IsEqualTo(expected.Length);
        await Assert.That(HotkeyHelper.Actions.Distinct().Count()).IsEqualTo(expected.Length);
    }

    [Test]
    public async Task Default_ShouldOnlyUseAssignableKeys()
    {
        bool allAssignable = HotkeyHelper.Actions.SelectMany(HotkeyHelper.GetDefaultBindings).All(HotkeyHelper.IsAssignable);

        await Assert.That(allAssignable).IsTrue();
    }

    [Test]
    public async Task SetBinding_ShouldMoveTheKeyFromTheOtherAction()
    {
        HotkeyBinding equals = new(OemPlus);

        HotkeyMap map = HotkeyMap.Default.SetBinding(PlayerHotkeyAction.Faster, equals, keepExistingBindings: false);

        await Assert.That(map.TryGetAction(equals, out PlayerHotkeyAction action)).IsTrue();
        await Assert.That(action).IsEqualTo(PlayerHotkeyAction.Faster);
        await Assert.That(map.GetBindings(PlayerHotkeyAction.Faster)).IsEquivalentTo(new[] { equals });
        await Assert.That(map.GetBindings(PlayerHotkeyAction.NormalRate).Count).IsEqualTo(0);
        await Assert.That(map.TryGetAction(new HotkeyBinding(OemPlus, VirtualKeyModifiers.Shift), out _)).IsFalse();
    }

    [Test]
    public async Task SetBinding_ShouldKeepExistingBindingsWhenAdding()
    {
        HotkeyBinding q = new(VirtualKey.Q);

        HotkeyMap map = HotkeyMap.Default.SetBinding(PlayerHotkeyAction.Faster, q, keepExistingBindings: true);

        await Assert.That(map.GetBindings(PlayerHotkeyAction.Faster).Count).IsEqualTo(3);
        await Assert.That(map.TryGetAction(q, out PlayerHotkeyAction action)).IsTrue();
        await Assert.That(action).IsEqualTo(PlayerHotkeyAction.Faster);
        await Assert.That(map.IsDefault(PlayerHotkeyAction.Faster)).IsFalse();
    }

    [Test]
    public async Task ClearBindings_ShouldFreeTheKeys()
    {
        HotkeyMap map = HotkeyMap.Default.ClearBindings(PlayerHotkeyAction.Stop);

        await Assert.That(map.GetBindings(PlayerHotkeyAction.Stop).Count).IsEqualTo(0);
        await Assert.That(map.TryGetAction(new HotkeyBinding(VirtualKey.S), out _)).IsFalse();
    }

    [Test]
    public async Task ResetBindings_ShouldTakeTheDefaultKeysBack()
    {
        HotkeyBinding equals = new(OemPlus);
        HotkeyMap changed = HotkeyMap.Default.SetBinding(PlayerHotkeyAction.Faster, equals, keepExistingBindings: false);

        HotkeyMap map = changed.ResetBindings(PlayerHotkeyAction.NormalRate);

        await Assert.That(map.IsDefault(PlayerHotkeyAction.NormalRate)).IsTrue();
        await Assert.That(map.TryGetAction(equals, out PlayerHotkeyAction action)).IsTrue();
        await Assert.That(action).IsEqualTo(PlayerHotkeyAction.NormalRate);
        await Assert.That(map.GetBindings(PlayerHotkeyAction.Faster).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Serialize_ShouldOnlyStoreChangedActions()
    {
        await Assert.That(HotkeyMap.Default.Serialize()).IsEqualTo(string.Empty);

        HotkeyMap map = HotkeyMap.Default.SetBinding(PlayerHotkeyAction.Faster, new HotkeyBinding(OemPlus), keepExistingBindings: false);

        await Assert.That(map.Serialize()).IsEqualTo("Faster=0:187;NormalRate=");
    }

    [Test]
    public async Task Parse_ShouldRestoreSerializedMaps()
    {
        HotkeyMap map = HotkeyMap.Default
            .SetBinding(PlayerHotkeyAction.Faster, new HotkeyBinding(OemPlus), keepExistingBindings: false)
            .SetBinding(PlayerHotkeyAction.Stop, new HotkeyBinding(VirtualKey.X, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift), keepExistingBindings: true)
            .ClearBindings(PlayerHotkeyAction.ClearPlayQueue);

        HotkeyMap parsed = HotkeyMap.Parse(map.Serialize());

        foreach (PlayerHotkeyAction action in HotkeyHelper.Actions)
        {
            await Assert.That(parsed.GetBindings(action)).IsEquivalentTo(map.GetBindings(action));
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("garbage")]
    [Arguments("Unknown=0:65;=;Faster")]
    public async Task Parse_ShouldUseDefaultsForInvalidText(string? text)
    {
        HotkeyMap map = HotkeyMap.Parse(text);

        foreach (PlayerHotkeyAction action in HotkeyHelper.Actions)
        {
            await Assert.That(map.IsDefault(action)).IsTrue();
        }
    }

    [Test]
    public async Task Parse_ShouldSkipInvalidAndUnassignableKeys()
    {
        // 91 is the Windows key and 9 is Tab, which cannot be shortcuts.
        HotkeyMap map = HotkeyMap.Parse("Faster=4:187,x,0:91,0:9,99:65,4:187");

        await Assert.That(map.GetBindings(PlayerHotkeyAction.Faster)).IsEquivalentTo(new[] { new HotkeyBinding(OemPlus, VirtualKeyModifiers.Shift) });
    }

    [Test]
    public async Task HotkeyBinding_ShouldRoundTripThroughStorageText()
    {
        HotkeyBinding binding = new(VirtualKey.Left, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu);

        bool isValid = HotkeyBinding.TryParse(binding.ToStorageString(), out HotkeyBinding parsed);

        await Assert.That(binding.ToStorageString()).IsEqualTo("3:37");
        await Assert.That(isValid).IsTrue();
        await Assert.That(parsed).IsEqualTo(binding);
    }

    [Test]
    public async Task HotkeyHelper_ShouldClassifyActions()
    {
        await Assert.That(HotkeyHelper.IsRepeatable(PlayerHotkeyAction.Faster)).IsTrue();
        await Assert.That(HotkeyHelper.IsRepeatable(PlayerHotkeyAction.VolumeUp)).IsTrue();
        await Assert.That(HotkeyHelper.IsRepeatable(PlayerHotkeyAction.PlayPause)).IsFalse();
        await Assert.That(HotkeyHelper.IsRepeatable(PlayerHotkeyAction.ToggleFullscreen)).IsFalse();
        await Assert.That(HotkeyHelper.IsAvailableOutsidePlayer(PlayerHotkeyAction.PlayPause)).IsFalse();
        await Assert.That(HotkeyHelper.IsAvailableOutsidePlayer(PlayerHotkeyAction.JumpForwardShort)).IsFalse();
        await Assert.That(HotkeyHelper.IsAvailableOutsidePlayer(PlayerHotkeyAction.ToggleMute)).IsTrue();
        await Assert.That(HotkeyHelper.RequiresMedia(PlayerHotkeyAction.OpenFiles)).IsFalse();
        await Assert.That(HotkeyHelper.RequiresMedia(PlayerHotkeyAction.Stop)).IsTrue();
        await Assert.That(HotkeyHelper.GetCategory(PlayerHotkeyAction.SaveFrame)).IsEqualTo(HotkeyCategory.Video);
        await Assert.That(HotkeyHelper.IsAssignable(new HotkeyBinding(VirtualKey.Shift))).IsFalse();
        await Assert.That(HotkeyHelper.IsAssignable(new HotkeyBinding(VirtualKey.GamepadA))).IsFalse();
    }
}
