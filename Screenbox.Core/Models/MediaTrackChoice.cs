namespace Screenbox.Core.Models;

/// <summary>
/// Identifies a track of a media item well enough to find it again the next time the item is played.
/// </summary>
/// <remarks>
/// Tracks have no identifier that survives a session, so a choice is matched by language and label, and falls
/// back to the position in the track list, which is stable for the same file.
/// </remarks>
/// <param name="Index">The position of the track in its list, or a negative value for no track at all.</param>
/// <param name="LanguageTag">The language tag of the track, such as "en", or an empty string if it has none.</param>
/// <param name="Label">The label of the track, which is the file name of an external subtitle.</param>
public sealed record MediaTrackChoice(int Index, string LanguageTag, string Label)
{
    /// <summary>
    /// Gets the choice that means the track is turned off.
    /// </summary>
    public static MediaTrackChoice Disabled { get; } = new(-1, string.Empty, string.Empty);

    /// <summary>
    /// Gets a value that indicates whether the choice is to play no track at all.
    /// </summary>
    public bool IsDisabled => Index < 0;
}
