using System;

namespace Screenbox.Core.Models;

/// <summary>
/// The audio and subtitle tracks that a media item was last played with.
/// </summary>
/// <param name="Location">The location of the media item.</param>
/// <param name="Audio">The chosen audio track, or <see langword="null"/> if the user never chose one.</param>
/// <param name="Subtitle">The chosen subtitle track, <see cref="MediaTrackChoice.Disabled"/> if the user turned
/// subtitles off, or <see langword="null"/> if the user never chose.</param>
/// <param name="Updated">When the choice was last made, which decides what is dropped when the list is full.</param>
public sealed record MediaTrackPreference(
    string Location,
    MediaTrackChoice? Audio,
    MediaTrackChoice? Subtitle,
    DateTimeOffset Updated);
