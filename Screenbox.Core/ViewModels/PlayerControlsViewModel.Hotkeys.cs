using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;

namespace Screenbox.Core.ViewModels;

// Performs the actions of the keyboard shortcuts. Most of them change state that this
// view model already owns, such as the playback rate, the timing offsets and the aspect ratio.
public sealed partial class PlayerControlsViewModel
{
    private enum DeinterlaceState
    {
        Automatic,
        Off,
        On,
    }

    // The speed step of the Screenbox speed shortcuts, which stay between 0.25x and 4x.
    private const double QuarterRateStep = 0.25;

    // Measures the time between the subtitle synchronization key presses, as VLC does.
    private static readonly Stopwatch SubtitleSyncClock = Stopwatch.StartNew();

    private int _savedSubtitleTrackIndex = -1;
    private TimeSpan? _subtitleSyncAudioBookmark;
    private TimeSpan? _subtitleSyncSubtitleBookmark;
    private string _displayAspectRatio = string.Empty;
    private double _videoZoom = 1;
    private bool _isVideoAutoscaled = true;
    private DeinterlaceState _deinterlaceState = DeinterlaceState.Automatic;
    private string _deinterlaceMode = VlcPlaybackHelper.AutomaticDeinterlaceMode;
    private string? _audioDeviceId;

    /// <summary>
    /// Gets the playback position of the current media.
    /// </summary>
    public TimeSpan Position => MediaPlayer?.Position ?? TimeSpan.Zero;

    /// <summary>
    /// Gets the duration of the current media.
    /// </summary>
    /// <value>The duration, or <see cref="TimeSpan.Zero"/> if it is unknown.</value>
    public TimeSpan NaturalDuration => MediaPlayer?.NaturalDuration ?? TimeSpan.Zero;

