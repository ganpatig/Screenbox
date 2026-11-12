using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Coordinators;
using Screenbox.Core.Enums;
using Screenbox.Core.Events;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Xaml.Controls.Primitives;

namespace Screenbox.Core.ViewModels;

public sealed partial class SeekBarViewModel :
    ObservableRecipient,
    IRecipient<TimeChangeOverrideMessage>,
    IRecipient<ChangeTimeRequestMessage>,
    IRecipient<PlayerControlsVisibilityChangedMessage>,
    IRecipient<QueueCurrentItemChangedMessage>,
    IRecipient<SettingsChangedMessage>,
    IRecipient<PropertyChangedMessage<PlayerVisibilityState>>,
    IRecipient<PropertyChangedMessage<IMediaPlayer?>>
{
    [ObservableProperty] public partial double Length { get; set; }

    [ObservableProperty] public partial double Time { get; set; }

    [ObservableProperty] public partial bool IsSeekable { get; set; }

    [ObservableProperty] public partial bool BufferingVisible { get; set; }

    [ObservableProperty] public partial double PreviewTime { get; set; }

    [ObservableProperty] public partial bool ShouldShowPreview { get; set; }

    [ObservableProperty] public partial bool ShouldHandleKeyDown { get; set; }

    [ObservableProperty] public partial ChapterCue? CurrentChapterCue { get; set; }

    /// <summary>
    /// Gets or sets the most recently decoded seek preview frame.
    /// </summary>
    /// <value>The decoded frame, or <see langword="null"/> if no frame is available for the playing media.</value>
    [ObservableProperty] public partial SeekPreviewFrame? PreviewFrame { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the seek preview shows the video frame at the hovered position.
    /// </summary>
    [ObservableProperty] public partial bool IsSeekPreviewEnabled { get; set; }

    public ObservableCollection<ChapterCue> Chapters { get; }

    private TimeSpan NaturalDuration => TimeSpan.FromMilliseconds(Length);

    private TimeSpan Position
    {
        get => TimeSpan.FromMilliseconds(Time);
        set => Time = value.TotalMilliseconds;
    }

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    private readonly ISettingsService _settingsService;
    private readonly PlayerContext _playerContext;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _bufferingTimer;
    private readonly DispatcherQueueTimer _seekTimer;
    private readonly DispatcherQueueTimer _originalPositionTimer;
    private readonly IPlaybackProgressTracker _playbackProgressTracker;
    private readonly ISeekPreviewCoordinator _seekPreviewCoordinator;
    private readonly DispatcherQueueTimer _previewTimer;
    private TimeSpan _originalPosition;
    private TimeSpan _lastTrackedPosition;
    private bool? _seekDirection;
    private bool _timeChangeOverride;
    private MediaViewModel? _currentItem;
    private int _previewFrameVersion;
    private double _shownPreviewTime;
    private SeekPreviewQuality _shownPreviewQuality;

    /// <summary>
    /// How long the hovered position has to stay still before the frame at exactly that position is decoded.
    /// </summary>
    private static readonly TimeSpan ExactPreviewDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How far a fast preview frame may be from the hovered position before it is too old to show. It matches
    /// the span of positions that share one keyframe preview.
    /// </summary>
    private const double FastPreviewToleranceMilliseconds = 1000;

    public SeekBarViewModel(ISettingsService settingsService, IPlaybackProgressTracker playbackProgressTracker,
        PlayerContext playerContext, ISeekPreviewCoordinator seekPreviewCoordinator)
    {
        _settingsService = settingsService;
        _playbackProgressTracker = playbackProgressTracker;
        _playerContext = playerContext;
        _seekPreviewCoordinator = seekPreviewCoordinator;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _bufferingTimer = _dispatcherQueue.CreateTimer();
        _seekTimer = _dispatcherQueue.CreateTimer();
        _originalPositionTimer = _dispatcherQueue.CreateTimer();
        _originalPositionTimer.IsRepeating = false;
        _previewTimer = _dispatcherQueue.CreateTimer();
        _previewTimer.IsRepeating = false;
        ShouldShowPreview = true;
        ShouldHandleKeyDown = true;
        IsSeekPreviewEnabled = settingsService.PlayerShowSeekPreview;
        Chapters = new ObservableCollection<ChapterCue>();

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            MediaPlayer.NaturalDurationChanged += OnNaturalDurationChanged;
            MediaPlayer.PositionChanged += OnPositionChanged;
            MediaPlayer.ChapterChanged += OnChapterChanged;
            MediaPlayer.MediaEnded += OnEndReached;
            MediaPlayer.BufferingStarted += OnBufferingStarted;
            MediaPlayer.BufferingEnded += OnBufferingEnded;
            MediaPlayer.PlaybackItemChanged += OnPlaybackItemChanged;
            MediaPlayer.CanSeekChanged += OnCanSeekChanged;
        }

        Messenger.Register<TimeChangeOverrideMessage>(this);
        Messenger.Register<ChangeTimeRequestMessage>(this);
        Messenger.Register<PlayerControlsVisibilityChangedMessage>(this);
        Messenger.Register<QueueCurrentItemChangedMessage>(this);
        Messenger.Register<SettingsChangedMessage>(this);
        Messenger.Register<PropertyChangedMessage<PlayerVisibilityState>>(this);
        Messenger.Register<PropertyChangedMessage<IMediaPlayer?>>(this);
    }

    public void Receive(SettingsChangedMessage message)
    {
        if (message.SettingsName == nameof(SettingsPageViewModel.PlayerShowSeekPreview))
        {
            IsSeekPreviewEnabled = _settingsService.PlayerShowSeekPreview;
        }
    }

    public void Receive(QueueCurrentItemChangedMessage message)
    {
        _lastTrackedPosition = TimeSpan.Zero;
        _currentItem = message.Value;
        if (message.Value != null && _playbackProgressTracker.IsLoaded)
        {
            RestoreLastPosition(message.Value);
        }
    }

    public void Receive(PropertyChangedMessage<PlayerVisibilityState> message)
    {
        ShouldHandleKeyDown = message.NewValue != PlayerVisibilityState.Visible;
    }

    public void Receive(PlayerControlsVisibilityChangedMessage message)
    {
        if (!message.Value && ShouldShowPreview)
        {
            ShouldShowPreview = false;
        }
    }

    public async void Receive(PropertyChangedMessage<IMediaPlayer?> message)
    {
        if (message.Sender is not PlayerContext) return;

        if (message.OldValue is { } oldPlayer)
        {
            oldPlayer.PlaybackStateChanged -= OnPlaybackStateChanged;
            oldPlayer.NaturalDurationChanged -= OnNaturalDurationChanged;
            oldPlayer.PositionChanged -= OnPositionChanged;
            oldPlayer.ChapterChanged -= OnChapterChanged;
            oldPlayer.MediaEnded -= OnEndReached;
            oldPlayer.BufferingStarted -= OnBufferingStarted;
            oldPlayer.BufferingEnded -= OnBufferingEnded;
            oldPlayer.PlaybackItemChanged -= OnPlaybackItemChanged;
            oldPlayer.CanSeekChanged -= OnCanSeekChanged;
        }

        if (MediaPlayer != null)
        {
            MediaPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            MediaPlayer.NaturalDurationChanged += OnNaturalDurationChanged;
            MediaPlayer.PositionChanged += OnPositionChanged;
            MediaPlayer.ChapterChanged += OnChapterChanged;
            MediaPlayer.MediaEnded += OnEndReached;
            MediaPlayer.BufferingStarted += OnBufferingStarted;
            MediaPlayer.BufferingEnded += OnBufferingEnded;
            MediaPlayer.PlaybackItemChanged += OnPlaybackItemChanged;
            MediaPlayer.CanSeekChanged += OnCanSeekChanged;

            if (!_playbackProgressTracker.IsLoaded)
            {
                await _playbackProgressTracker.LoadFromDiskAsync();
                if (_currentItem != null)
                {
                    RestoreLastPosition(_currentItem);
                }
            }
        }
    }

    public void Receive(TimeChangeOverrideMessage message)
    {
        _timeChangeOverride = message.Value;
    }

    public void Receive(ChangeTimeRequestMessage message)
    {
        var result = UpdatePosition(message.Value, message.IsOffset, message.Debounce);
        message.Reply(result);
    }

    public void OnSeekBarPointerEvent(bool pressed)
    {
        _timeChangeOverride = pressed;
    }

    public void UpdatePreviewTime(double normalizedPosition)
    {
        normalizedPosition = Math.Clamp(normalizedPosition, 0, 1);
        PreviewTime = (long)(normalizedPosition * Length);

        // A keyframe decodes quickly enough to follow the pointer, so it keeps the preview moving while scrubbing.
        RequestPreviewFrame(SeekPreviewQuality.Fast);

        // The frame at exactly this position can take seconds to decode, so it is only decoded once the
        // user settles on a position.
        _previewTimer.Debounce(() => RequestPreviewFrame(SeekPreviewQuality.Exact), ExactPreviewDelay);
    }

    /// <summary>
    /// Interprets a pointer wheel input and adjusts the media playback position accordingly.
    /// </summary>
    /// <remarks>
    /// The seek duration is determined by the following modifier keys:
    /// <list type="bullet">
    /// <item><description><see cref="VirtualKeyModifiers.None"/>: Uses the default step.</description></item>
    /// <item><description><see cref="VirtualKeyModifiers.Control"/>: Increases the step.</description></item>
    /// <item><description><see cref="VirtualKeyModifiers.Shift"/>: Decreases the step.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="pointerWheelDelta">The pointer wheel delta value.</param>
    /// <param name="modifiers">A bitwise combination of the enumeration values that specifies the modifier keys held during the change.</param>
    public void OnSeekBarPointerWheelChanged(double pointerWheelDelta, VirtualKeyModifiers modifiers)
    {
        if (!IsSeekable || MediaPlayer is null) return;

        int delta = modifiers switch
        {
            VirtualKeyModifiers.Control => 10_000,
            VirtualKeyModifiers.Shift => 2_000,
            _ => 5_000
        };

        int signedDelta = pointerWheelDelta > 0 ? delta : -delta;
        var result = UpdatePosition(TimeSpan.FromMilliseconds(signedDelta), isOffset: true, debounce: true);
        TimeSpan offset = result.NewPosition - result.OriginalPosition;
        string extra = $"{(offset > TimeSpan.Zero ? '+' : string.Empty)}{Humanizer.ToDuration(offset)}";

        Messenger.SendPositionStatus(result.NewPosition, result.NaturalDuration, extra);
    }

    public void OnSeekBarValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        var newPosition = TimeSpan.FromMilliseconds(args.NewValue);
        // Only update player position when there is a user interaction.
        // SeekBar should have OneWay binding to Time, so when Time changes and invokes
        // this handler, Time = args.NewValue. The only exception is when the change is
        // coming from user.
        // We can detect user interaction by checking if Time != args.NewValue
        if (IsSeekable && MediaPlayer != null && Math.Abs(Time - args.NewValue) > 50)
        {
            Time = args.NewValue;
            double currentMs = MediaPlayer.Position.TotalMilliseconds;
            double newDiffMs = Math.Abs(args.NewValue - currentMs);
            bool shouldUpdate = newDiffMs > 400;
            bool shouldOverride = _timeChangeOverride && newDiffMs > 100;
            bool paused = MediaPlayer.PlaybackState is MediaPlaybackState.Paused or MediaPlaybackState.Buffering;
            if (shouldUpdate || paused || shouldOverride)
            {
                SetPlayerPosition(newPosition, true);
            }
        }

        UpdateProgress(newPosition);
    }

    /// <summary>
    /// Seeks the media playback position to the specified chapter.
    /// </summary>
    /// <param name="chapter">The chapter to seek.</param>
    [RelayCommand]
    private void SeekToChapter(ChapterCue chapter)
    {
        Messenger.Send(new ChangeTimeRequestMessage(chapter.StartTime, debounce: false));
    }

    private void RestoreLastPosition(MediaViewModel media)
    {
        if (!_settingsService.PersistPlaybackPosition) return;

        TimeSpan lastPosition = _playbackProgressTracker.GetPosition(media.Location);
        if (lastPosition <= TimeSpan.Zero) return;
        if (_settingsService.RestorePlaybackPosition)
        {
            if (media.IsPlaying)
            {
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => SetPlayerPosition(lastPosition, false));
                _lastTrackedPosition = TimeSpan.Zero;
            }
            else
            {
                // Media is not seekable yet, so we need to wait for the PlaybackStateChanged event
                _lastTrackedPosition = lastPosition;
            }
        }
        else
        {
            Messenger.Send(new RaiseResumePositionNotificationMessage(lastPosition));
        }
    }

    private PositionChangedResult UpdatePosition(TimeSpan position, bool isOffset, bool debounce)
    {
        TimeSpan currentPosition = Position;

        if (isOffset && position != TimeSpan.Zero)
        {
            bool isSeekingForward = position > TimeSpan.Zero;

            // If direction changed, treat this as the start of a new seek gesture.
            if (_seekDirection != isSeekingForward)
            {
                _originalPosition = currentPosition;
                _seekDirection = isSeekingForward;
            }

            _originalPositionTimer.Debounce(() => _originalPosition = currentPosition, TimeSpan.FromSeconds(1), true);
        }
        else
        {
            _originalPositionTimer.Stop();
            _originalPosition = currentPosition;
            _seekDirection = null;
        }

        // Assume UI thread
        Position = isOffset ? (currentPosition + position) switch
        {
            var newPosition when newPosition < TimeSpan.Zero => TimeSpan.Zero,
            var newPosition when newPosition > NaturalDuration => NaturalDuration,
            var newPosition => newPosition
        } : position;
        SetPlayerPosition(Position, debounce);

        return new PositionChangedResult(currentPosition, Position, _originalPosition, NaturalDuration);
    }

    private void SetPlayerPosition(TimeSpan position, bool debounce)
    {
        if (!IsSeekable || MediaPlayer == null) return;
        if (debounce)
        {
            _seekTimer.Debounce(() => MediaPlayer.Position = position, TimeSpan.FromMilliseconds(50));
        }
        else
        {
            _seekTimer.Stop();
            MediaPlayer.Position = position;
        }
    }

    private void OnCanSeekChanged(IMediaPlayer sender, EventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsSeekable = sender.CanSeek;
        });
    }

    private void OnPlaybackStateChanged(IMediaPlayer sender, ValueChangedEventArgs<MediaPlaybackState> args)
    {
        if (args.NewValue is not (MediaPlaybackState.None or MediaPlaybackState.Opening) &&
            _lastTrackedPosition > TimeSpan.Zero)
        {
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                SetPlayerPosition(_lastTrackedPosition, false);
                _lastTrackedPosition = TimeSpan.Zero;
            });
        }
    }

    private void OnPlaybackItemChanged(IMediaPlayer sender, object? args)
    {
        _seekTimer.Stop();
        if (sender.PlaybackItem == null)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsSeekable = false;
                Time = 0;
                CurrentChapterCue = null;
                Chapters.Clear();
                ClearPreviewFrame();
            });
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                Time = 0;
                CurrentChapterCue = null;
                Chapters.Clear();
                ClearPreviewFrame();
            });
        }
    }

    /// <summary>
    /// Requests the video frame at <see cref="PreviewTime"/> and shows it once it has been decoded.
    /// </summary>
    /// <param name="quality">How closely the frame has to match the hovered position.</param>
    private async void RequestPreviewFrame(SeekPreviewQuality quality)
    {
        if (!IsSeekPreviewEnabled || !IsSeekable || Length <= 0) return;

        int version = _previewFrameVersion;
        SeekPreviewFrame? frame =
            await _seekPreviewCoordinator.GetFrameAsync(TimeSpan.FromMilliseconds(PreviewTime), quality);

        // Discard frames of media that was replaced, or previews that were disabled, while the frame was decoding.
        if (frame is null || version != _previewFrameVersion || !IsSeekPreviewEnabled) return;

        // Frames do not arrive in the order they were requested. Showing one for a position that the pointer
        // has already left makes the preview run through old frames on its way to the current one.
        double tolerance = quality is SeekPreviewQuality.Exact ? 0 : FastPreviewToleranceMilliseconds;
        if (Math.Abs(frame.Position.TotalMilliseconds - PreviewTime) > tolerance) return;

        // A keyframe must not replace the exact frame of the position that is being hovered.
        if (quality is SeekPreviewQuality.Fast && _shownPreviewQuality is SeekPreviewQuality.Exact &&
            Math.Abs(_shownPreviewTime - PreviewTime) <= tolerance) return;

        _shownPreviewTime = frame.Position.TotalMilliseconds;
        _shownPreviewQuality = quality;
        PreviewFrame = frame;
    }

    /// <summary>
    /// Removes the current preview frame and invalidates frames that are still being decoded.
    /// </summary>
    private void ClearPreviewFrame()
    {
        _previewFrameVersion++;
        _previewTimer.Stop();
        _shownPreviewQuality = SeekPreviewQuality.Fast;
        _shownPreviewTime = 0;
        PreviewFrame = null;
    }

    partial void OnIsSeekPreviewEnabledChanged(bool value)
    {
        if (!value)
        {
            ClearPreviewFrame();
        }
    }

    partial void OnShouldShowPreviewChanged(bool value)
    {
        // Stop decoding positions the user is no longer looking at. A frame already being decoded still
        // completes and is cached for the next hover.
        if (!value)
        {
            _previewTimer.Stop();
            _seekPreviewCoordinator.CancelPendingRequest();
        }
    }

    private void OnBufferingEnded(IMediaPlayer sender, object? args)
    {
        _bufferingTimer.Stop();
        _dispatcherQueue.TryEnqueue(() => BufferingVisible = false);
    }

    private void OnBufferingStarted(IMediaPlayer sender, object? args)
    {
        // When the player is paused, the following still triggers a buffering
        if (sender.Position == sender.NaturalDuration)
            return;

        // Only show buffering if it takes more than 0.5s
        _bufferingTimer.Debounce(() => BufferingVisible = true, TimeSpan.FromSeconds(0.5));
    }

    private void OnPositionChanged(IMediaPlayer sender, object? args)
    {
        if (_seekTimer.IsRunning || _timeChangeOverride) return;
        _dispatcherQueue.TryEnqueue(() =>
        {
            Time = sender.Position.TotalMilliseconds;
        });
    }

    private void OnChapterChanged(IMediaPlayer sender, ValueChangedEventArgs<ChapterCue?> args)
    {
        // Small delay to prevent boundary retriggers.
        _originalPositionTimer.Debounce(() =>
        {
            CurrentChapterCue = args.NewValue;
        }, TimeSpan.FromMilliseconds(100));
    }

    private void OnNaturalDurationChanged(IMediaPlayer sender, object? args)
    {
        // Natural duration can fluctuate during playback
        // Do not rely on this event to detect media changes
        _dispatcherQueue.TryEnqueue(() =>
        {
            Length = sender.NaturalDuration.TotalMilliseconds;
            IsSeekable = sender.CanSeek;
            UpdateChapters(sender.PlaybackItem?.Chapters);
        });
    }

    private void OnEndReached(IMediaPlayer sender, object? args)
    {
        if (!_timeChangeOverride)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                // Check if Time is close enough to Length. Sometimes a new file is already loaded at this point.
                if (Length - Time is > 0 and < 400)
                {
                    // Round Time to Length to avoid gap at the end
                    Time = Length;
                }
            });
        }
    }

    private void UpdateChapters(PlaybackChapterList? chapterList)
    {
        Chapters.Clear();
        if (chapterList == null) return;
        if (MediaPlayer != null)
        {
            chapterList.Load(MediaPlayer);
        }

        foreach (ChapterCue chapterCue in chapterList)
        {
            Chapters.Add(chapterCue);
        }
        // Chapters.SyncItems(chapterList);
    }

    private void UpdateProgress(TimeSpan position)
    {
        if (!_settingsService.PersistPlaybackPosition ||
            _currentItem == null || NaturalDuration <= TimeSpan.FromMinutes(1) ||
            DateTimeOffset.Now - _playbackProgressTracker.LastUpdated <= TimeSpan.FromSeconds(3))
            return;

        if (position > TimeSpan.FromSeconds(30) && position + TimeSpan.FromSeconds(10) < NaturalDuration)
        {
            _playbackProgressTracker.UpdateProgress(_currentItem.Location, position);
        }
        else if (position > TimeSpan.FromSeconds(5))
        {
            _playbackProgressTracker.RemovePosition(_currentItem.Location);
        }
    }
}
