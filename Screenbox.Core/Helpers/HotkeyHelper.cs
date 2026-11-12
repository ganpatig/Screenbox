using System;
using System.Collections.Generic;
using System.Linq;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;
using Windows.System;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Describes the player actions that keyboard shortcuts can trigger: their default keys, their groups and when they apply.
/// </summary>
/// <remarks>
/// The default keys are the hotkeys of VLC media player 3.0 for Windows, together with the shortcuts that
/// Screenbox already had, such as Page Down for the next item. The punctuation keys assume the US keyboard layout.
/// </remarks>
public static class HotkeyHelper
{
    private const VirtualKeyModifiers Shift = VirtualKeyModifiers.Shift;
    private const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;
    private const VirtualKeyModifiers Alt = VirtualKeyModifiers.Menu;

    // The keys that type "=" and "+", "-", ",", ".", "[" and "]" on the US keyboard layout.
    private const VirtualKey OemPlus = (VirtualKey)0xBB;
    private const VirtualKey OemComma = (VirtualKey)0xBC;
    private const VirtualKey OemMinus = (VirtualKey)0xBD;
    private const VirtualKey OemPeriod = (VirtualKey)0xBE;
    private const VirtualKey OemOpenBracket = (VirtualKey)0xDB;
    private const VirtualKey OemCloseBracket = (VirtualKey)0xDD;