    /// <summary>
    /// Performs the player action of a keyboard shortcut and shows the result on the on-screen display.
    /// </summary>
    /// <remarks>
    /// Actions that need the view, such as opening a dialog or a menu, are ignored.
    /// </remarks>
    /// <param name="action">The action to perform.</param>
    public void ExecuteHotkey(PlayerHotkeyAction action)
    {
        switch (action)
        {
            case PlayerHotkeyAction.Play:
                // Like VLC, the play key returns to normal speed before it resumes playback.
                if (Math.Abs(PlaybackRate - 1) > 0.0001)
                {
                    ChangePlaybackRate(1);
                }
                else if (!IsPlaying)
                {
                    PlayPauseWithBadge();
                }

                break;
            case PlayerHotkeyAction.Pause:
                if (IsPlaying)
                {
                    PlayPauseWithBadge();
                }

                break;
            case PlayerHotkeyAction.Stop:
                Stop();
                break;
            case PlayerHotkeyAction.Next:
                if (NextCommand.CanExecute(null))
                {
                    NextCommand.Execute(null);
                }

                break;
            case PlayerHotkeyAction.Previous:
                if (PreviousCommand.CanExecute(null))
                {
                    PreviousCommand.Execute(null);
                }

                break;
            case PlayerHotkeyAction.NextChapter:
                GoToChapter(next: true);
                break;
            case PlayerHotkeyAction.PreviousChapter:
                GoToChapter(next: false);
                break;
            case PlayerHotkeyAction.Faster:
                ChangePlaybackRate(VlcPlaybackHelper.GetFasterRate(PlaybackRate));
                break;
            case PlayerHotkeyAction.Slower:
                ChangePlaybackRate(VlcPlaybackHelper.GetSlowerRate(PlaybackRate));
                break;
            case PlayerHotkeyAction.NormalRate:
                ChangePlaybackRate(1);
                break;
            case PlayerHotkeyAction.FasterFine:
                ChangePlaybackRate(VlcPlaybackHelper.GetFineRate(PlaybackRate, 1));
                break;
            case PlayerHotkeyAction.SlowerFine:
                ChangePlaybackRate(VlcPlaybackHelper.GetFineRate(PlaybackRate, -1));
                break;
            case PlayerHotkeyAction.FasterStep:
                ChangePlaybackRate(Math.Clamp(PlaybackRate + QuarterRateStep, QuarterRateStep, 4));
                break;
            case PlayerHotkeyAction.SlowerStep:
                ChangePlaybackRate(Math.Clamp(PlaybackRate - QuarterRateStep, QuarterRateStep, 4));
                break;
            case PlayerHotkeyAction.NextFrame:
                if (MediaPlayer?.PlaybackItem is not null)
                {
                    MediaPlayer.StepForwardOneFrame();
                }

                break;
            case PlayerHotkeyAction.PreviousFrame:
                // Stepping back seeks, which only lands on the previous frame while playback is paused.
                if (MediaPlayer is { CanSeek: true, PlaybackState: MediaPlaybackState.Paused })
                {
                    MediaPlayer.StepBackwardOneFrame();
                }

                break;
            case PlayerHotkeyAction.ShowPosition:
                if (MediaPlayer is not null)
                {
                    Messenger.SendPositionStatus(MediaPlayer.Position, MediaPlayer.NaturalDuration);
                }

                break;
            case PlayerHotkeyAction.JumpBackwardExtraShort:
                Jump(-VlcPlaybackHelper.ExtraShortJumpSeconds);
                break;
            case PlayerHotkeyAction.JumpForwardExtraShort:
                Jump(VlcPlaybackHelper.ExtraShortJumpSeconds);
                break;
            case PlayerHotkeyAction.JumpBackwardShort:
                // The short jump is the rewind and fast forward step from the settings, like the short jump length in VLC.
                Jump(-_settingsService.PlayerRewindStep);
                break;
            case PlayerHotkeyAction.JumpForwardShort:
                Jump(_settingsService.PlayerFastForwardStep);
                break;
            case PlayerHotkeyAction.JumpBackwardMedium:
                Jump(-VlcPlaybackHelper.MediumJumpSeconds);
                break;
            case PlayerHotkeyAction.JumpForwardMedium:
                Jump(VlcPlaybackHelper.MediumJumpSeconds);
                break;
            case PlayerHotkeyAction.JumpBackwardLong:
                Jump(-VlcPlaybackHelper.LongJumpSeconds);
                break;
            case PlayerHotkeyAction.JumpForwardLong:
                Jump(VlcPlaybackHelper.LongJumpSeconds);
                break;
            case PlayerHotkeyAction.JumpToStart:
                JumpToPosition(TimeSpan.Zero, string.Empty);
                break;
            case PlayerHotkeyAction.JumpToEnd:
                JumpToPosition(NaturalDuration, string.Empty);
                break;
            case >= PlayerHotkeyAction.JumpTo0Percent and <= PlayerHotkeyAction.JumpTo90Percent:
                int percent = (action - PlayerHotkeyAction.JumpTo0Percent) * 10;
                JumpToPosition(NaturalDuration * (percent / 100d), string.Create(CultureInfo.CurrentCulture, $"{percent}%"));
                break;
            case PlayerHotkeyAction.CycleLoop:
                PlayQueue.RepeatMode = VlcPlaybackHelper.GetNextRepeatMode(PlayQueue.RepeatMode);
                Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Loop, value: PlayQueue.RepeatMode).ShowMessage());
                break;
            case PlayerHotkeyAction.ToggleRandom:
                PlayQueue.ShuffleMode = !PlayQueue.ShuffleMode;
                Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Shuffle, value: PlayQueue.ShuffleMode).ShowMessage());
                break;
            case PlayerHotkeyAction.ClearPlayQueue:
                Messenger.Send(new ClearQueueMessage());
                break;
            case PlayerHotkeyAction.VolumeUp:
                ChangeVolume(VlcPlaybackHelper.VolumeStep);
                break;
            case PlayerHotkeyAction.VolumeDown:
                ChangeVolume(-VlcPlaybackHelper.VolumeStep);
                break;
            case PlayerHotkeyAction.CycleAudioTrack:
                CycleAudioTrack();
                break;
            case PlayerHotkeyAction.CycleAudioDevice:
                _ = CycleAudioDeviceAsync();
                break;
            case PlayerHotkeyAction.AudioDelayUp:
                ChangeAudioDelay(VlcPlaybackHelper.DelayStep);
                break;
            case PlayerHotkeyAction.AudioDelayDown:
                ChangeAudioDelay(-VlcPlaybackHelper.DelayStep);
                break;
            case PlayerHotkeyAction.CycleSubtitleTrack:
                CycleSubtitleTrack(reverse: false);
                break;
            case PlayerHotkeyAction.CycleSubtitleTrackReverse:
                CycleSubtitleTrack(reverse: true);
                break;
            case PlayerHotkeyAction.ToggleSubtitles:
                ToggleSubtitles();
                break;
            case PlayerHotkeyAction.SubtitleDelayUp:
                ChangeSubtitleDelay(VlcPlaybackHelper.DelayStep);
                break;
            case PlayerHotkeyAction.SubtitleDelayDown:
                ChangeSubtitleDelay(-VlcPlaybackHelper.DelayStep);
                break;
            case PlayerHotkeyAction.SubtitleSyncBookmarkAudio:
                _subtitleSyncAudioBookmark = SubtitleSyncClock.Elapsed;
                SendSubtitleSyncStatus(new SubtitleSyncStatus(SubtitleSyncStep.AudioBookmarked));
                break;
            case PlayerHotkeyAction.SubtitleSyncBookmarkSubtitle:
                if (!HasActiveSubtitle())
                {
                    SendSubtitleSyncStatus(new SubtitleSyncStatus(SubtitleSyncStep.NoActiveSubtitle));
                    break;
                }

                _subtitleSyncSubtitleBookmark = SubtitleSyncClock.Elapsed;
                SendSubtitleSyncStatus(new SubtitleSyncStatus(SubtitleSyncStep.SubtitleBookmarked));
                break;
            case PlayerHotkeyAction.SubtitleSyncApply:
                ApplySubtitleSync();
                break;
            case PlayerHotkeyAction.SubtitleSyncReset:
                SubtitleTimingOffset = 0;
                ClearSubtitleSyncBookmarks();
                SendSubtitleSyncStatus(new SubtitleSyncStatus(SubtitleSyncStep.Reset));
                break;
            case PlayerHotkeyAction.CycleAspectRatio:
                CycleDisplayAspectRatio();
                break;
            case PlayerHotkeyAction.CycleCrop:
                CycleCrop();
                break;
            case PlayerHotkeyAction.ToggleAutoscale:
                if (Math.Abs(_videoZoom - 1) > 0.001)
                {
                    // VLC first resets a custom zoom to the original size.
                    _videoZoom = 1;
                    SetVideoZoom(_videoZoom);
                }
                else
                {
                    SetVideoScale(!_isVideoAutoscaled);
                }

                break;
            case PlayerHotkeyAction.ScaleUp:
                SetVideoZoom(VlcPlaybackHelper.GetScaledUpZoom(_videoZoom));
                break;
            case PlayerHotkeyAction.ScaleDown:
                SetVideoZoom(VlcPlaybackHelper.GetScaledDownZoom(_videoZoom));
                break;
            case PlayerHotkeyAction.NextZoomMode:
                SetVideoZoom(VlcPlaybackHelper.GetNextZoomMode(_videoZoom, forward: true));
                break;
            case PlayerHotkeyAction.PreviousZoomMode:
                SetVideoZoom(VlcPlaybackHelper.GetNextZoomMode(_videoZoom, forward: false));
                break;
            case PlayerHotkeyAction.ZoomQuarter:
                SetVideoZoom(0.25);
                break;
            case PlayerHotkeyAction.ZoomHalf:
                SetVideoZoom(0.5);
                break;
            case PlayerHotkeyAction.ZoomOriginal:
                SetVideoZoom(1);
                break;
            case PlayerHotkeyAction.ZoomDouble:
                SetVideoZoom(2);
                break;
            case PlayerHotkeyAction.ToggleDeinterlace:
                ToggleDeinterlace();
                break;
            case PlayerHotkeyAction.CycleDeinterlaceMode:
                CycleDeinterlaceMode();
                break;
            case PlayerHotkeyAction.ToggleFullscreen:
                ToggleFullscreenCommand.Execute(null);
                break;
            case PlayerHotkeyAction.SaveFrame:
                if (SaveSnapshotCommand.CanExecute(null))
                {
                    SaveSnapshotCommand.Execute(null);
                }

                break;
            case PlayerHotkeyAction.ToggleCompactOverlay:
                ToggleCompactLayoutCommand.Execute(null);
                break;
        }
    }

    private void JumpToPosition(TimeSpan position, string percentText)
    {
        if (MediaPlayer is null)
        {
            return;
        }

        PositionChangedResult result = Messenger.Send(new ChangeTimeRequestMessage(position));
        Messenger.SendPositionStatus(result.NewPosition, result.NaturalDuration, percentText);
    }

    /// <summary>
    /// Moves playback to the specified time and shows the new position on the on-screen display.
    /// </summary>
    /// <param name="time">The time to move to. Times past the end of the media move to the end.</param>
    public void JumpToTime(TimeSpan time)
    {
        if (MediaPlayer is not { CanSeek: true })
        {
            return;
        }

        PositionChangedResult result = Messenger.Send(new ChangeTimeRequestMessage(time, debounce: false));
        Messenger.SendPositionStatus(result.NewPosition, result.NaturalDuration);
    }

    /// <summary>
    /// Clears the shortcut state that belongs to the media that stopped playing.
    /// </summary>
    private void ResetHotkeyStateForNewMedia()
    {
        _savedSubtitleTrackIndex = -1;
        ClearSubtitleSyncBookmarks();

        // The aspect ratio and the zoom apply to the player rather than the media,
        // so they would otherwise stretch or zoom the next video as well.
        bool hasCustomAspectRatio = _displayAspectRatio.Length > 0;
        bool hasCustomScale = !_isVideoAutoscaled || Math.Abs(_videoZoom - 1) > 0.001;
        _displayAspectRatio = string.Empty;
        _videoZoom = 1;
        _isVideoAutoscaled = true;

        if (MediaPlayer is VlcMediaPlayer player)
        {
            if (hasCustomAspectRatio)
            {
                player.AspectRatio = null;
            }

            if (hasCustomScale)
            {
                player.VideoScale = 0;
            }
        }
    }

    /// <summary>
    /// Forgets the shortcut state that was applied to a player that has been replaced.
    /// </summary>
    private void ResetHotkeyStateForNewPlayer()
    {
        _savedSubtitleTrackIndex = -1;
        ClearSubtitleSyncBookmarks();
        _displayAspectRatio = string.Empty;
        _videoZoom = 1;
        _isVideoAutoscaled = true;
        _deinterlaceState = DeinterlaceState.Automatic;
        _deinterlaceMode = VlcPlaybackHelper.AutomaticDeinterlaceMode;
        _audioDeviceId = null;
    }

    private void Stop()
    {
        if (MediaPlayer?.PlaybackState is not (MediaPlaybackState.Playing or MediaPlaybackState.Paused or MediaPlaybackState.Buffering))
        {
            return;
        }

        // VLC unloads the media on stop, so that playing again starts from the beginning. Pausing at
        // the beginning gives the same result without closing the video, which would empty the player.
        MediaPlayer.Pause();
        if (MediaPlayer.CanSeek)
        {
            Messenger.Send(new ChangeTimeRequestMessage(TimeSpan.Zero, debounce: false));
        }

        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Stop).ShowBadge());
    }

    private void GoToChapter(bool next)
    {
        if (MediaPlayer is not { CanSeek: true, PlaybackItem: { } item } || item.Chapters.Count == 0)
        {
            return;
        }

        TimeSpan[] startTimes = item.Chapters.Select(chapter => chapter.StartTime).ToArray();
        int index = next
            ? VlcPlaybackHelper.GetNextChapterIndex(startTimes, MediaPlayer.Position)
            : VlcPlaybackHelper.GetPreviousChapterIndex(startTimes, MediaPlayer.Position);
        if (index < 0)
        {
            return;
        }

        ChapterCue chapter = item.Chapters[index];
        Messenger.Send(new ChangeTimeRequestMessage(chapter.StartTime, debounce: false));
        Messenger.Send(
            new PlayerOsdUpdateMessage(
                next ? PlaybackCommandKind.NextChapter : PlaybackCommandKind.PreviousChapter,
                value: new PlayerOsdItem(chapter.Title, index + 1))
            .ShowMessage());
    }

    private void ChangePlaybackRate(double rate)
    {
        double oldRate = PlaybackRate;
        PlaybackRate = rate;
        Messenger.Send(
            new PlayerOsdUpdateMessage(
                rate < oldRate ? PlaybackCommandKind.RateDown : PlaybackCommandKind.RateUp,
                value: rate)
            .ShowBadge()
            .ShowMessage());
    }

    private void Jump(int seconds)
    {
        if (MediaPlayer is not { CanSeek: true })
        {
            return;
        }

        Messenger.SendSeekWithStatus(TimeSpan.FromSeconds(seconds));
    }

    private void ChangeVolume(int delta)
    {
        int volume = Messenger.Send(new ChangeVolumeRequestMessage(delta, true));
        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Volume, value: volume).ShowBadge().ShowMessage());
    }

    private void CycleAudioTrack()
    {
        if (MediaPlayer?.PlaybackItem is not { } item)
        {
            return;
        }

        PlaybackAudioTrackList tracks = item.AudioTracks;
        int index = VlcPlaybackHelper.GetNextAudioTrackIndex(tracks.SelectedIndex, tracks.Count);
        if (index < 0)
        {
            return;
        }

        tracks.SelectedIndex = index;
        Messenger.Send(
            new PlayerOsdUpdateMessage(PlaybackCommandKind.AudioTrack, value: new PlayerOsdItem(tracks[index].Label, index + 1))
            .ShowMessage());
    }

    private async Task CycleAudioDeviceAsync()
    {
        try
        {
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            if (devices.Count == 0 || MediaPlayer is null)
            {
                return;
            }

            // The player follows the default device until a device is picked with this key.
            string currentId = _audioDeviceId ?? MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default) ?? string.Empty;
            int index = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
                {
                    index = (i + 1) % devices.Count;
                    break;
                }
            }

            DeviceInformation device = devices[index];
            MediaPlayer.AudioDevice = device;
            _audioDeviceId = device.Id;
            Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.AudioDevice, value: device.Name).ShowMessage());
        }
        catch (Exception)
        {
            // Enumerating devices fails when the audio service is unavailable. The current device keeps playing.
        }
    }

    private void ChangeAudioDelay(double delta)
    {
        AudioTimingOffset += delta;
        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.AudioDelay, value: AudioTimingOffset).ShowMessage());
    }

    private void ChangeSubtitleDelay(double delta)
    {
        // Like VLC, the delay only changes while a subtitle is showing. A null value reports that there is none.
        if (!HasActiveSubtitle())
        {
            Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.SubtitleDelay).ShowMessage());
            return;
        }

        SubtitleTimingOffset += delta;
        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.SubtitleDelay, value: SubtitleTimingOffset).ShowMessage());
    }

    private bool HasActiveSubtitle()
    {
        return MediaPlayer?.PlaybackItem is { SubtitleTracks.SelectedIndex: >= 0 };
    }

    private void CycleSubtitleTrack(bool reverse)
    {
        if (MediaPlayer?.PlaybackItem is not { } item)
        {
            return;
        }

        PlaybackSubtitleTrackList tracks = item.SubtitleTracks;
        int index = VlcPlaybackHelper.GetNextSubtitleTrackIndex(tracks.SelectedIndex, tracks.Count, reverse);
        tracks.SelectedIndex = index;
        SendSubtitleTrackStatus(tracks, index);
    }

    private void ToggleSubtitles()
    {
        if (MediaPlayer?.PlaybackItem is not { } item)
        {
            return;
        }

        PlaybackSubtitleTrackList tracks = item.SubtitleTracks;
        int index = -1;
        if (tracks.SelectedIndex >= 0)
        {
            _savedSubtitleTrackIndex = tracks.SelectedIndex;
        }
        else if (tracks.Count > 0)
        {
            // Restore the track that was turned off, or the first track if none was.
            index = _savedSubtitleTrackIndex >= 0 && _savedSubtitleTrackIndex < tracks.Count ? _savedSubtitleTrackIndex : 0;
            _savedSubtitleTrackIndex = -1;
        }

        tracks.SelectedIndex = index;
        SendSubtitleTrackStatus(tracks, index);
    }

    private void SendSubtitleTrackStatus(PlaybackSubtitleTrackList tracks, int index)
    {
        PlayerOsdUpdateMessage message = index < 0
            ? new PlayerOsdUpdateMessage(PlaybackCommandKind.SubtitleOff)
            : new PlayerOsdUpdateMessage(PlaybackCommandKind.Subtitle, value: new PlayerOsdItem(tracks[index].Label, index + 1));
        Messenger.Send(message.ShowMessage());
    }

    private void ApplySubtitleSync()
    {
        if (_subtitleSyncAudioBookmark is not { } audioBookmark || _subtitleSyncSubtitleBookmark is not { } subtitleBookmark)
        {
            SendSubtitleSyncStatus(new SubtitleSyncStatus(SubtitleSyncStep.MissingBookmarks));
            return;
        }

        double correction = VlcPlaybackHelper.GetSubtitleSyncCorrection(audioBookmark, subtitleBookmark, PlaybackRate);
        SubtitleTimingOffset += correction;
        ClearSubtitleSyncBookmarks();
        SendSubtitleSyncStatus(new SubtitleSyncStatus(SubtitleSyncStep.Applied, (int)correction, (int)Math.Round(SubtitleTimingOffset)));
    }

    private void ClearSubtitleSyncBookmarks()
    {
        _subtitleSyncAudioBookmark = null;
        _subtitleSyncSubtitleBookmark = null;
    }

    private void SendSubtitleSyncStatus(SubtitleSyncStatus status)
    {
        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.SubtitleSync, value: status).ShowMessage());
    }

    private void CycleDisplayAspectRatio()
    {
        if (!HasVideo || MediaPlayer is not VlcMediaPlayer player)
        {
            return;
        }

        _displayAspectRatio = VlcPlaybackHelper.GetNextAspectRatio(_displayAspectRatio);
        player.AspectRatio = _displayAspectRatio;

        // The default aspect ratio is reported with the resource name of its localized label.
        string label = VlcPlaybackHelper.GetAspectRatioLabel(_displayAspectRatio);
        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.AspectRatio, value: label.Length > 0 ? label : "Default").ShowMessage());
    }

    private void CycleCrop()
    {
        if (!HasVideo)
        {
            return;
        }

        string cropMode = VlcPlaybackHelper.GetNextCropMode(AspectRatio);
        if (TryApplyAspectRatio(cropMode))
        {
            Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Crop, value: cropMode).ShowMessage());
        }
    }

    private void SetVideoZoom(double zoom)
    {
        // Like VLC, choosing a zoom factor stops fitting the video to the view.
        _videoZoom = zoom;
        SetVideoScale(isAutoscaled: false);
    }

    private void SetVideoScale(bool isAutoscaled)
    {
        if (!HasVideo || MediaPlayer is not VlcMediaPlayer player)
        {
            return;
        }

        _isVideoAutoscaled = isAutoscaled;
        double scale = isAutoscaled ? 0 : _videoZoom;
        player.VideoScale = scale;
        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Zoom, value: scale).ShowMessage());
    }

    private void ToggleDeinterlace()
    {
        if (!HasVideo || MediaPlayer is not VlcMediaPlayer player)
        {
            return;
        }

        // Automatic deinterlacing counts as on, so the first key press turns it off, as in VLC.
        if (_deinterlaceState is DeinterlaceState.Off)
        {
            _deinterlaceState = DeinterlaceState.On;
            player.SetDeinterlace(VlcPlaybackHelper.GetLibVlcDeinterlaceMode(_deinterlaceMode));
            SendDeinterlaceStatus(new DeinterlaceStatus(true, VlcPlaybackHelper.GetDeinterlaceModeLabel(_deinterlaceMode)));
        }
        else
        {
            _deinterlaceState = DeinterlaceState.Off;
            player.SetDeinterlace(null);
            SendDeinterlaceStatus(new DeinterlaceStatus(false, null));
        }
    }

    private void CycleDeinterlaceMode()
    {
        if (!HasVideo || MediaPlayer is not VlcMediaPlayer player)
        {
            return;
        }

        _deinterlaceMode = VlcPlaybackHelper.GetNextDeinterlaceMode(_deinterlaceMode);
        string label = VlcPlaybackHelper.GetDeinterlaceModeLabel(_deinterlaceMode);
        if (_deinterlaceState is DeinterlaceState.Off)
        {
            // The mode is remembered for when deinterlacing is turned on.
            SendDeinterlaceStatus(new DeinterlaceStatus(false, label));
            return;
        }

        // LibVLC cannot keep automatic deinterlacing with a different mode, so choosing a mode turns deinterlacing on.
        _deinterlaceState = DeinterlaceState.On;
        player.SetDeinterlace(VlcPlaybackHelper.GetLibVlcDeinterlaceMode(_deinterlaceMode));
        SendDeinterlaceStatus(new DeinterlaceStatus(true, label));
    }

    private void SendDeinterlaceStatus(DeinterlaceStatus status)
    {
        Messenger.Send(new PlayerOsdUpdateMessage(PlaybackCommandKind.Deinterlace, value: status).ShowMessage());
    }
}
