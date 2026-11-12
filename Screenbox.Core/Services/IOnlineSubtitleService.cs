using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Screenbox.Core.Models;
using Windows.Storage;

namespace Screenbox.Core.Services;

/// <summary>
/// Represents a service that searches and downloads subtitles from an online subtitle database.
/// </summary>
public interface IOnlineSubtitleService
{
    /// <summary>
    /// Searches subtitles by file hash and by title.
    /// </summary>
    /// <param name="query">The search to run.</param>
    /// <param name="cancellationToken">A token to cancel the search.</param>
    /// <returns>The subtitles found, with subtitles that match the file hash first.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">The service could not be reached.</exception>
    Task<IReadOnlyList<OnlineSubtitle>> SearchAsync(OnlineSubtitleSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a subtitle into a local file that the player can load.
    /// </summary>
    /// <param name="subtitle">The subtitle to download.</param>
    /// <param name="cancellationToken">A token to cancel the download.</param>
    /// <returns>The downloaded subtitle file, encoded as UTF-8.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">The subtitle could not be downloaded.</exception>
    Task<StorageFile> DownloadAsync(OnlineSubtitle subtitle, CancellationToken cancellationToken = default);
}
