namespace Screenbox.Core.Enums;

/// <summary>
/// Defines constants that specify the outcome of a subtitle synchronization key press.
/// </summary>
public enum SubtitleSyncStep
{
    /// <summary>The time at which a line was heard was bookmarked.</summary>
    AudioBookmarked,

    /// <summary>The time at which the matching subtitle appeared was bookmarked.</summary>
    SubtitleBookmarked,

    /// <summary>The correction was not applied because a bookmark is missing.</summary>
    MissingBookmarks,

    /// <summary>The subtitle delay was corrected with the time between the bookmarks.</summary>
    Applied,

    /// <summary>The subtitle delay was reset to zero.</summary>
    Reset,

    /// <summary>Nothing changed because no subtitle track is selected.</summary>
    NoActiveSubtitle,
}
