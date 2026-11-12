namespace Screenbox.Core.Enums;

/// <summary>
/// Defines constants that specify the player actions that keyboard shortcuts can trigger.
/// </summary>
/// <remarks>
/// The default keys follow the hotkeys of VLC media player 3.0 for Windows, together with the shortcuts
/// that Screenbox already had. The names are stored in the settings, so existing names must not change.
/// </remarks>
public enum PlayerHotkeyAction
{
    None,

    // Playback
    PlayPause,
    Play,
    Pause,
    Stop,
    Next,
    Previous,
    NextChapter,
    PreviousChapter,
    Faster,
    Slower,
    NormalRate,
    FasterFine,
    SlowerFine,
    FasterStep,
    SlowerStep,
    NextFrame,
    PreviousFrame,
    ShowPosition,
    JumpToTime,
    CycleLoop,
    ToggleRandom,
    ClearPlayQueue,

    // Jumping
    JumpBackwardExtraShort,
    JumpForwardExtraShort,
    JumpBackwardShort,
    JumpForwardShort,
    JumpBackwardMedium,
    JumpForwardMedium,
    JumpBackwardLong,
    JumpForwardLong,
    JumpToStart,
    JumpToEnd,
    JumpTo0Percent,
    JumpTo10Percent,
    JumpTo20Percent,
    JumpTo30Percent,
    JumpTo40Percent,
    JumpTo50Percent,
    JumpTo60Percent,
    JumpTo70Percent,
    JumpTo80Percent,
    JumpTo90Percent,

    // Audio
    VolumeUp,
    VolumeDown,
    ToggleMute,
    CycleAudioTrack,
    CycleAudioDevice,
    AudioDelayUp,
    AudioDelayDown,

    // Subtitles
    CycleSubtitleTrack,
    CycleSubtitleTrackReverse,
    ToggleSubtitles,
    SubtitleDelayUp,
    SubtitleDelayDown,
    SubtitleSyncBookmarkAudio,
    SubtitleSyncBookmarkSubtitle,
    SubtitleSyncApply,
    SubtitleSyncReset,

    // Video
    ToggleFullscreen,
    ExitFullscreen,
    CycleAspectRatio,
    CycleCrop,
    ToggleAutoscale,
    ScaleUp,
    ScaleDown,
    NextZoomMode,
    PreviousZoomMode,
    ZoomQuarter,
    ZoomHalf,
    ZoomOriginal,
    ZoomDouble,
    ToggleDeinterlace,
    CycleDeinterlaceMode,
    SaveFrame,

    // Window and menus
    ShowControls,
    ShowContextMenu,
    ShowPlayQueue,
    OpenFiles,
    ShowMediaInfo,
    ToggleCompactOverlay,
    Cast,
    ResizeWindowHalf,
    ResizeWindowOriginal,
    ResizeWindowOneAndHalf,
    ResizeWindowToScreen,
    GrowWindow,
    ShrinkWindow,
    ShowKeyboardShortcuts,
}
