using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Services;

namespace Screenbox.Helpers;

/// <summary>
/// Provides the localized names of the keyboard shortcut actions and their groups.
/// </summary>
public static class HotkeyLabelHelper
{
    /// <summary>
    /// Gets the name of a group of keyboard shortcuts.
    /// </summary>
    /// <param name="category">The group.</param>
    /// <returns>The localized name of the group.</returns>
    public static string GetCategoryName(HotkeyCategory category)
    {
        return category switch
        {
            HotkeyCategory.Playback => Strings.Resources.ShortcutsPlaybackHeader,
            HotkeyCategory.Jumping => Strings.Resources.ShortcutsJumpHeader,
            HotkeyCategory.Audio => Strings.Resources.ShortcutsAudioHeader,
            HotkeyCategory.Subtitles => Strings.Resources.ShortcutsSubtitlesHeader,
            HotkeyCategory.Video => Strings.Resources.ShortcutsVideoHeader,
            _ => Strings.Resources.ShortcutsInterfaceHeader,
        };
    }

    /// <summary>
    /// Gets the description of the action that a keyboard shortcut triggers.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <param name="settingsService">The settings, which provide the length of the short jumps.</param>
    /// <returns>The localized description of the action.</returns>
    public static string GetDescription(PlayerHotkeyAction action, ISettingsService settingsService)
    {
        return action switch
        {
            PlayerHotkeyAction.PlayPause => Strings.Resources.HotkeyPlayPause,
            PlayerHotkeyAction.Play => Strings.Resources.HotkeyPlay,
            PlayerHotkeyAction.Pause => Strings.Resources.HotkeyPause,
            PlayerHotkeyAction.Stop => Strings.Resources.HotkeyStop,
            PlayerHotkeyAction.Next => Strings.Resources.HotkeyNext,
            PlayerHotkeyAction.Previous => Strings.Resources.HotkeyPrevious,
            PlayerHotkeyAction.NextChapter => Strings.Resources.HotkeyNextChapter,
            PlayerHotkeyAction.PreviousChapter => Strings.Resources.HotkeyPreviousChapter,
            PlayerHotkeyAction.Faster => Strings.Resources.HotkeyFaster,
            PlayerHotkeyAction.Slower => Strings.Resources.HotkeySlower,
            PlayerHotkeyAction.NormalRate => Strings.Resources.HotkeyNormalRate,
            PlayerHotkeyAction.FasterFine => Strings.Resources.HotkeyFasterFine,
            PlayerHotkeyAction.SlowerFine => Strings.Resources.HotkeySlowerFine,
            PlayerHotkeyAction.FasterStep => Strings.Resources.HotkeyFasterStep,
            PlayerHotkeyAction.SlowerStep => Strings.Resources.HotkeySlowerStep,
            PlayerHotkeyAction.NextFrame => Strings.Resources.HotkeyNextFrame,
            PlayerHotkeyAction.PreviousFrame => Strings.Resources.HotkeyPreviousFrame,
            PlayerHotkeyAction.ShowPosition => Strings.Resources.HotkeyShowPosition,
            PlayerHotkeyAction.JumpToTime => Strings.Resources.HotkeyJumpToTime,
            PlayerHotkeyAction.CycleLoop => Strings.Resources.HotkeyCycleLoop,
            PlayerHotkeyAction.ToggleRandom => Strings.Resources.HotkeyToggleRandom,
            PlayerHotkeyAction.ClearPlayQueue => Strings.Resources.HotkeyClearPlayQueue,
            PlayerHotkeyAction.JumpBackwardExtraShort => Strings.Resources.HotkeyJumpBackwardSeconds(VlcPlaybackHelper.ExtraShortJumpSeconds),
            PlayerHotkeyAction.JumpForwardExtraShort => Strings.Resources.HotkeyJumpForwardSeconds(VlcPlaybackHelper.ExtraShortJumpSeconds),
            PlayerHotkeyAction.JumpBackwardShort => Strings.Resources.HotkeyJumpBackwardSeconds(settingsService.PlayerRewindStep),
            PlayerHotkeyAction.JumpForwardShort => Strings.Resources.HotkeyJumpForwardSeconds(settingsService.PlayerFastForwardStep),
            PlayerHotkeyAction.JumpBackwardMedium => Strings.Resources.HotkeyJumpBackwardMinutes(VlcPlaybackHelper.MediumJumpSeconds / 60),
            PlayerHotkeyAction.JumpForwardMedium => Strings.Resources.HotkeyJumpForwardMinutes(VlcPlaybackHelper.MediumJumpSeconds / 60),
            PlayerHotkeyAction.JumpBackwardLong => Strings.Resources.HotkeyJumpBackwardMinutes(VlcPlaybackHelper.LongJumpSeconds / 60),
            PlayerHotkeyAction.JumpForwardLong => Strings.Resources.HotkeyJumpForwardMinutes(VlcPlaybackHelper.LongJumpSeconds / 60),
            PlayerHotkeyAction.JumpToStart => Strings.Resources.HotkeyJumpToStart,
            PlayerHotkeyAction.JumpToEnd => Strings.Resources.HotkeyJumpToEnd,
            >= PlayerHotkeyAction.JumpTo0Percent and <= PlayerHotkeyAction.JumpTo90Percent
                => Strings.Resources.HotkeyJumpToPercent((action - PlayerHotkeyAction.JumpTo0Percent) * 10),
            PlayerHotkeyAction.VolumeUp => Strings.Resources.HotkeyVolumeUp,
            PlayerHotkeyAction.VolumeDown => Strings.Resources.HotkeyVolumeDown,
            PlayerHotkeyAction.ToggleMute => Strings.Resources.HotkeyToggleMute,
            PlayerHotkeyAction.CycleAudioTrack => Strings.Resources.HotkeyCycleAudioTrack,
            PlayerHotkeyAction.CycleAudioDevice => Strings.Resources.HotkeyCycleAudioDevice,
            PlayerHotkeyAction.AudioDelayUp => Strings.Resources.HotkeyAudioDelayUp,
            PlayerHotkeyAction.AudioDelayDown => Strings.Resources.HotkeyAudioDelayDown,
            PlayerHotkeyAction.CycleSubtitleTrack => Strings.Resources.HotkeyCycleSubtitleTrack,
            PlayerHotkeyAction.CycleSubtitleTrackReverse => Strings.Resources.HotkeyCycleSubtitleTrackReverse,
            PlayerHotkeyAction.ToggleSubtitles => Strings.Resources.HotkeyToggleSubtitles,
            PlayerHotkeyAction.SubtitleDelayUp => Strings.Resources.HotkeySubtitleDelayUp,
            PlayerHotkeyAction.SubtitleDelayDown => Strings.Resources.HotkeySubtitleDelayDown,
            PlayerHotkeyAction.SubtitleSyncBookmarkAudio => Strings.Resources.HotkeySubtitleSyncBookmarkAudio,
            PlayerHotkeyAction.SubtitleSyncBookmarkSubtitle => Strings.Resources.HotkeySubtitleSyncBookmarkSubtitle,
            PlayerHotkeyAction.SubtitleSyncApply => Strings.Resources.HotkeySubtitleSyncApply,
            PlayerHotkeyAction.SubtitleSyncReset => Strings.Resources.HotkeySubtitleSyncReset,
            PlayerHotkeyAction.ToggleFullscreen => Strings.Resources.HotkeyToggleFullscreen,
            PlayerHotkeyAction.ExitFullscreen => Strings.Resources.HotkeyExitFullscreen,
            PlayerHotkeyAction.CycleAspectRatio => Strings.Resources.HotkeyCycleAspectRatio,
            PlayerHotkeyAction.CycleCrop => Strings.Resources.HotkeyCycleCrop,
            PlayerHotkeyAction.ToggleAutoscale => Strings.Resources.HotkeyToggleAutoscale,
            PlayerHotkeyAction.ScaleUp => Strings.Resources.HotkeyScaleUp,
            PlayerHotkeyAction.ScaleDown => Strings.Resources.HotkeyScaleDown,
            PlayerHotkeyAction.NextZoomMode => Strings.Resources.HotkeyNextZoomMode,
            PlayerHotkeyAction.PreviousZoomMode => Strings.Resources.HotkeyPreviousZoomMode,
            PlayerHotkeyAction.ZoomQuarter => Strings.Resources.HotkeyZoomTo("25%"),
            PlayerHotkeyAction.ZoomHalf => Strings.Resources.HotkeyZoomTo("50%"),
            PlayerHotkeyAction.ZoomOriginal => Strings.Resources.HotkeyZoomTo("100%"),
            PlayerHotkeyAction.ZoomDouble => Strings.Resources.HotkeyZoomTo("200%"),
            PlayerHotkeyAction.ToggleDeinterlace => Strings.Resources.HotkeyToggleDeinterlace,
            PlayerHotkeyAction.CycleDeinterlaceMode => Strings.Resources.HotkeyCycleDeinterlaceMode,
            PlayerHotkeyAction.SaveFrame => Strings.Resources.HotkeySaveFrame,
            PlayerHotkeyAction.ShowControls => Strings.Resources.HotkeyShowControls,
            PlayerHotkeyAction.ShowContextMenu => Strings.Resources.HotkeyShowContextMenu,
            PlayerHotkeyAction.ShowPlayQueue => Strings.Resources.HotkeyShowPlayQueue,
            PlayerHotkeyAction.OpenFiles => Strings.Resources.HotkeyOpenFiles,
            PlayerHotkeyAction.ShowMediaInfo => Strings.Resources.HotkeyShowMediaInfo,
            PlayerHotkeyAction.ToggleCompactOverlay => Strings.Resources.HotkeyToggleCompactOverlay,
            PlayerHotkeyAction.Cast => Strings.Resources.HotkeyCast,
            PlayerHotkeyAction.ResizeWindowHalf => Strings.Resources.HotkeyResizeWindowTo("50%"),
            PlayerHotkeyAction.ResizeWindowOriginal => Strings.Resources.HotkeyResizeWindowTo("100%"),
            PlayerHotkeyAction.ResizeWindowOneAndHalf => Strings.Resources.HotkeyResizeWindowTo("150%"),
            PlayerHotkeyAction.ResizeWindowToScreen => Strings.Resources.HotkeyResizeWindowToScreen,
            PlayerHotkeyAction.GrowWindow => Strings.Resources.HotkeyGrowWindow,
            PlayerHotkeyAction.ShrinkWindow => Strings.Resources.HotkeyShrinkWindow,
            PlayerHotkeyAction.ShowKeyboardShortcuts => Strings.Resources.HotkeyShowKeyboardShortcuts,
            _ => action.ToString(),
        };
    }

    /// <summary>
    /// Gets the key combinations of an action as text.
    /// </summary>
    /// <param name="map">The keyboard shortcuts.</param>
    /// <param name="action">The player action.</param>
    /// <returns>The key combinations separated by commas, or a localized "None" if the action has no keys.</returns>
    public static string GetKeysText(HotkeyMap map, PlayerHotkeyAction action)
    {
        var bindings = map.GetBindings(action);
        if (bindings.Count == 0)
        {
            return Strings.Resources.None;
        }

        string[] names = new string[bindings.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = bindings[i].ToDisplayName();
        }

        return string.Join(", ", names);
    }
}
