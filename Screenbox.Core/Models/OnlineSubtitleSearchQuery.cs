namespace Screenbox.Core.Models;

/// <summary>
/// Describes an online subtitle search.
/// </summary>
/// <param name="Text">The title to search for, or an empty string to skip the title search.</param>
/// <param name="MovieHash">
/// The OpenSubtitles hash of the playing file, or <see langword="null"/> to skip the hash search.
/// </param>
/// <param name="MovieByteSize">The size of the playing file in bytes, which the hash search also requires.</param>
/// <param name="LanguageCode">The provider's language code, or an empty string to search every language.</param>
public sealed record OnlineSubtitleSearchQuery(string Text, string? MovieHash, long MovieByteSize, string LanguageCode);
