using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Screenbox.Core.Contexts;
using Screenbox.Core.Coordinators;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Screenbox.Core.ViewModels;

public sealed partial class PlayerControlsViewModel : ObservableRecipient,
    IRecipient<PropertyChangedMessage<IMediaPlayer?>>,
    IRecipient<SettingsChangedMessage>,
    IRecipient<TogglePlayPauseMessage>,
    IRecipient<ChangePlaybackRateRequestMessage>,
    IRecipient<PropertyChangedMessage<PlayerVisibilityState>>,
    IRecipient<PropertyChangedMessage<WindowViewMode>>
{
    /// <summary>
    /// The observable play queue state. Bind to this for <c>Items</c>,
    /// <c>CurrentItem</c>, <c>ShuffleMode</c>, and <c>RepeatMode</c>.
    /// </summary>
    public PlayQueueContext PlayQueue { get; }

    public bool ShouldBeAdaptive => !IsCompact && SystemInformation.IsDesktop;

    /// <summary>
    /// Gets the key combinations assigned to the player actions.
    /// </summary>
    public HotkeyMap Hotkeys => _hotkeyContext.Map;

    [ObservableProperty] public partial bool IsPlaying { get; set; }
    [ObservableProperty] public partial bool IsFullscreen { get; set; }
    [ObservableProperty] public partial string? TitleName { get; set; } // TODO: Handle VLC title name
    [ObservableProperty] public partial double PlaybackRate { get; set; }
    [ObservableProperty] public partial double AudioTimingOffset { get; set; }
    [ObservableProperty] public partial double SubtitleTimingOffset { get; set; }
    [ObservableProperty] public partial bool IsAdvancedModeActive { get; set; }
    [ObservableProperty] public partial bool IsMinimal { get; set; }
    [ObservableProperty] public partial bool PlayerShowChapters { get; set; }
    [ObservableProperty] public partial bool IsDisplayingRemainingTime { get; set; }

    /// <summary>
    /// Gets the aspect ratio option that the video is cropped to.
    /// </summary>
    /// <value><c>Fit</c>, <c>Fill</c>, or a ratio in <c>width:height</c> form, such as <c>16:9</c>.</value>
    [ObservableProperty] public partial string AspectRatio { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldBeAdaptive))]
    public partial bool IsCompact { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSnapshotCommand))]
    public partial bool HasVideo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    public partial bool HasActiveItem { get; set; }

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IWindowService _windowService;
    private readonly ISettingsService _settingsService;
    private readonly PlayerContext _playerContext;
    private readonly IFilesService _filesService;
    private readonly IPlayQueueCoordinator _coordinator;
    private readonly HotkeyContext _hotkeyContext;

    private Size _aspectRatio;

    public PlayerControlsViewModel(
        PlayQueueContext playQueue,
        IFilesService filesService,
        IPlayQueueCoordinator coordinator,
        ISettingsService settingsService,
        IWindowService windowService,
        PlayerContext playerContext,
        HotkeyContext hotkeyContext)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _windowService = windowService;
        _settingsService = settingsService;
        _playerContext = playerContext;
        _filesService = filesService;
        _coordinator = coordinator;
        _hotkeyContext = hotkeyContext;
        PlaybackRate = 1.0;
        AudioTimingOffset = 0.0;
        SubtitleTimingOffset = 0.0;
        AspectRatio = "Fit";
        IsAdvancedModeActive = settingsService.AdvancedMode;
        IsMinimal = true;
        IsDisplayingRemainingTime = settingsService.PersistentShowRemainingTime;
        PlayerShowChapters = settingsService.PlayerShowChapters;
        PlayQueue = playQueue;
        PlayQueue.PropertyChanged += PlayQueueOnPropertyChanged;
        _coordinator.CanNavigateChanged += OnCoordinatorCanNavigateChanged;

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            MediaPlayer.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
        }

        Messenger.Register<PropertyChangedMessage<IMediaPlayer?>>(this);
        Messenger.Register<SettingsChangedMessage>(this);
        Messenger.Register<TogglePlayPauseMessage>(this);
        Messenger.Register<ChangePlaybackRateRequestMessage>(this);
        Messenger.Register<PropertyChangedMessage<PlayerVisibilityState>>(this);
        Messenger.Register<PropertyChangedMessage<WindowViewMode>>(this);
    }

    public void Receive(SettingsChangedMessage message)
    {
        switch (message.SettingsName)
        {
            case nameof(SettingsPageViewModel.AdvancedMode):
                IsAdvancedModeActive = _settingsService.AdvancedMode;
                break;
            case nameof(SettingsPageViewModel.PlayerShowChapters):
                PlayerShowChapters = _settingsService.PlayerShowChapters;
                break;
        }
    }

    public void Receive(PropertyChangedMessage<IMediaPlayer?> message)
    {
        if (message.Sender is not PlayerContext) return;
        if (message.OldValue is { } oldPlayer)
        {
            oldPlayer.PlaybackStateChanged -= OnPlaybackStateChanged;
            oldPlayer.NaturalVideoSizeChanged -= OnNaturalVideoSizeChanged;
        }

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            MediaPlayer.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
        }

        // A new player starts with the default aspect ratio, zoom and deinterlacing.
        ResetHotkeyStateForNewPlayer();
    }

    public void Receive(TogglePlayPauseMessage message)
    {
        if (!HasActiveItem || MediaPlayer == null) return;
        if (message.ShowBadge)
        {
            PlayPauseWithBadge();
        }
        else
        {
            PlayPause();
        }
    }

    public void Receive(ChangePlaybackRateRequestMessage message)
    {
        SetPlaybackRate(message.Value);
        message.Reply(PlaybackRate);
    }

    public void Receive(PropertyChangedMessage<PlayerVisibilityState> message)
    {
        IsMinimal = message.NewValue != PlayerVisibilityState.Visible;
    }

    /// <summary>
    /// Toggles the playback state of the active item and displays a badge indicating the new state.
    /// </summary>
    public void PlayPauseWithBadge()
    {
        if (!HasActiveItem)
            return;

        PlayPause();
        Messenger.Send(new PlayerOsdUpdateMessage(IsPlaying ? PlaybackCommandKind.Pause : PlaybackCommandKind.Play).ShowBadge());
    }

    partial void OnIsDisplayingRemainingTimeChanged(bool value)
    {
        _settingsService.PersistentShowRemainingTime = value;
    }

    partial void OnPlaybackRateChanged(double value)
    {
        if (MediaPlayer == null) return;
        MediaPlayer.PlaybackRate = value;
    }

    partial void OnAudioTimingOffsetChanged(double value)
    {
        if (MediaPlayer == null) return;

        if (MediaPlayer is VlcMediaPlayer vlcMediaPlayer)
        {
            vlcMediaPlayer.AudioDelay = value;
        }
    }

    partial void OnSubtitleTimingOffsetChanged(double value)
    {
        if (MediaPlayer == null) return;

        if (MediaPlayer is VlcMediaPlayer vlcMediaPlayer)
        {
            vlcMediaPlayer.SubtitleDelay = value;
        }
    }

    private void PlayQueueOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayQueueContext.CurrentItem):
                HasActiveItem = PlayQueue.CurrentItem is not null;
                SubtitleTimingOffset = 0;
                AudioTimingOffset = 0;
                ResetHotkeyStateForNewMedia();
                break;
        }
    }

    private void OnNaturalVideoSizeChanged(IMediaPlayer sender, object? args)
    {
        _dispatcherQueue.TryEnqueue(() => HasVideo = MediaPlayer?.NaturalVideoHeight > 0);
    }

    private void OnPlaybackStateChanged(IMediaPlayer sender, object? args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsPlaying = sender.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Opening;
        });
    }

    public void Receive(PropertyChangedMessage<WindowViewMode> message)
    {
        if (message.Sender is not WindowContext) return;
        switch (message.NewValue)
        {
            case WindowViewMode.Default:
                IsFullscreen = false;
                IsCompact = false;
                break;
            case WindowViewMode.Compact:
                IsCompact = true;
                IsFullscreen = false;
                break;
            case WindowViewMode.FullScreen:
                IsFullscreen = true;
                IsCompact = false;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void OnCoordinatorCanNavigateChanged(object? sender, EventArgs e)
    {
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task Next() => await _coordinator.NextAsync();

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task Previous() => await _coordinator.PreviousAsync();

    private bool CanGoNext() => _coordinator.CanNext();

    private bool CanGoPrevious() => _coordinator.CanPrevious();

    [RelayCommand]
    private void ResetMediaPlayback()
    {
        if (MediaPlayer is null) return;
        TimeSpan pos = MediaPlayer.Position;
        _coordinator.ResetCurrentItem();
        _dispatcherQueue.TryEnqueue(() =>
        {
            MediaPlayer.Play();
            MediaPlayer.Position = pos;
        });
    }

    [RelayCommand]
    private void SetPlaybackRate(double rate)
    {
        PlaybackRate = rate;
    }

    /// <summary>
    /// Shows the settings page. The player page brings the player back to the mini player first.
    /// </summary>
    [RelayCommand]
    private void ShowSettings()
    {
        Messenger.Send(new ShowSettingsPageMessage());
    }

    [RelayCommand]
    private void SetAspectRatio(string aspect)
    {
        if (TryApplyAspectRatio(aspect))
        {
            Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.AspectRatio, value: aspect).ShowMessage());
        }
    }

    /// <summary>
    /// Crops the video to an aspect ratio option.
    /// </summary>
    /// <param name="aspect"><c>Fit</c>, <c>Fill</c>, or a ratio in <c>width:height</c> form, such as <c>16:9</c>.</param>
    /// <returns><see langword="true"/> if the option is valid and was applied; otherwise, <see langword="false"/>.</returns>
    private bool TryApplyAspectRatio(string aspect)
    {
        switch (aspect)
        {
            case "Fit":
                _aspectRatio = new Size(0, 0);
                break;
            case "Fill":
                _aspectRatio = new Size(double.NaN, double.NaN);
                break;
            default:
                string[] values = aspect.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != 2) return false;
                if (!double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)) return false;
                if (!double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)) return false;
                _aspectRatio = new Size(width, height);
                break;
        }

        AspectRatio = aspect;
        Messenger.Send(new ChangeAspectRatioMessage(_aspectRatio));
        return true;
    }

    [RelayCommand]
    private async Task ToggleCompactLayoutAsync()
    {
        if (IsCompact)
        {
            await _windowService.TryExitCompactLayoutAsync();
            return;
        }

        await _windowService.TryEnterCompactLayoutAsync(GetCompactOverlaySize());
    }

    /// <summary>
    /// Gets the size to open the compact overlay ("picture in picture") window at.
    /// </summary>
    /// <remarks>
    /// The window keeps the size it was last used at. Without one, it opens as large as Windows allows for
    /// such a window, which is 500 effective pixels and at most half the screen in each direction.
    /// </remarks>
    /// <returns>The size of the window, in effective pixels.</returns>
    private Size GetCompactOverlaySize()
    {
        Size rememberedSize = _settingsService.PlayerCompactOverlaySize;
        if (rememberedSize is { Width: >= 1, Height: >= 1 })
        {
            return rememberedSize;
        }

        double aspectRatio = MediaPlayer?.NaturalVideoHeight > 0
            ? MediaPlayer.NaturalVideoWidth / (double)MediaPlayer.NaturalVideoHeight
            : 1;
        Size maxSize = _windowService.GetMaxCompactOverlaySize();
        double height = Math.Min(maxSize.Height, maxSize.Width / aspectRatio);
        return new Size(height * aspectRatio, height);
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        if (IsCompact) return;
        if (IsFullscreen)
        {
            _windowService.ExitFullScreen();
        }
        else
        {
            _windowService.TryEnterFullScreen();
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveItem))]
    private void PlayPause()
    {
        if (IsPlaying)
        {
            MediaPlayer?.Pause();
        }
        else
        {
            MediaPlayer?.Play();
        }
    }

    /// <summary>
    /// Saves a snapshot of the current video frame to the configured folder, falling back
    /// to the Pictures library when the folder is not set or cannot be accessed.
    /// Sends a <see cref="FailedToSaveFrameNotificationMessage"/> on failure.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasVideo))]
    private async Task SaveSnapshotAsync()
    {
        if (MediaPlayer?.PlaybackState is not (MediaPlaybackState.Paused or MediaPlaybackState.Playing)) return;
        try
        {
            StorageFile file = await SaveSnapshotInternalAsync(MediaPlayer);
            Messenger.Send(new RaiseFrameSavedNotificationMessage(file));
        }
        catch (UnauthorizedAccessException)
        {
            Messenger.Send(new RaiseLibraryAccessDeniedNotificationMessage(KnownLibraryId.Pictures));
        }
        catch (Exception e)
        {
            Messenger.Send(new FailedToSaveFrameNotificationMessage(e.Message));
        }
    }

    private async Task<StorageFile> SaveSnapshotInternalAsync(IMediaPlayer mediaPlayer)
    {
        if (mediaPlayer is not VlcMediaPlayer player)
        {
            throw new NotImplementedException("Not supported on non VLC players");
        }

        StorageFolder tempFolder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(
            $"snapshot_{DateTimeOffset.Now.Ticks}",
            CreationCollisionOption.FailIfExists);

        try
        {
            if (!player.VlcPlayer.TakeSnapshot(0, tempFolder.Path, 0, 0))
                throw new Exception("VLC failed to save snapshot");

            StorageFile file = (await tempFolder.GetFilesAsync())[0];
            StorageFolder destFolder = await _filesService.GetFrameCaptureFolderAsync(_settingsService.FrameCaptureFolderToken);
            return await file.CopyAsync(destFolder, $"Screenbox_{DateTimeOffset.Now:yyyyMMdd_HHmmss}{file.FileType}",
                NameCollisionOption.GenerateUniqueName);
        }
        finally
        {
            await tempFolder.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
    }
}
