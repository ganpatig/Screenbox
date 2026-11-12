using System;

namespace Screenbox.Core.Models;

/// <summary>
/// Represents a subtitle found by an online subtitle search.
/// </summary>
/// <param name="Id">The identifier of the subtitle file at the provider.</param>
/// <param name="FileName">The file name of the subtitle as it was uploaded.</param>
/// <param name="LanguageName">The English name of the subtitle language.</param>
/// <param name="LanguageCode">The provider's code of the subtitle language, such as <c>eng</c>.</param>
/// <param name="Format">The lowercase subtitle file format, such as <c>srt</c>.</param>
/// <param name="Encoding">The character encoding reported by the provider, or an empty string if unknown.</param>
/// <param name="DownloadCount">The number of times the subtitle has been downloaded.</param>
/// <param name="ReleaseName">The name of the video release that the subtitle was made for.</param>
/// <param name="IsHashMatch">
/// Whether the subtitle was matched by the hash of the playing file, which means it is timed for that exact file.
/// </param>
/// <param name="IsHearingImpaired">Whether the subtitle includes descriptions for the hearing impaired.</param>
/// <param name="DownloadUri">The address to download the subtitle from.</param>
public sealed record OnlineSubtitle(
    string Id,
    string FileName,
    string LanguageName,
    string LanguageCode,
    string Format,
    string Encoding,
    int DownloadCount,
    string ReleaseName,
    bool IsHashMatch,
    bool IsHearingImpaired,
    Uri DownloadUri);
