namespace Screenbox.Core.Enums;

/// <summary>
/// Describes the state of an online subtitle search.
/// </summary>
public enum OnlineSubtitleSearchState
{
    /// <summary>No search has run yet.</summary>
    Idle,

    /// <summary>A search is in progress.</summary>
    Searching,

    /// <summary>The last search found subtitles.</summary>
    HasResults,

    /// <summary>The last search found no subtitles.</summary>
    NoResults,

    /// <summary>The last search failed, typically because the service could not be reached.</summary>
    SearchFailed,

    /// <summary>A subtitle is being downloaded.</summary>
    Downloading,

    /// <summary>The last download failed.</summary>
    DownloadFailed,

    /// <summary>No media is playing, so there is nothing to search subtitles for.</summary>
    NoMedia
}
