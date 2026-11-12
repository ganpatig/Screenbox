using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Screenbox.Core.Models;
using Screenbox.Core.Models.Serialization;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Implements the protocol details of the legacy opensubtitles.org REST API, separately from networking so that
/// they can be tested without a connection.
/// </summary>
public static partial class OpenSubtitlesHelper
{
    /// <summary>
    /// The language code used when a language tag has no matching subtitle language.
    /// </summary>
    public const string DefaultLanguageCode = "eng";

    private const string SearchBaseUri = "https://rest.opensubtitles.org/search/";
    private const int HashChunkSize = 65536;
    private const int MaxFileNameLength = 100;
    private const int MaxSubtitleBytes = 20 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly char[] TitleSeparators = [' ', '.', '_'];

    /// <summary>
    /// Text subtitle formats that can be decoded and loaded by LibVLC. Image-based formats, such as VobSub, are skipped.
    /// </summary>
    private static readonly string[] TextSubtitleFormats = ["srt", "ass", "ssa", "vtt", "sub", "smi", "txt"];

    static OpenSubtitlesHelper()
    {
        // Older subtitles are often stored in Windows code pages, which .NET only decodes with this provider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Gets the languages that subtitles can be searched in. The first entry searches every language.
    /// </summary>
    /// <remarks>
    /// The codes were verified against the service. Most are ISO 639-2/B codes, with service-specific codes for
    /// regional variants, such as <c>pob</c> for Brazilian Portuguese and <c>zht</c> for Traditional Chinese.
    /// Generic entries come before their regional variants so that language tag lookups prefer them.
    /// </remarks>
    public static IReadOnlyList<OnlineSubtitleLanguage> Languages { get; } = new OnlineSubtitleLanguage[]
    {
        new(string.Empty, string.Empty, "All languages"),
        new("alb", "sq", "Albanian"),
        new("ara", "ar", "Arabic"),
        new("ben", "bn", "Bengali"),
        new("bos", "bs", "Bosnian"),
        new("bul", "bg", "Bulgarian"),
        new("bur", "my", "Burmese"),
        new("cat", "ca", "Catalan"),
        new("chi", "zh-Hans", "Chinese (Simplified)"),
        new("zht", "zh-Hant", "Chinese (Traditional)"),
        new("hrv", "hr", "Croatian"),
        new("cze", "cs", "Czech"),
        new("dan", "da", "Danish"),
        new("dut", "nl", "Dutch"),
        new("eng", "en", "English"),
        new("est", "et", "Estonian"),
        new("fin", "fi", "Finnish"),
        new("fre", "fr", "French"),
        new("ger", "de", "German"),
        new("ell", "el", "Greek"),
        new("heb", "he", "Hebrew"),
        new("hin", "hi", "Hindi"),
        new("hun", "hu", "Hungarian"),
        new("ice", "is", "Icelandic"),
        new("ind", "id", "Indonesian"),
        new("ita", "it", "Italian"),
        new("jpn", "ja", "Japanese"),
        new("kor", "ko", "Korean"),
        new("kur", "ku", "Kurdish"),
        new("lav", "lv", "Latvian"),
        new("lit", "lt", "Lithuanian"),
        new("mac", "mk", "Macedonian"),
        new("may", "ms", "Malay"),
        new("mal", "ml", "Malayalam"),
        new("nor", "no", "Norwegian"),
        new("per", "fa", "Persian"),
        new("pol", "pl", "Polish"),
        new("por", "pt-PT", "Portuguese"),
        new("pob", "pt-BR", "Portuguese (Brazil)"),
        new("rum", "ro", "Romanian"),
        new("rus", "ru", "Russian"),
        new("scc", "sr", "Serbian"),
        new("sin", "si", "Sinhala"),
        new("slo", "sk", "Slovak"),
        new("slv", "sl", "Slovenian"),
        new("spa", "es", "Spanish"),
        new("spn", "es-ES", "Spanish (Spain)"),
        new("spl", "es-419", "Spanish (Latin America)"),
        new("swe", "sv", "Swedish"),
        new("tgl", "fil", "Tagalog"),
        new("tam", "ta", "Tamil"),
        new("tel", "te", "Telugu"),
        new("tha", "th", "Thai"),
        new("tur", "tr", "Turkish"),
        new("ukr", "uk", "Ukrainian"),
        new("urd", "ur", "Urdu"),
        new("vie", "vi", "Vietnamese")
    };

    /// <summary>
    /// Finds the subtitle language code for a BCP-47 language tag.
    /// </summary>
    /// <param name="languageTag">The language tag, such as <c>en-US</c> or <c>pt-BR</c>.</param>
    /// <returns>The matching language code, or <see cref="DefaultLanguageCode"/> if the language is not supported.</returns>
    /// <example>
    /// <code>
    /// string code = OpenSubtitlesHelper.GetLanguageCode("pt-BR"); // "pob"
    /// </code>
    /// </example>
    public static string GetLanguageCode(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return DefaultLanguageCode;
        }

        string tag = languageTag.Trim().ToLowerInvariant();
        string primarySubtag = tag.Split('-')[0];
        return primarySubtag switch
        {
            "pt" when tag.EndsWith("-br", StringComparison.Ordinal) => "pob",
            "zh" when tag.Contains("-hant", StringComparison.Ordinal) || tag.EndsWith("-tw", StringComparison.Ordinal)
                      || tag.EndsWith("-hk", StringComparison.Ordinal) || tag.EndsWith("-mo", StringComparison.Ordinal) => "zht",
            "nb" or "nn" or "no" => "nor",
            "fil" or "tl" => "tgl",
            _ => Languages.FirstOrDefault(language => !language.IsAllLanguages
                     && language.LanguageTag.Split('-')[0].Equals(primarySubtag, StringComparison.OrdinalIgnoreCase))?.Code
                 ?? DefaultLanguageCode
        };
    }

