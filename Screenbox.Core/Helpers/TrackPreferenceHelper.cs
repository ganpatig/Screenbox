using System;
using System.Collections.Generic;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Matches remembered track choices and language preferences against the tracks of a media item.
/// </summary>
public static class TrackPreferenceHelper
{
    /// <summary>
    /// Describes the track that is selected in a track list.
    /// </summary>
    /// <param name="tracks">The tracks of the media item.</param>
    /// <param name="selectedIndex">The index of the selected track, or a negative value if none is selected.</param>
    /// <returns>The choice to remember, which is <see cref="MediaTrackChoice.Disabled"/> when no track is selected.</returns>
    public static MediaTrackChoice CreateChoice<T>(IReadOnlyList<T> tracks, int selectedIndex) where T : MediaTrack
    {
        if (selectedIndex < 0 || selectedIndex >= tracks.Count)
        {
            return MediaTrackChoice.Disabled;
        }

        T track = tracks[selectedIndex];
        return new MediaTrackChoice(selectedIndex, GetLanguage(track), track.Label ?? string.Empty);
    }

    /// <summary>
    /// Finds the track that a remembered choice refers to.
    /// </summary>
    /// <remarks>
    /// The language and the label identify a track even when tracks were added or removed, for example when an
    /// external subtitle file was loaded. The remembered position is only used when neither of them matches,
    /// which is the case for tracks that carry no language and no description.
    /// </remarks>
    /// <param name="tracks">The tracks of the media item.</param>
    /// <param name="choice">The remembered choice.</param>
    /// <returns>The index of the matching track, or -1 if the choice is to play no track or nothing matches.</returns>
    public static int FindTrack<T>(IReadOnlyList<T> tracks, MediaTrackChoice? choice) where T : MediaTrack
    {
        if (choice is null || choice.IsDisabled || tracks.Count == 0)
        {
            return -1;
        }

        bool hasLanguage = !string.IsNullOrEmpty(choice.LanguageTag);
        bool hasLabel = !string.IsNullOrEmpty(choice.Label);

        if (hasLanguage && hasLabel)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (IsSameLanguage(tracks[i], choice.LanguageTag) && IsSameLabel(tracks[i].Label, choice.Label))
                {
                    return i;
                }
            }
        }

        if (hasLanguage)
        {
            int index = FindLanguage(tracks, choice.LanguageTag);
            if (index >= 0)
            {
                return index;
            }
        }

        if (hasLabel)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (IsSameLabel(tracks[i].Label, choice.Label))
                {
                    return i;
                }
            }
        }

        // A track without a language or a description is only identified by where it is in the list.
        return !hasLanguage && !hasLabel && choice.Index < tracks.Count ? choice.Index : -1;
    }

    /// <summary>
    /// Finds the first track in one of the preferred languages.
    /// </summary>
    /// <param name="tracks">The tracks of the media item.</param>
    /// <param name="languages">The preferred languages, as a comma separated list of language tags such as "en,fr".</param>
    /// <returns>The index of the first matching track, or -1 if no track is in any of the languages.</returns>
    public static int FindPreferredLanguage<T>(IReadOnlyList<T> tracks, string? languages) where T : MediaTrack
    {
        if (string.IsNullOrWhiteSpace(languages))
        {
            return -1;
        }

        foreach (string language in languages!.Split(','))
        {
            string tag = language.Trim();
            if (tag.Length == 0)
            {
                continue;
            }

            int index = FindLanguage(tracks, tag);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindLanguage<T>(IReadOnlyList<T> tracks, string language) where T : MediaTrack
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (IsSameLanguage(tracks[i], language))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the language of a track, preferring its tag, such as "en", over its name, such as "English".
    /// </summary>
    /// <remarks>Tracks of media that LibVLC could not parse a language tag from only carry the name.</remarks>
    private static string GetLanguage(MediaTrack track)
    {
        return !string.IsNullOrEmpty(track.LanguageTag) ? track.LanguageTag : track.Language ?? string.Empty;
    }

    /// <summary>
    /// Determines whether a track is in a language, which can be given as a tag or as a name.
    /// </summary>
    private static bool IsSameLanguage(MediaTrack track, string language)
    {
        return IsSameTag(track.LanguageTag, language) || IsSameTag(track.Language, language) ||
               (!string.IsNullOrEmpty(track.Language) &&
                string.Equals(track.Language, language, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Compares two language tags by their primary language, so that "en" also matches "en-US".
    /// </summary>
    private static bool IsSameTag(string? tag, string? otherTag)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(otherTag))
        {
            return false;
        }

        return GetPrimaryLanguage(tag!).Equals(GetPrimaryLanguage(otherTag!), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPrimaryLanguage(string tag)
    {
        int separator = tag.IndexOf('-');
        return separator < 0 ? tag : tag.Substring(0, separator);
    }

    private static bool IsSameLabel(string? label, string? otherLabel)
    {
        return !string.IsNullOrEmpty(label) && string.Equals(label, otherLabel, StringComparison.OrdinalIgnoreCase);
    }
}
