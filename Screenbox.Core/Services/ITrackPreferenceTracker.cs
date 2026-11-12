using System.Threading.Tasks;
using Screenbox.Core.Models;

namespace Screenbox.Core.Services;

/// <summary>
/// Remembers which audio and subtitle track the user played each media item with, so that the same tracks
/// can be selected again the next time the item is played.
/// </summary>
public interface ITrackPreferenceTracker
{
    /// <summary>
    /// Loads the remembered choices from disk, unless they are already loaded.
    /// </summary>
    Task EnsureLoadedAsync();

    /// <summary>
    /// Gets the tracks remembered for a media location.
    /// </summary>
    /// <param name="location">The location of the media item.</param>
    /// <returns>The remembered tracks, or <see langword="null"/> if the item was never played with a chosen track.</returns>
    MediaTrackPreference? Get(string location);

    /// <summary>
    /// Remembers the audio track of a media location.
    /// </summary>
    void UpdateAudio(string location, MediaTrackChoice choice);

    /// <summary>
    /// Remembers the subtitle track of a media location, which can be <see cref="MediaTrackChoice.Disabled"/>.
    /// </summary>
    void UpdateSubtitle(string location, MediaTrackChoice choice);

    /// <summary>
    /// Forgets every remembered choice.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Writes the remembered choices to disk.
    /// </summary>
    Task SaveToDiskAsync();
}
