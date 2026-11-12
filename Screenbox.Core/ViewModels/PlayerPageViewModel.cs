using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Screenbox.Core.ViewModels;

public sealed partial class PlayerPageViewModel : ObservableRecipient,
    IRecipient<TogglePlayerVisibilityMessage>,
    IRecipient<ShowSettingsPageMessage>,
    IRecipient<PlayerOsdUpdateMessage>,
    IRecipient<PropertyChangedMessage<IMediaPlayer?>>,
    IRecipient<QueueCurrentItemChangedMessage>,
    IRecipient<OverrideControlsHideDelayMessage>,
    IRecipient<DragDropMessage>,
    IRecipient<VisualizerChangedMessage>,
    IRecipient<PropertyChangedMessage<NavigationViewDisplayMode>>,
    IRecipient<PropertyChangedMessage<WindowViewMode>>
{
    [ObservableProperty] public partial bool ControlsHidden { get; set; }
    [ObservableProperty] public partial bool IsPlaying { get; set; }
    [ObservableProperty] public partial bool IsOpening { get; set; }
    [ObservableProperty] public partial bool AudioOnly { get; set; }
    [ObservableProperty] public partial WindowViewMode ViewMode { get; set; }
    [ObservableProperty] public partial NavigationViewDisplayMode NavigationViewDisplayMode { get; set; }
    [ObservableProperty] public partial MediaViewModel? Media { get; set; }
    [ObservableProperty] public partial bool ShowVisualizer { get; set; }

    /// <summary>
    /// Gets or sets a value that specifies the playback command of the on-screen display.
    /// </summary>
    /// <value>A value of the enumeration that indicates the playback command associated with the on-screen display.</value>
    [ObservableProperty]
    public partial PlaybackCommandKind CurrentPlaybackCommand { get; set; }

    /// <summary>
    /// Gets or sets whether the player OSD message is visible.
    /// </summary>
    /// <value><see langword="true"/> if the player OSD message is visible; otherwise, <see langword="false"/>.</value>
    [ObservableProperty]
    public partial bool IsOsdMessageVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the player OSD badge is visible.
    /// </summary>
    /// <value><see langword="true"/> if the player OSD badge is visible; otherwise, <see langword="false"/>.</value>
    [ObservableProperty]
    public partial bool IsOsdBadgeVisible { get; set; }

    /// <summary>
    /// Gets or sets the value for the player OSD message.
    /// </summary>
    /// <value>The value for the player OSD message.</value>
    [ObservableProperty]
    public partial object? OsdMessageValue { get; set; }

    /// <summary>
    /// Set to <see langword="true"/> by the view model to signal the view to close the play queue flyout.
    /// The view should reset this to <see langword="false"/> after closing the flyout.
    /// </summary>
    [ObservableProperty] public partial bool ShouldClosePlayQueueFlyout { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    [NotifyPropertyChangedFor(nameof(IsPlayerVisibilityVisible))]
    public partial PlayerVisibilityState PlayerVisibility { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    public partial MediaPlaybackState PlaybackState { get; set; }

    public bool SeekBarPointerInteracting { get; set; }

    public bool IsPlayerVisibilityVisible => PlayerVisibility is PlayerVisibilityState.Visible;

    /// <summary>
    /// Gets the key combinations assigned to the player actions.
    /// </summary>
    public HotkeyMap Hotkeys => _hotkeyContext.Map;

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _openingTimer;
    private readonly DispatcherQueueTimer _controlsVisibilityTimer;
    private readonly DispatcherQueueTimer _osdMessageTimer;
    private readonly DispatcherQueueTimer _osdBadgeTimer;
    private readonly DispatcherQueueTimer _playPauseHoldTimer;
    private readonly IWindowService _windowService;
    private readonly ISettingsService _settingsService;
    private readonly IFilesService _filesService;
    private readonly PlayerContext _playerContext;
    private readonly INavigationService _navigationService;
    private readonly HotkeyContext _hotkeyContext;
    private bool _visibilityOverride;
    private bool _resizeNext;
    private bool _isPlayPauseHoldActive;
    private double _playbackRateBeforeHold;

    public PlayerPageViewModel(IWindowService windowService,
        ISettingsService settingsService, IFilesService filesService, PlayerContext playerContext,
        INavigationService navigationService, HotkeyContext hotkeyContext)
    {
        _windowService = windowService;
        _settingsService = settingsService;
        _filesService = filesService;
        _playerContext = playerContext;
        _navigationService = navigationService;
        _hotkeyContext = hotkeyContext;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _openingTimer = _dispatcherQueue.CreateTimer();
        _controlsVisibilityTimer = _dispatcherQueue.CreateTimer();
        _osdMessageTimer = _dispatcherQueue.CreateTimer();
        _osdBadgeTimer = _dispatcherQueue.CreateTimer();
        _playPauseHoldTimer = _dispatcherQueue.CreateTimer();
        NavigationViewDisplayMode = Messenger.Send<NavigationViewDisplayModeRequestMessage>();
        PlayerVisibility = PlayerVisibilityState.Hidden;

        // Strong reference handlers. No need to unsubscribe since PlayerPageViewModel has the same lifetime as the app.
        FocusManager.GotFocus += FocusManagerOnFocusChanged;
        navigationService.Navigated += OnNavigationServiceNavigated;

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnStateChanged;
            MediaPlayer.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
        }

        Messenger.Register<TogglePlayerVisibilityMessage>(this);
        Messenger.Register<ShowSettingsPageMessage>(this);
        Messenger.Register<PlayerOsdUpdateMessage>(this);
        Messenger.Register<PropertyChangedMessage<IMediaPlayer?>>(this);
        Messenger.Register<QueueCurrentItemChangedMessage>(this);
        Messenger.Register<OverrideControlsHideDelayMessage>(this);
        Messenger.Register<DragDropMessage>(this);
        Messenger.Register<VisualizerChangedMessage>(this);
        Messenger.Register<PropertyChangedMessage<NavigationViewDisplayMode>>(this);
        Messenger.Register<PropertyChangedMessage<WindowViewMode>>(this);
    }

    public async void Receive(DragDropMessage message)
    {
        await OnDropAsync(message.Data);
    }

    public void Receive(VisualizerChangedMessage message)
    {
        if (message.Path is null) return;
        ShowVisualizer = AudioOnly && !string.IsNullOrEmpty(message.Path);
    }

    public void Receive(ShowSettingsPageMessage message)
    {
        // This comes from the player menu, which is still closing, and whose button belongs to the player
        // that is about to shrink into the mini player. Changing the page while the menu is going away
        // pulls the tree apart underneath it, so the page change waits for the menu to finish.
        _dispatcherQueue.TryEnqueue(ShowSettingsPage);
    }

    /// <summary>
    /// Shows the settings page.
    /// </summary>
    /// <remarks>
    /// Navigating brings the full player back to the mini player on its own, in
    /// <see cref="OnNavigationServiceNavigated"/>. Only full screen and compact overlay, which have a layout
    /// of their own, have to be left first.
    /// </remarks>
    public void ShowSettingsPage()
    {
        switch (_windowService.ViewMode)
        {
            case WindowViewMode.FullScreen:
                _windowService.ExitFullScreen();
                break;
            case WindowViewMode.Compact:
                // Leaving compact overlay takes a moment, and the page can only be shown afterwards.
                _ = ShowSettingsPageFromCompactAsync();
                return;
        }

        _navigationService.Navigate(typeof(SettingsPageViewModel));
    }

    private async Task ShowSettingsPageFromCompactAsync()
    {
        await _windowService.TryExitCompactLayoutAsync();
        _navigationService.Navigate(typeof(SettingsPageViewModel));
    }

    public void Receive(TogglePlayerVisibilityMessage message)
    {
        switch (PlayerVisibility)
        {
            case PlayerVisibilityState.Visible:
                GoBack();
                break;
            case PlayerVisibilityState.Minimal:
                RestorePlayer();
                break;
        }
    }

    public void Receive(PropertyChangedMessage<NavigationViewDisplayMode> message)
    {
        NavigationViewDisplayMode = message.NewValue;
    }

    public void Receive(PropertyChangedMessage<WindowViewMode> message)
    {
        if (message.Sender is not WindowContext) return;
        _dispatcherQueue.TryEnqueue(() => ViewMode = message.NewValue);
    }

    private void OnNavigationServiceNavigated(object? sender, EventArgs e)
    {
        if (PlayerVisibility != PlayerVisibilityState.Visible) return;
        GoBack();
        ShouldClosePlayQueueFlyout = true;
    }

    public void Receive(PropertyChangedMessage<IMediaPlayer?> message)
    {
        if (message.Sender is not PlayerContext) return;

        if (message.OldValue is { } oldPlayer)
        {
            oldPlayer.PlaybackStateChanged -= OnStateChanged;
            oldPlayer.NaturalVideoSizeChanged -= OnNaturalVideoSizeChanged;
        }

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnStateChanged;
            MediaPlayer.NaturalVideoSizeChanged += OnNaturalVideoSizeChanged;
        }
    }

    public void Receive(PlayerOsdUpdateMessage message)
    {
        bool shouldShowMessage = message.HasMessage && PlayerVisibility == PlayerVisibilityState.Visible;

        CurrentPlaybackCommand = message.Kind;
        OsdMessageValue = message.Value;

        // Message
        _dispatcherQueue.TryEnqueue(() =>
        {
            _osdMessageTimer.Stop();

            if (!shouldShowMessage)
            {
                IsOsdMessageVisible = false;
                return;
            }

            IsOsdMessageVisible = true;

            if (message.Duration != Timeout.InfiniteTimeSpan)
            {
                _osdMessageTimer.Debounce(() =>
                {
                    IsOsdMessageVisible = false;
                }, message.Duration);
            }
        });

        // Badge
        _dispatcherQueue.TryEnqueue(() =>
        {
            _osdBadgeTimer.Stop();

            if (!message.HasBadge)
            {
                IsOsdBadgeVisible = false;
                return;
            }

            IsOsdBadgeVisible = true;

            if (message.Duration != Timeout.InfiniteTimeSpan)
            {
                _osdBadgeTimer.Debounce(() =>
                {
                    IsOsdBadgeVisible = false;
                }, message.Duration);
            }
        });
    }

    public async void Receive(QueueCurrentItemChangedMessage message)
    {
        MediaViewModel? current = message.Value;
        _dispatcherQueue.TryEnqueue(() => UpdatePropertiesWithCurrentItem(current));
        if (current != null)
        {
            await current.LoadDetailsAsync(_filesService);
            await current.LoadThumbnailAsync();

            // Process again in case media type changed after loading details
            _dispatcherQueue.TryEnqueue(() => UpdatePropertiesWithCurrentItem(current));
        }
    }

    public void Receive(OverrideControlsHideDelayMessage message)
    {
        OverrideControlsDelayHide(message.Delay);
    }

    [DynamicWindowsRuntimeCast(typeof(StorageFile))]
    public async Task OnDropAsync(DataPackageView data)
    {
        try
        {
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                IReadOnlyList<IStorageItem>? items = await data.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    if (items.Count == 1 && items[0] is StorageFile file && file.IsSupportedSubtitle() &&
                        MediaPlayer is VlcMediaPlayer player && Media?.Item.Value != null)
                    {
                        Media.Item.Value.SubtitleTracks.AddExternalSubtitle(player, file, true);
                        Messenger.Send(new SubtitleAddedNotificationMessage(file));
                    }
                    else
                    {
                        Messenger.Send(new PlayFilesMessage(items));
                    }

                    return;
                }
            }

            if (data.Contains(StandardDataFormats.WebLink))
            {
                Uri? uri = await data.GetWebLinkAsync();
                if (uri.IsFile)
                {
                    Messenger.Send(new PlayMediaMessage(uri));
                }
            }
        }
        catch (Exception exception)
        {
            Messenger.Send(new MediaLoadFailedNotificationMessage(exception.Message, string.Empty));
        }
    }

    public bool OnPlayerClick()
    {
        if (!ControlsHidden) return (_settingsService.PlayerGestureTap == PlaybackActionKind.None) && TryHideControls(true);
        ControlsHidden = false;
        DelayHideControls();
        return true;
    }

    public void OnPointerMoved()
    {
        if (_visibilityOverride) return;
        ControlsHidden = false;

        if (SeekBarPointerInteracting) return;
        DelayHideControls();
    }

    /// <summary>
    /// Handles the play/pause key press interaction, initiating the hold
    /// behavior that temporarily increases playback speed.
    /// </summary>
    /// <remarks>
    /// If the media is currently <see cref="MediaPlaybackState.Paused"/> and
    /// <see cref="ISettingsService.PlayerGesturePressAndHold"/> is not enabled,
    /// the hold behavior is not available. To activate the hold behavior, the play/pause
    /// key needs to be pressed and held down for at least 500 milliseconds.
    /// </remarks>
    public void HandlePlayPauseKeyDown()
    {
        const double HoldingSpeed = 2.0;

        if (!_settingsService.PlayerGesturePressAndHold ||
            _isPlayPauseHoldActive ||
            MediaPlayer is null || MediaPlayer.PlaybackState is MediaPlaybackState.Paused)
            return;

        _playPauseHoldTimer.Debounce(() =>
        {
            _playbackRateBeforeHold = MediaPlayer.PlaybackRate;
            // If the rate is already faster than the holding speed, set it to twice the holding speed.
            double effectiveHoldingSpeed = MediaPlayer.PlaybackRate >= HoldingSpeed ? HoldingSpeed * 2.0 : HoldingSpeed;
            if (MediaPlayer.PlaybackRate != effectiveHoldingSpeed)
            {
                Messenger.Send(new ChangePlaybackRateRequestMessage(effectiveHoldingSpeed));
                Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.RateUp, value: effectiveHoldingSpeed).SetDuration(Timeout.InfiniteTimeSpan).ShowMessage());
            }

            _isPlayPauseHoldActive = true;
        }, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Handles the release of the play/pause key to either toggle playback or
    /// revert to the original playback rate.
    /// </summary>
    /// <remarks>
    /// If the play/pause key is pressed and released quickly, toggles playback state;
    /// if held, restores the original playback rate.
    /// </remarks>
    public void HandlePlayPauseKeyUp()
    {
        _playPauseHoldTimer.Stop();

        if (!_isPlayPauseHoldActive)
        {
            Messenger.Send(new TogglePlayPauseMessage(true));
        }
        else
        {
            if (MediaPlayer is not null && MediaPlayer.PlaybackRate != _playbackRateBeforeHold)
            {
                Messenger.Send(new ChangePlaybackRateRequestMessage(_playbackRateBeforeHold));
                Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.RateDown, value: _playbackRateBeforeHold).ShowMessage());
            }

            _isPlayPauseHoldActive = false;
        }
    }

    /// <summary>
    /// Shows the player controls and restarts the timer that hides them again, as moving the pointer does.
    /// </summary>
    public void ShowControls()
    {
        if (PlayerVisibility != PlayerVisibilityState.Visible) return;
        ControlsHidden = false;
        DelayHideControls();
    }

    /// <summary>
    /// Opens the play queue page.
    /// </summary>
    public void ShowPlayQueuePage()
    {
        _navigationService.Navigate(typeof(PlayQueuePageViewModel));
    }

    /// <summary>
    /// Resizes the window for a window size keyboard shortcut.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><see cref="PlayerHotkeyAction.ResizeWindowHalf"/>, <see cref="PlayerHotkeyAction.ResizeWindowOriginal"/> and
    /// <see cref="PlayerHotkeyAction.ResizeWindowOneAndHalf"/>: Resize to 50%, 100% or 150% of the video size.</description></item>
    /// <item><description><see cref="PlayerHotkeyAction.ResizeWindowToScreen"/>: Resize to fill the screen.</description></item>
    /// <item><description><see cref="PlayerHotkeyAction.GrowWindow"/> and <see cref="PlayerHotkeyAction.ShrinkWindow"/>: Change the window size by 10%.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="action">The window size action.</param>
    /// <param name="currentSize">The size of the current window.</param>
    public void ResizeWindow(PlayerHotkeyAction action, Size currentSize)
    {
        if (MediaPlayer is null)
            return;

        var videoSize = new Size(MediaPlayer.NaturalVideoWidth, MediaPlayer.NaturalVideoHeight);

        // Desired step is 10% of the current window size
        // However, 10% step doesn't always give a round number for resizing and rounding error will accumulate
        // We want to maintain the original aspect ratio as long as possible
        double stepHeight = Math.Round(currentSize.Height * 0.1);
        double stepWidth = Math.Round(currentSize.Width * 0.1);
        double desiredStep = Math.Min(stepWidth / currentSize.Width, stepHeight / currentSize.Height);

        double? scale = action switch
        {
            PlayerHotkeyAction.ResizeWindowHalf => 0.5,
            PlayerHotkeyAction.ResizeWindowOriginal => 1.0,
            PlayerHotkeyAction.ResizeWindowOneAndHalf => 1.5,
            PlayerHotkeyAction.ResizeWindowToScreen => 0.0,
            PlayerHotkeyAction.GrowWindow => 1 + desiredStep,
            PlayerHotkeyAction.ShrinkWindow => 1 - desiredStep,
            _ => null,
        };

        if (scale is null)
            return;

        bool isStep = action is PlayerHotkeyAction.GrowWindow or PlayerHotkeyAction.ShrinkWindow;
        double? newScalar = ResizeWindow(desiredSize: isStep ? currentSize : videoSize, scalar: scale.Value);
        if (newScalar is null or <= 0)
            return;

        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Scale, value: scale.Value).ShowMessage());
    }

    public void OnFileLaunched()
    {
        if (_settingsService.PlayerAutoResize == PlayerAutoResizeOption.OnLaunch)
            _resizeNext = true;
    }

    // Hidden button acts as a focus sink when controls are hidden
    public void HiddenButtonOnClick()
    {
        ControlsHidden = false;
        if (SystemInformation.IsDesktop)
        {
            // On Desktop, user expect Space to pause without needing to see the controls
            Messenger.Send(new TogglePlayPauseMessage(true));
        }
    }

    partial void OnControlsHiddenChanged(bool value)
    {
        if (value)
        {
            _windowService.HideCursor();
        }
        else
        {
            _windowService.ShowCursor();
        }

        Messenger.Send(new PlayerControlsVisibilityChangedMessage(!value));
    }

    partial void OnPlayerVisibilityChanged(PlayerVisibilityState value)
    {
        if (value != PlayerVisibilityState.Visible) ControlsHidden = false;
    }

    [RelayCommand]
    public void GoBack()
    {
        // Only allow back when not in fullscreen or compact overlay
        // Doing so would break layout logic
        switch (_windowService.ViewMode)
        {
            case WindowViewMode.FullScreen:
                _windowService.ExitFullScreen();
                break;
            case WindowViewMode.Compact:
                _windowService.TryExitCompactLayoutAsync();
                break;
            case WindowViewMode.Default:
                Playlist playlist = Messenger.Send(new QueueRequestMessage());
                bool hasItemsInQueue = playlist.Items.Count > 0;
                PlayerVisibility = hasItemsInQueue ? PlayerVisibilityState.Minimal : PlayerVisibilityState.Hidden;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [RelayCommand]
    private void RestorePlayer()
    {
        PlayerVisibility = PlayerVisibilityState.Visible;
    }

    [DynamicWindowsRuntimeCast(typeof(Control))]
    public bool TryHideControls(bool skipFocusCheck = false)
    {
        bool shouldCheckPlaying = _settingsService.PlayerShowControls && !IsPlaying;
        if (PlayerVisibility != PlayerVisibilityState.Visible || shouldCheckPlaying ||
            SeekBarPointerInteracting || AudioOnly || ControlsHidden) return false;

        if (!skipFocusCheck)
        {
            Control? focused = FocusManager.GetFocusedElement() as Control;
            // Don't hide controls when a Slider is in focus since user can interact with Slider
            // using arrow keys without affecting focus.
            if (focused is Slider { IsFocusEngaged: true }) return false;

            // Do not hide controls while a popup is open.
            bool isPopupOpen = VisualTreeHelper.GetOpenPopups(Window.Current).Any();
            if (isPopupOpen) return false;
        }

        ControlsHidden = true;

        // Workaround for PointerMoved is raised when show/hide cursor
        OverrideControlsDelayHide();

        return true;
    }

    private void DelayHideControls()
    {
        if (PlayerVisibility != PlayerVisibilityState.Visible || AudioOnly) return;

        int delayInSeconds = _settingsService.PlayerControlsHideDelay;
        _controlsVisibilityTimer.Debounce(() => TryHideControls(), TimeSpan.FromSeconds(delayInSeconds));
    }

    private void OverrideControlsDelayHide(int delay = 400)
    {
        _visibilityOverride = true;
        Task.Delay(delay).ContinueWith(_ => _visibilityOverride = false);
    }

    private void FocusManagerOnFocusChanged(object? sender, FocusManagerGotFocusEventArgs e)
    {
        if (_visibilityOverride) return;
        ControlsHidden = false;
        DelayHideControls();
    }

    private void UpdatePropertiesWithCurrentItem(MediaViewModel? current)
    {
        Media = current;
        AudioOnly = current == null || current.MediaType == MediaPlaybackType.Music;
        ShowVisualizer = current != null && AudioOnly && !string.IsNullOrEmpty(_settingsService.LivelyActivePath);
        if (current != null)
        {
            // Auto-resize player window
            bool shouldBeVisible = _settingsService.PlayerAutoResize == PlayerAutoResizeOption.Always && !AudioOnly;
            if (PlayerVisibility != PlayerVisibilityState.Visible)
            {
                PlayerVisibility = shouldBeVisible ? PlayerVisibilityState.Visible : PlayerVisibilityState.Minimal;
            }

            if (AudioOnly)
            {
                // If it's audio only, don't resize on next video playback
                _resizeNext = false;
            }
        }
        else if (PlayerVisibility == PlayerVisibilityState.Minimal)
        {
            PlayerVisibility = PlayerVisibilityState.Hidden;
        }
    }

    private void OnStateChanged(IMediaPlayer sender, object? args)
    {
        _openingTimer.Stop();
        MediaPlaybackState state = sender.PlaybackState;
        if (state == MediaPlaybackState.Opening)
        {
            _openingTimer.Debounce(() => IsOpening = state == MediaPlaybackState.Opening, TimeSpan.FromSeconds(0.5));
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            PlaybackState = state;
            IsPlaying = state == MediaPlaybackState.Playing;
            IsOpening = false;

            if (!IsPlaying && _settingsService.PlayerShowControls)
            {
                ControlsHidden = false;
            }

            if (!IsPlaying && !_settingsService.PlayerShowControls)
            {
                DelayHideControls();
            }

            if (!ControlsHidden && IsPlaying)
            {
                DelayHideControls();
            }
        });
    }

    private void OnNaturalVideoSizeChanged(IMediaPlayer sender, EventArgs args)
    {
        if (!_resizeNext && _settingsService.PlayerAutoResize != PlayerAutoResizeOption.Always) return;
        _resizeNext = false;

        _dispatcherQueue.TryEnqueue(() =>
        {
            Size desiredSize = new(sender.NaturalVideoWidth, sender.NaturalVideoHeight);
            if (ResizeWindow(desiredSize, 1).HasValue) return;

            // Resize to fill the screen only when video size is bigger than max window size
            Size maxWindowSize = _windowService.GetMaxWindowSize();
            if (sender.NaturalVideoWidth >= maxWindowSize.Width ||
                sender.NaturalVideoHeight >= maxWindowSize.Height)
                ResizeWindow(desiredSize, 0);
        });
    }

    private double? ResizeWindow(Size desiredSize, double scalar = 1)
    {
        if (scalar < 0 || _windowService.ViewMode != WindowViewMode.Default) return null;
        double actualScalar = _windowService.ResizeWindow(desiredSize, scalar);
        return actualScalar > 0 ? actualScalar : null;
    }
}
