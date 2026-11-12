using System.Text.Json.Serialization;

namespace Screenbox.Core.Models.Serialization;

/// <summary>
/// Represents one result of the legacy opensubtitles.org REST search API. The API returns every value as a string.
/// </summary>
internal sealed class OpenSubtitlesSearchResult
{
    [JsonPropertyName("IDSubtitleFile")]
    public string? IdSubtitleFile { get; set; }

    [JsonPropertyName("SubFileName")]
    public string? SubFileName { get; set; }

    [JsonPropertyName("LanguageName")]
    public string? LanguageName { get; set; }

    [JsonPropertyName("SubLanguageID")]
    public string? SubLanguageId { get; set; }

    [JsonPropertyName("SubFormat")]
    public string? SubFormat { get; set; }

    [JsonPropertyName("SubEncoding")]
    public string? SubEncoding { get; set; }

    [JsonPropertyName("SubDownloadsCnt")]
    public string? SubDownloadsCnt { get; set; }

    [JsonPropertyName("MovieReleaseName")]
    public string? MovieReleaseName { get; set; }

    [JsonPropertyName("MatchedBy")]
    public string? MatchedBy { get; set; }

    [JsonPropertyName("SubHearingImpaired")]
    public string? SubHearingImpaired { get; set; }

    [JsonPropertyName("SubDownloadLink")]
    public string? SubDownloadLink { get; set; }
}