    /// <summary>
    /// Computes the OpenSubtitles hash of a video file, which identifies the exact release that subtitles are timed for.
    /// </summary>
    /// <remarks>
    /// The hash is the file size plus the sum of the 64-bit little-endian integers in the first and the last 64 KiB of
    /// the file, with overflow ignored.
    /// </remarks>
    /// <param name="stream">A readable and seekable stream of the whole file.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>
    /// The hash as 16 lowercase hexadecimal digits, or <see langword="null"/> if the file is smaller than two chunks
    /// or the stream cannot seek.
    /// </returns>
    public static async Task<string?> ComputeMovieHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead || !stream.CanSeek || stream.Length < HashChunkSize * 2)
        {
            return null;
        }

        ulong hash = (ulong)stream.Length;
        byte[] buffer = new byte[HashChunkSize];

        stream.Seek(0, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        hash = AddChunk(hash, buffer);

        stream.Seek(-HashChunkSize, SeekOrigin.End);
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        hash = AddChunk(hash, buffer);

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the address that searches subtitles matching a file hash.
    /// </summary>
    /// <param name="movieHash">The OpenSubtitles hash of the file.</param>
    /// <param name="movieByteSize">The size of the file in bytes.</param>
    /// <param name="languageCode">The subtitle language code, or an empty string for every language.</param>
    /// <returns>The search address.</returns>
    public static Uri BuildHashSearchUri(string movieHash, long movieByteSize, string languageCode)
    {
        return BuildSearchUri(
        [
            new("moviebytesize", movieByteSize.ToString(CultureInfo.InvariantCulture)),
            new("moviehash", movieHash.ToLowerInvariant())
        ], languageCode);
    }

    /// <summary>
    /// Builds the address that searches subtitles by title.
    /// </summary>
    /// <param name="text">The title to search for.</param>
    /// <param name="languageCode">The subtitle language code, or an empty string for every language.</param>
    /// <returns>The search address.</returns>
    public static Uri BuildTextSearchUri(string text, string languageCode)
    {
        return BuildSearchUri([new("query", text.Trim().ToLowerInvariant())], languageCode);
    }

    /// <summary>
    /// Parses a search response into subtitles that can be loaded as text.
    /// </summary>
    /// <param name="json">The JSON array returned by the search API.</param>
    /// <returns>The subtitles, skipping results in image-based formats or without a download address.</returns>
    /// <exception cref="JsonException">The response is not a JSON array of search results.</exception>
    public static IReadOnlyList<OnlineSubtitle> ParseSearchResults(string json)
    {
        OpenSubtitlesSearchResult[]? results =
            JsonSerializer.Deserialize(json, OpenSubtitlesJsonContext.Default.OpenSubtitlesSearchResultArray);
        if (results is null)
        {
            return [];
        }

        List<OnlineSubtitle> subtitles = new(results.Length);
        foreach (OpenSubtitlesSearchResult result in results)
        {
            if (TryCreateSubtitle(result) is { } subtitle)
            {
                subtitles.Add(subtitle);
            }
        }

        return subtitles;
    }

    /// <summary>
    /// Combines hash and title search results. Hash matches come first because they are timed for the playing file,
    /// and every group is ordered by popularity.
    /// </summary>
    /// <param name="hashResults">The results of the hash search.</param>
    /// <param name="textResults">The results of the title search.</param>
    /// <returns>The combined results without duplicates.</returns>
    public static IReadOnlyList<OnlineSubtitle> MergeResults(IReadOnlyList<OnlineSubtitle> hashResults,
        IReadOnlyList<OnlineSubtitle> textResults)
    {
        IEnumerable<OnlineSubtitle> ordered = hashResults
            .OrderByDescending(subtitle => subtitle.IsHashMatch)
            .ThenByDescending(subtitle => subtitle.DownloadCount)
            .Concat(textResults.OrderByDescending(subtitle => subtitle.DownloadCount));

        HashSet<string> seenIds = new(StringComparer.Ordinal);
        List<OnlineSubtitle> merged = new(hashResults.Count + textResults.Count);
        foreach (OnlineSubtitle subtitle in ordered)
        {
            if (seenIds.Add(subtitle.Id))
            {
                merged.Add(subtitle);
            }
        }

        return merged;
    }

    /// <summary>
    /// Derives a search title from a video file name by removing the extension, separators, and release tags.
    /// </summary>
    /// <param name="fileName">The file name of the video.</param>
    /// <returns>The title to search for, or an empty string if there is no file name.</returns>
    /// <example>
    /// <code>
    /// string title = OpenSubtitlesHelper.GetSearchText("The.Matrix.1999.1080p.BluRay.x264.mkv"); // "The Matrix 1999"
    /// </code>
    /// </example>
    public static string GetSearchText(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        string name = fileName.Trim();
        string extension = Path.GetExtension(name);

        // Only remove a real extension. An all-digit suffix such as ".1999" is part of the title.
        if (extension.Length is > 1 and <= 5 && extension.AsSpan(1).ContainsAnyExceptInRange('0', '9'))
        {
            name = name[..^extension.Length];
        }

        List<string> words = [];
        foreach (string word in name.Split(TitleSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            // Everything from the first release tag onwards describes the encode rather than the title.
            if (ReleaseTagRegex().IsMatch(word))
            {
                break;
            }

            words.Add(word);
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// Decompresses a downloaded subtitle if it is gzip-compressed, which is how the service delivers subtitle files.
    /// </summary>
    /// <param name="data">The downloaded bytes.</param>
    /// <returns>The decompressed bytes, or <paramref name="data"/> if it is not gzip-compressed.</returns>
    /// <exception cref="InvalidDataException">The data is corrupt, or decompresses to more than a subtitle could need.</exception>
    public static byte[] DecompressIfGzip(byte[] data)
    {
        if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
        {
            return data;
        }

        using MemoryStream input = new(data);
        using GZipStream gzip = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            // No subtitle is this large. Stop rather than keep expanding a malformed or malicious archive.
            if (output.Length + read > MaxSubtitleBytes)
            {
                throw new InvalidDataException("The downloaded subtitle is too large.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Decodes a downloaded subtitle into text.
    /// </summary>
    /// <remarks>
    /// Byte order marks and valid UTF-8 take precedence over the reported encoding, which is sometimes wrong. Otherwise
    /// the reported encoding is used, falling back to Windows-1252, the most common encoding of older subtitles.
    /// </remarks>
    /// <param name="data">The subtitle bytes.</param>
    /// <param name="encodingName">The encoding reported by the service, such as <c>CP1250</c>, or <see langword="null"/>.</param>
    /// <returns>The subtitle text.</returns>
    public static string DecodeSubtitleText(byte[] data, string? encodingName)
    {
        ReadOnlySpan<byte> bytes = data;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8, so use the reported legacy encoding below.
        }

        return (GetEncoding(encodingName) ?? Encoding.GetEncoding(1252)).GetString(bytes);
    }

    /// <summary>
    /// Creates a safe local file name for a downloaded subtitle. The language code is kept in the name so that
    /// subtitle tracks in different languages can be told apart.
    /// </summary>
    /// <param name="subtitle">The downloaded subtitle.</param>
    /// <returns>A file name that is valid on Windows.</returns>
    public static string GetSubtitleFileName(OnlineSubtitle subtitle)
    {
        string baseName = Path.GetFileNameWithoutExtension(subtitle.FileName);
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(baseName.Length);
        foreach (char c in baseName)
        {
            builder.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
        }

        string safeName = builder.ToString().Trim(' ', '.');
        if (safeName.Length > MaxFileNameLength)
        {
            safeName = safeName[..MaxFileNameLength].TrimEnd(' ', '.');
        }

        if (safeName.Length == 0)
        {
            safeName = "subtitle";
        }

        bool needsLanguageSuffix = subtitle.LanguageCode.Length > 0
                                   && !safeName.EndsWith("." + subtitle.LanguageCode, StringComparison.OrdinalIgnoreCase);
        return needsLanguageSuffix
            ? $"{safeName}.{subtitle.LanguageCode}.{subtitle.Format}"
            : $"{safeName}.{subtitle.Format}";
    }

    /// <summary>
    /// Builds a search address from path parameters.
    /// </summary>
    /// <remarks>
    /// Parameters are path segments in alphabetical order. The language is omitted to search every language, because
    /// <c>sublanguageid-all</c> makes the service redirect to an invalid address.
    /// </remarks>
    private static Uri BuildSearchUri(List<KeyValuePair<string, string>> parameters, string languageCode)
    {
        if (languageCode.Length > 0)
        {
            parameters.Add(new KeyValuePair<string, string>("sublanguageid", languageCode));
        }

        IEnumerable<string> segments = parameters
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter => parameter.Key + "-" + Uri.EscapeDataString(parameter.Value));
        return new Uri(SearchBaseUri + string.Join('/', segments));
    }

    /// <summary>
    /// Maps a search result to a subtitle, or returns <see langword="null"/> if it cannot be loaded as text.
    /// </summary>
    private static OnlineSubtitle? TryCreateSubtitle(OpenSubtitlesSearchResult result)
    {
        string format = result.SubFormat?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(result.IdSubtitleFile)
            || !TextSubtitleFormats.Contains(format)
            || !Uri.TryCreate(result.SubDownloadLink, UriKind.Absolute, out Uri? downloadUri)
            || (downloadUri.Scheme != Uri.UriSchemeHttps && downloadUri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        _ = int.TryParse(result.SubDownloadsCnt, NumberStyles.Integer, CultureInfo.InvariantCulture, out int downloadCount);
        return new OnlineSubtitle(
            result.IdSubtitleFile,
            result.SubFileName ?? string.Empty,
            result.LanguageName ?? string.Empty,
            result.SubLanguageId ?? string.Empty,
            format,
            result.SubEncoding ?? string.Empty,
            downloadCount,
            result.MovieReleaseName?.Trim() ?? string.Empty,
            IsHashMatch: string.Equals(result.MatchedBy, "moviehash", StringComparison.OrdinalIgnoreCase),
            IsHearingImpaired: result.SubHearingImpaired == "1",
            downloadUri);
    }

    /// <summary>
    /// Adds the 64-bit little-endian integers of a chunk to a running hash, ignoring overflow.
    /// </summary>
    private static ulong AddChunk(ulong hash, ReadOnlySpan<byte> chunk)
    {
        for (int offset = 0; offset + sizeof(ulong) <= chunk.Length; offset += sizeof(ulong))
        {
            hash = unchecked(hash + BinaryPrimitives.ReadUInt64LittleEndian(chunk[offset..]));
        }

        return hash;
    }

    /// <summary>
    /// Resolves an encoding name reported by the service.
    /// </summary>
    private static Encoding? GetEncoding(string? encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName))
        {
            return null;
        }

        string name = encodingName.Trim();
        try
        {
            // The service reports Windows code pages as names like "CP1250", so use the number instead of the name.
            return name.StartsWith("CP", StringComparison.OrdinalIgnoreCase)
                   && int.TryParse(name.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int codePage)
                ? Encoding.GetEncoding(codePage)
                : Encoding.GetEncoding(name);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^(\d{3,4}[pi]|4k|uhd|hdr(10)?\+?|bluray|blu-ray|bdrip|brrip|bdremux|remux|web-?dl|web-?rip|hdtv|hdrip|dvdrip|x26[45]|h26[45]|hevc|xvid|divx|10bit|aac|ac3|dts|ddp?5|atmos|proper|repack|extended|unrated|imax)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagRegex();
}
