using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.Storage;

namespace Screenbox.Core.ViewModels;

/// <summary>
/// Searches online subtitles for the playing media and loads the chosen subtitle into the player.
/// </summary>
public sealed partial class OnlineSubtitleSearchViewModel : ObservableRecipient
{
    /// <summary>
    /// Gets the languages that subtitles can be searched in.
    /// </summary>
    public IReadOnlyList<OnlineSubtitleLanguage> Languages => OpenSubtitlesHelper.Languages;

    /// <summary>
    /// Gets the subtitles found by the last search.
    /// </summary>
    public ObservableCollection<OnlineSubtitle> Results { get; } = new();

    /// <summary>
    /// Gets or sets the title to search for.
    /// </summary>
    [ObservableProperty] public partial string SearchText { get; set; }

    /// <summary>
    /// Gets or sets the language to search subtitles in.
    /// </summary>
    [ObservableProperty] public partial OnlineSubtitleLanguage SelectedLanguage { get; set; }

    /// <summary>
    /// Gets or sets the subtitle to download.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial OnlineSubtitle? SelectedSubtitle { get; set; }

    /// <summary>
    /// Gets or sets the state of the search.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial OnlineSubtitleSearchState State { get; set; }

    /// <summary>
    /// Occurs when a downloaded subtitle has been loaded into the player.
    /// </summary>
    public event EventHandler? SubtitleLoaded;

    private readonly IOnlineSubtitleService _onlineSubtitleService;
    private readonly PlayerContext _playerContext;
    private readonly ILogger<OnlineSubtitleSearchViewModel> _logger;
    private string? _movieHash;
    private long _movieByteSize;
    private bool _isInitialized;

    public OnlineSubtitleSearchViewModel(IOnlineSubtitleService onlineSubtitleService, ISettingsService settingsService,
        PlayerContext playerContext, ILogger<OnlineSubtitleSearchViewModel> logger)
    {
        _onlineSubtitleService = onlineSubtitleService;
        _playerContext = playerContext;
        _logger = logger;
        SearchText = string.Empty;
        SelectedLanguage = GetDefaultLanguage(settingsService.PersistentSubtitleLanguage);
        State = OnlineSubtitleSearchState.Idle;
    }

    /// <summary>
    /// Prepares a search for the playing media and runs it.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_playerContext.MediaPlayer?.PlaybackItem is not { } item)
        {
            State = OnlineSubtitleSearchState.NoMedia;
            return;
        }

        SearchText = OpenSubtitlesHelper.GetSearchText(GetSourceName(item.OriginalSource));
        (_movieHash, _movieByteSize) = await ComputeMovieHashAsync(item.OriginalSource);
        _isInitialized = true;
        await SearchCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Searches subtitles for the current title and language.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        State = OnlineSubtitleSearchState.Searching;
        SelectedSubtitle = null;
        Results.Clear();
        try
        {
            OnlineSubtitleSearchQuery query = new(SearchText.Trim(), _movieHash, _movieByteSize, SelectedLanguage.Code);
            IReadOnlyList<OnlineSubtitle> subtitles = await _onlineSubtitleService.SearchAsync(query, cancellationToken);
            foreach (OnlineSubtitle subtitle in subtitles)
            {
                Results.Add(subtitle);
            }

            SelectedSubtitle = Results.Count > 0 ? Results[0] : null;
            State = Results.Count > 0 ? OnlineSubtitleSearchState.HasResults : OnlineSubtitleSearchState.NoResults;
        }
        catch (OperationCanceledException)
        {
            State = OnlineSubtitleSearchState.Idle;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Online subtitle search failed.");
            State = OnlineSubtitleSearchState.SearchFailed;
        }
    }

    /// <summary>
    /// Downloads the selected subtitle and loads it into the player.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        if (SelectedSubtitle is not { } subtitle
            || _playerContext.MediaPlayer is not VlcMediaPlayer { PlaybackItem: { } item } player)
        {
            return;
        }

        OnlineSubtitleSearchState previousState = State;
        State = OnlineSubtitleSearchState.Downloading;
        try
        {
            StorageFile file = await _onlineSubtitleService.DownloadAsync(subtitle, cancellationToken);
            item.SubtitleTracks.AddExternalSubtitle(player, file, true);
            Messenger.Send(new SubtitleAddedNotificationMessage(file));
            State = previousState;
            SubtitleLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            State = previousState;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to download an online subtitle.");
            State = OnlineSubtitleSearchState.DownloadFailed;
        }
    }

    private bool CanSearch() =>
        State is not (OnlineSubtitleSearchState.Searching or OnlineSubtitleSearchState.Downloading);

    private bool CanDownload() => SelectedSubtitle is not null && CanSearch();

    partial void OnSelectedLanguageChanged(OnlineSubtitleLanguage value)
    {
        // Changing the language searches again, so the results always match the selected language.
        if (_isInitialized && SearchCommand.CanExecute(null))
        {
            SearchCommand.Execute(null);
        }
    }

    /// <summary>
    /// Gets the language to search in first, preferring the language of the last chosen subtitle.
    /// </summary>
    private static OnlineSubtitleLanguage GetDefaultLanguage(string persistentSubtitleLanguage)
    {
        string? languageTag = persistentSubtitleLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            languageTag = LanguageHelper.GetPreferredLanguage();
        }

        string code = OpenSubtitlesHelper.GetLanguageCode(languageTag);
        return OpenSubtitlesHelper.Languages.First(language => language.Code == code);
    }

    /// <summary>
    /// Gets the file name of a media source, which is used to derive the search title.
    /// </summary>
    [DynamicWindowsRuntimeCast(typeof(StorageFile))]
    private static string GetSourceName(object source)
    {
        return source switch
        {
            StorageFile file => file.Name,
            string path => Path.GetFileName(path),
            Uri uri => Path.GetFileName(uri.LocalPath),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Computes the hash of a local media file, so that subtitles timed for this exact release are listed first.
    /// </summary>
    /// <returns>The hash and the file size, or <see langword="null"/> and 0 if the source is not a readable file.</returns>
    [DynamicWindowsRuntimeCast(typeof(StorageFile))]
    private async Task<(string? Hash, long Size)> ComputeMovieHashAsync(object source)
    {
        try
        {
            StorageFile? file = source switch
            {
                StorageFile storageFile => storageFile,
                string path when Path.IsPathRooted(path) => await StorageFile.GetFileFromPathAsync(path),
                Uri { IsFile: true } uri => await StorageFile.GetFileFromPathAsync(uri.LocalPath),
                _ => null
            };

            if (file is null)
            {
                return (null, 0);
            }

            using Stream stream = await file.OpenStreamForReadAsync();
            string? hash = await OpenSubtitlesHelper.ComputeMovieHashAsync(stream);
            return (hash, stream.Length);
        }
        catch (Exception e)
        {
            // The hash only improves matching. Searching by title still works without it.
            _logger.LogWarning(e, "Failed to compute the subtitle hash of the playing media.");
            return (null, 0);
        }
    }
}
