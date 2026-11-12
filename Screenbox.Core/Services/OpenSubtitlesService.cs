using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Windows.Storage;

namespace Screenbox.Core.Services;

/// <summary>
/// Searches and downloads subtitles with the legacy opensubtitles.org REST API, which needs no account or API key.
/// </summary>
/// <remarks>
/// This API is unsupported and relies on the shared development user agent of OpenSubtitles, so it may stop working
/// without notice. All provider-specific behavior lives in this class and <see cref="OpenSubtitlesHelper"/>, so that
/// another provider can replace it without changes elsewhere.
/// </remarks>
public sealed class OpenSubtitlesService : IOnlineSubtitleService
{
    /// <summary>
    /// The shared development user agent of OpenSubtitles. The legacy API rejects user agents that are not registered.
    /// </summary>
    private const string ApiUserAgent = "TemporaryUserAgent";

    private const string SubtitleFolderName = "Subtitles";

    private static readonly HttpClient Client = CreateClient();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OnlineSubtitle>> SearchAsync(OnlineSubtitleSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OnlineSubtitle> hashResults = [];
        if (query.MovieHash is { Length: > 0 } movieHash && query.MovieByteSize > 0)
        {
            Uri hashSearchUri = OpenSubtitlesHelper.BuildHashSearchUri(movieHash, query.MovieByteSize, query.LanguageCode);
            hashResults = await SearchAsync(hashSearchUri, cancellationToken);
        }

        IReadOnlyList<OnlineSubtitle> textResults = [];
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            Uri textSearchUri = OpenSubtitlesHelper.BuildTextSearchUri(query.Text, query.LanguageCode);
            textResults = await SearchAsync(textSearchUri, cancellationToken);
        }

        return OpenSubtitlesHelper.MergeResults(hashResults, textResults);
    }

    /// <inheritdoc/>
    public async Task<StorageFile> DownloadAsync(OnlineSubtitle subtitle, CancellationToken cancellationToken = default)
    {
        byte[] data = await Client.GetByteArrayAsync(subtitle.DownloadUri, cancellationToken);
        string text = OpenSubtitlesHelper.DecodeSubtitleText(OpenSubtitlesHelper.DecompressIfGzip(data), subtitle.Encoding);

        // Save as UTF-8 with a byte order mark so that LibVLC does not have to guess the encoding.
        byte[] content = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];
        StorageFolder folder = await ApplicationData.Current.TemporaryFolder
            .CreateFolderAsync(SubtitleFolderName, CreationCollisionOption.OpenIfExists)
            .AsTask(cancellationToken);
        StorageFile file = await folder
            .CreateFileAsync(OpenSubtitlesHelper.GetSubtitleFileName(subtitle), CreationCollisionOption.ReplaceExisting)
            .AsTask(cancellationToken);
        await FileIO.WriteBytesAsync(file, content).AsTask(cancellationToken);
        return file;
    }

    /// <summary>
    /// Runs a single search request.
    /// </summary>
    private static async Task<IReadOnlyList<OnlineSubtitle>> SearchAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return OpenSubtitlesHelper.ParseSearchResults(json);
    }

    /// <summary>
    /// Creates the HTTP client shared by all requests, so that connections are reused between searches.
    /// </summary>
    private static HttpClient CreateClient()
    {
        HttpClient client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        client.DefaultRequestHeaders.Add("X-User-Agent", ApiUserAgent);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiUserAgent);
        return client;
    }
}
