namespace Screenbox.Core.Models;

/// <summary>
/// Represents a language that online subtitles can be searched in.
/// </summary>
/// <param name="Code">The provider's language code, or an empty string to search every language.</param>
/// <param name="LanguageTag">
/// The BCP-47 tag used to show a localized language name, or an empty string for every language.
/// </param>
/// <param name="EnglishName">The English name of the language, shown when no localized name is available.</param>
public sealed record OnlineSubtitleLanguage(string Code, string LanguageTag, string EnglishName)
{
    /// <summary>
    /// Gets a value that indicates whether this entry searches subtitles in every language.
    /// </summary>
    public bool IsAllLanguages => Code.Length == 0;
}