    // The actions in the order in which the shortcut editor lists them, with their default keys.
    private static readonly (PlayerHotkeyAction Action, HotkeyCategory Category, HotkeyBinding[] Keys)[] Definitions =
    [
        (PlayerHotkeyAction.PlayPause, HotkeyCategory.Playback, [new(VirtualKey.Space)]),
        (PlayerHotkeyAction.Stop, HotkeyCategory.Playback, [new(VirtualKey.S)]),
        (PlayerHotkeyAction.Next, HotkeyCategory.Playback, [new(VirtualKey.N), new(VirtualKey.PageDown)]),
        (PlayerHotkeyAction.Previous, HotkeyCategory.Playback, [new(VirtualKey.P), new(VirtualKey.PageUp)]),
        (PlayerHotkeyAction.NextChapter, HotkeyCategory.Playback, [new(VirtualKey.N, Shift)]),
        (PlayerHotkeyAction.PreviousChapter, HotkeyCategory.Playback, [new(VirtualKey.P, Shift)]),
        (PlayerHotkeyAction.Faster, HotkeyCategory.Playback, [new(OemPlus, Shift), new(VirtualKey.Add)]),
        (PlayerHotkeyAction.Slower, HotkeyCategory.Playback, [new(OemMinus), new(VirtualKey.Subtract)]),
        (PlayerHotkeyAction.NormalRate, HotkeyCategory.Playback, [new(OemPlus)]),
        (PlayerHotkeyAction.FasterFine, HotkeyCategory.Playback, [new(OemCloseBracket)]),
        (PlayerHotkeyAction.SlowerFine, HotkeyCategory.Playback, [new(OemOpenBracket)]),
        (PlayerHotkeyAction.FasterStep, HotkeyCategory.Playback, [new(OemPeriod, Shift)]),
        (PlayerHotkeyAction.SlowerStep, HotkeyCategory.Playback, [new(OemComma, Shift)]),
        (PlayerHotkeyAction.NextFrame, HotkeyCategory.Playback, [new(VirtualKey.E), new(OemPeriod)]),
        (PlayerHotkeyAction.PreviousFrame, HotkeyCategory.Playback, [new(OemComma)]),
        (PlayerHotkeyAction.ShowPosition, HotkeyCategory.Playback, [new(VirtualKey.T)]),
        (PlayerHotkeyAction.JumpToTime, HotkeyCategory.Playback, [new(VirtualKey.T, Ctrl)]),
        (PlayerHotkeyAction.CycleLoop, HotkeyCategory.Playback, [new(VirtualKey.L), new(VirtualKey.R, Ctrl)]),
        (PlayerHotkeyAction.ToggleRandom, HotkeyCategory.Playback, [new(VirtualKey.R), new(VirtualKey.S, Ctrl)]),
        (PlayerHotkeyAction.ClearPlayQueue, HotkeyCategory.Playback, [new(VirtualKey.W, Ctrl)]),
        (PlayerHotkeyAction.Play, HotkeyCategory.Playback, [new(VirtualKey.Refresh)]),
        (PlayerHotkeyAction.Pause, HotkeyCategory.Playback, [new(VirtualKey.Stop)]),

        (PlayerHotkeyAction.JumpBackwardExtraShort, HotkeyCategory.Jumping, [new(VirtualKey.Left, Shift)]),
        (PlayerHotkeyAction.JumpForwardExtraShort, HotkeyCategory.Jumping, [new(VirtualKey.Right, Shift)]),
        (PlayerHotkeyAction.JumpBackwardShort, HotkeyCategory.Jumping, [new(VirtualKey.Left), new(VirtualKey.Left, Alt)]),
        (PlayerHotkeyAction.JumpForwardShort, HotkeyCategory.Jumping, [new(VirtualKey.Right), new(VirtualKey.Right, Alt)]),
        (PlayerHotkeyAction.JumpBackwardMedium, HotkeyCategory.Jumping, [new(VirtualKey.Left, Ctrl)]),
        (PlayerHotkeyAction.JumpForwardMedium, HotkeyCategory.Jumping, [new(VirtualKey.Right, Ctrl)]),
        (PlayerHotkeyAction.JumpBackwardLong, HotkeyCategory.Jumping, [new(VirtualKey.Left, Ctrl | Alt)]),
        (PlayerHotkeyAction.JumpForwardLong, HotkeyCategory.Jumping, [new(VirtualKey.Right, Ctrl | Alt)]),
        (PlayerHotkeyAction.JumpToStart, HotkeyCategory.Jumping, [new(VirtualKey.Home)]),
        (PlayerHotkeyAction.JumpToEnd, HotkeyCategory.Jumping, [new(VirtualKey.End)]),
        (PlayerHotkeyAction.JumpTo0Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad0)]),
        (PlayerHotkeyAction.JumpTo10Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad1)]),
        (PlayerHotkeyAction.JumpTo20Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad2)]),
        (PlayerHotkeyAction.JumpTo30Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad3)]),
        (PlayerHotkeyAction.JumpTo40Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad4)]),
        (PlayerHotkeyAction.JumpTo50Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad5)]),
        (PlayerHotkeyAction.JumpTo60Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad6)]),
        (PlayerHotkeyAction.JumpTo70Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad7)]),
        (PlayerHotkeyAction.JumpTo80Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad8)]),
        (PlayerHotkeyAction.JumpTo90Percent, HotkeyCategory.Jumping, [new(VirtualKey.NumberPad9)]),

        (PlayerHotkeyAction.VolumeUp, HotkeyCategory.Audio, [new(VirtualKey.Up), new(VirtualKey.Up, Ctrl)]),
        (PlayerHotkeyAction.VolumeDown, HotkeyCategory.Audio, [new(VirtualKey.Down), new(VirtualKey.Down, Ctrl)]),
        (PlayerHotkeyAction.ToggleMute, HotkeyCategory.Audio, [new(VirtualKey.M)]),
        (PlayerHotkeyAction.CycleAudioTrack, HotkeyCategory.Audio, [new(VirtualKey.B)]),
        (PlayerHotkeyAction.CycleAudioDevice, HotkeyCategory.Audio, [new(VirtualKey.A, Shift)]),
        (PlayerHotkeyAction.AudioDelayUp, HotkeyCategory.Audio, [new(VirtualKey.K)]),
        (PlayerHotkeyAction.AudioDelayDown, HotkeyCategory.Audio, [new(VirtualKey.J)]),

        (PlayerHotkeyAction.CycleSubtitleTrack, HotkeyCategory.Subtitles, [new(VirtualKey.V), new(VirtualKey.C, Ctrl)]),
        (PlayerHotkeyAction.CycleSubtitleTrackReverse, HotkeyCategory.Subtitles, [new(VirtualKey.V, Alt), new(VirtualKey.C, Ctrl | Shift)]),
        (PlayerHotkeyAction.ToggleSubtitles, HotkeyCategory.Subtitles, [new(VirtualKey.V, Shift)]),
        (PlayerHotkeyAction.SubtitleDelayUp, HotkeyCategory.Subtitles, [new(VirtualKey.H)]),
        (PlayerHotkeyAction.SubtitleDelayDown, HotkeyCategory.Subtitles, [new(VirtualKey.G)]),
        (PlayerHotkeyAction.SubtitleSyncBookmarkAudio, HotkeyCategory.Subtitles, [new(VirtualKey.H, Shift)]),
        (PlayerHotkeyAction.SubtitleSyncBookmarkSubtitle, HotkeyCategory.Subtitles, [new(VirtualKey.J, Shift)]),
        (PlayerHotkeyAction.SubtitleSyncApply, HotkeyCategory.Subtitles, [new(VirtualKey.K, Shift)]),
        (PlayerHotkeyAction.SubtitleSyncReset, HotkeyCategory.Subtitles, [new(VirtualKey.K, Ctrl | Shift)]),

        (PlayerHotkeyAction.ToggleFullscreen, HotkeyCategory.Video, [new(VirtualKey.F), new(VirtualKey.F11)]),
        (PlayerHotkeyAction.ExitFullscreen, HotkeyCategory.Video, [new(VirtualKey.Escape)]),
        (PlayerHotkeyAction.CycleAspectRatio, HotkeyCategory.Video, [new(VirtualKey.A)]),
        (PlayerHotkeyAction.CycleCrop, HotkeyCategory.Video, [new(VirtualKey.C)]),
        (PlayerHotkeyAction.ToggleAutoscale, HotkeyCategory.Video, [new(VirtualKey.O)]),
        (PlayerHotkeyAction.ScaleUp, HotkeyCategory.Video, [new(VirtualKey.O, Alt)]),
        (PlayerHotkeyAction.ScaleDown, HotkeyCategory.Video, [new(VirtualKey.O, Alt | Shift)]),
        (PlayerHotkeyAction.NextZoomMode, HotkeyCategory.Video, [new(VirtualKey.Z)]),
        (PlayerHotkeyAction.PreviousZoomMode, HotkeyCategory.Video, [new(VirtualKey.Z, Shift)]),
        (PlayerHotkeyAction.ZoomQuarter, HotkeyCategory.Video, [new(VirtualKey.Number1, Alt)]),
        (PlayerHotkeyAction.ZoomHalf, HotkeyCategory.Video, [new(VirtualKey.Number2, Alt)]),
        (PlayerHotkeyAction.ZoomOriginal, HotkeyCategory.Video, [new(VirtualKey.Number3, Alt)]),
        (PlayerHotkeyAction.ZoomDouble, HotkeyCategory.Video, [new(VirtualKey.Number4, Alt)]),
        (PlayerHotkeyAction.ToggleDeinterlace, HotkeyCategory.Video, [new(VirtualKey.D)]),
        (PlayerHotkeyAction.CycleDeinterlaceMode, HotkeyCategory.Video, [new(VirtualKey.D, Shift)]),
        (PlayerHotkeyAction.SaveFrame, HotkeyCategory.Video, [new(VirtualKey.S, Shift)]),

        (PlayerHotkeyAction.ShowControls, HotkeyCategory.Window, [new(VirtualKey.I)]),
        (PlayerHotkeyAction.ShowContextMenu, HotkeyCategory.Window, [new(VirtualKey.Application)]),
        (PlayerHotkeyAction.ShowPlayQueue, HotkeyCategory.Window, [new(VirtualKey.L, Ctrl)]),
        (PlayerHotkeyAction.OpenFiles, HotkeyCategory.Window, [new(VirtualKey.O, Ctrl), new(VirtualKey.O, Ctrl | Shift)]),
        (PlayerHotkeyAction.ShowMediaInfo, HotkeyCategory.Window, [new(VirtualKey.I, Ctrl), new(VirtualKey.J, Ctrl)]),
        (PlayerHotkeyAction.ToggleCompactOverlay, HotkeyCategory.Window, [new(VirtualKey.M, Ctrl)]),
        (PlayerHotkeyAction.Cast, HotkeyCategory.Window, [new(VirtualKey.K, Ctrl)]),
        (PlayerHotkeyAction.ResizeWindowHalf, HotkeyCategory.Window, [new(VirtualKey.Number1)]),
        (PlayerHotkeyAction.ResizeWindowOriginal, HotkeyCategory.Window, [new(VirtualKey.Number2)]),
        (PlayerHotkeyAction.ResizeWindowOneAndHalf, HotkeyCategory.Window, [new(VirtualKey.Number3)]),
        (PlayerHotkeyAction.ResizeWindowToScreen, HotkeyCategory.Window, [new(VirtualKey.Number4)]),
        (PlayerHotkeyAction.GrowWindow, HotkeyCategory.Window, [new(OemPlus, Ctrl)]),
        (PlayerHotkeyAction.ShrinkWindow, HotkeyCategory.Window, [new(OemMinus, Ctrl)]),
        (PlayerHotkeyAction.ShowKeyboardShortcuts, HotkeyCategory.Window, [new(VirtualKey.F1)]),
    ];

    private static readonly Dictionary<PlayerHotkeyAction, int> DefinitionIndexes =
        Definitions.Select((definition, index) => (definition.Action, index)).ToDictionary(pair => pair.Action, pair => pair.index);

    /// <summary>
    /// Gets every player action that a keyboard shortcut can trigger, in the order of the shortcut editor.
    /// </summary>
    public static IReadOnlyList<PlayerHotkeyAction> Actions { get; } = Definitions.Select(definition => definition.Action).ToArray();

    /// <summary>
    /// Gets the default key combinations of an action.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <returns>The default key combinations, or an empty list if the action has none.</returns>
    public static IReadOnlyList<HotkeyBinding> GetDefaultBindings(PlayerHotkeyAction action)
    {
        return DefinitionIndexes.TryGetValue(action, out int index) ? Definitions[index].Keys : Array.Empty<HotkeyBinding>();
    }

    /// <summary>
    /// Gets the group under which the shortcut editor lists an action.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <returns>The group of the action.</returns>
    public static HotkeyCategory GetCategory(PlayerHotkeyAction action)
    {
        return DefinitionIndexes.TryGetValue(action, out int index) ? Definitions[index].Category : HotkeyCategory.Window;
    }

    /// <summary>
    /// Determines whether a key combination can be assigned to an action.
    /// </summary>
    /// <remarks>
    /// A shortcut needs a key besides the modifiers. The Windows key belongs to the system, and the Tab key moves the
    /// focus, so neither can be used. Gamepad buttons are left to controller navigation.
    /// </remarks>
    /// <param name="binding">The key combination.</param>
    /// <returns><see langword="true"/> if the key combination can be used as a shortcut; otherwise, <see langword="false"/>.</returns>
    public static bool IsAssignable(HotkeyBinding binding)
    {
        return binding.Key is not (VirtualKey.None or VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu
                or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftControl or VirtualKey.RightControl
                or VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows
                or VirtualKey.Tab)
            && (int)binding.Key is < 195 or > 218;
    }

    /// <summary>
    /// Determines whether holding down the key of an action repeats the action.
    /// </summary>
    /// <remarks>
    /// Adjustments repeat like they do in VLC. Toggles, cycles and dialogs only react to the first key press,
    /// so that holding a key does not flicker between states.
    /// </remarks>
    /// <param name="action">The player action.</param>
    /// <returns><see langword="true"/> if the action repeats; otherwise, <see langword="false"/>.</returns>
    public static bool IsRepeatable(PlayerHotkeyAction action)
    {
        return action is PlayerHotkeyAction.Faster
            or PlayerHotkeyAction.Slower
            or PlayerHotkeyAction.FasterFine
            or PlayerHotkeyAction.SlowerFine
            or PlayerHotkeyAction.FasterStep
            or PlayerHotkeyAction.SlowerStep
            or PlayerHotkeyAction.NextFrame
            or PlayerHotkeyAction.PreviousFrame
            or PlayerHotkeyAction.JumpBackwardExtraShort
            or PlayerHotkeyAction.JumpForwardExtraShort
            or PlayerHotkeyAction.JumpBackwardShort
            or PlayerHotkeyAction.JumpForwardShort
            or PlayerHotkeyAction.JumpBackwardMedium
            or PlayerHotkeyAction.JumpForwardMedium
            or PlayerHotkeyAction.JumpBackwardLong
            or PlayerHotkeyAction.JumpForwardLong
            or PlayerHotkeyAction.VolumeUp
            or PlayerHotkeyAction.VolumeDown
            or PlayerHotkeyAction.AudioDelayUp
            or PlayerHotkeyAction.AudioDelayDown
            or PlayerHotkeyAction.SubtitleDelayUp
            or PlayerHotkeyAction.SubtitleDelayDown
            or PlayerHotkeyAction.ScaleUp
            or PlayerHotkeyAction.ScaleDown
            or PlayerHotkeyAction.GrowWindow
            or PlayerHotkeyAction.ShrinkWindow;
    }

    /// <summary>
    /// Determines whether an action also works while the full player is not showing.
    /// </summary>
    /// <remarks>
    /// The on-screen display that confirms most adjustments is only shown in the full player, and the library pages need
    /// keys such as the arrows and Space for navigation. Only actions with visible results elsewhere work from the mini
    /// player or the library.
    /// </remarks>
    /// <param name="action">The player action.</param>
    /// <returns><see langword="true"/> if the action works without the full player; otherwise, <see langword="false"/>.</returns>
    public static bool IsAvailableOutsidePlayer(PlayerHotkeyAction action)
    {
        return action is PlayerHotkeyAction.Play
            or PlayerHotkeyAction.Pause
            or PlayerHotkeyAction.Stop
            or PlayerHotkeyAction.Next
            or PlayerHotkeyAction.Previous
            or PlayerHotkeyAction.JumpToTime
            or PlayerHotkeyAction.CycleLoop
            or PlayerHotkeyAction.ToggleRandom
            or PlayerHotkeyAction.ClearPlayQueue
            or PlayerHotkeyAction.ToggleMute
            or PlayerHotkeyAction.ShowPlayQueue
            or PlayerHotkeyAction.OpenFiles
            or PlayerHotkeyAction.ShowMediaInfo
            or PlayerHotkeyAction.ShowKeyboardShortcuts;
    }

    /// <summary>
    /// Determines whether an action needs a media item in the play queue.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <returns><see langword="true"/> if the action needs media; otherwise, <see langword="false"/>.</returns>
    public static bool RequiresMedia(PlayerHotkeyAction action)
    {
        return action is not (PlayerHotkeyAction.None
            or PlayerHotkeyAction.ShowPlayQueue
            or PlayerHotkeyAction.OpenFiles
            or PlayerHotkeyAction.ShowKeyboardShortcuts);
    }
}
