using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Media;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Provides the calculations behind the VLC keyboard shortcuts, reproducing the behavior of VLC media player 3.0.
/// </summary>
public static class VlcPlaybackHelper
{
    /// <summary>
    /// The slowest playback rate VLC supports.
    /// </summary>
    public const double MinimumRate = 1d / 32;

    /// <summary>
    /// The fastest playback rate VLC supports.
    /// </summary>
    public const double MaximumRate = 31.25;

    /// <summary>
    /// The volume change of one volume key press, in percent.
    /// </summary>
    public const int VolumeStep = 5;

    /// <summary>
    /// The audio or subtitle delay change of one delay key press, in milliseconds.
    /// </summary>
    public const double DelayStep = 50;

    /// <summary>
    /// The length of an extra short jump, in seconds.
    /// </summary>
    public const int ExtraShortJumpSeconds = 3;

    /// <summary>
    /// The length of a medium jump, in seconds.
    /// </summary>
    public const int MediumJumpSeconds = 60;

    /// <summary>
    /// The length of a long jump, in seconds.
    /// </summary>
    public const int LongJumpSeconds = 300;

    /// <summary>
    /// The deinterlace mode VLC uses when it picks one automatically.
    /// </summary>
    public const string AutomaticDeinterlaceMode = "auto";

    // Rates stepped through by the faster and slower keys, from src/playlist/engine.c.
    private static readonly double[] Rates =
    [
        1d / 64, 1d / 32, 1d / 16, 1d / 8, 1d / 4, 1d / 3, 1d / 2, 2d / 3,
        1,
        3d / 2, 2, 3, 4, 8, 16, 32, 64,
    ];

    private static readonly double[] ZoomModes = [0.25, 0.5, 1, 2];

    private static readonly string[] AspectRatioValues = ["", "16:9", "4:3", "1:1", "16:10", "221:100", "235:100", "239:100", "5:4"];

    private static readonly string[] AspectRatioLabels = ["", "16:9", "4:3", "1:1", "16:10", "2.21:1", "2.35:1", "2.39:1", "5:4"];

    // VLC crops to the default picture first. Screenbox also offers Fill, which crops the video to the shape of the window.
    private static readonly string[] CropModes = ["Fit", "Fill", "16:10", "16:9", "4:3", "1.85:1", "2.21:1", "2.35:1", "2.39:1", "5:3", "5:4", "1:1"];

    private static readonly string[] DeinterlaceModes = ["auto", "discard", "blend", "mean", "bob", "linear", "x", "yadif", "yadif2x", "phosphor", "ivtc"];

    private static readonly string[] DeinterlaceModeLabels = ["Auto", "Discard", "Blend", "Mean", "Bob", "Linear", "X", "Yadif", "Yadif (2x)", "Phosphor", "Film NTSC (IVTC)"];

    /// <summary>
    /// Gets the playback rate after pressing the faster key.
    /// </summary>
    /// <param name="rate">The current playback rate.</param>
    /// <returns>The next preset rate that is at least 10% faster, limited to <see cref="MaximumRate"/>.</returns>
    /// <example>
    /// <code>
    /// VlcPlaybackHelper.GetFasterRate(1);   // 1.5
    /// VlcPlaybackHelper.GetFasterRate(1.5); // 2
    /// </code>
    /// </example>
    public static double GetFasterRate(double rate)
    {
        // The 10% margin keeps a rate that is slightly off a preset, such as 1.4999, from stopping at that preset.
        double target = rate * 1.1;
        foreach (double preset in Rates)
        {
            if (preset > target)
            {
                target = preset;
                break;
            }
        }

        return Math.Clamp(target, MinimumRate, MaximumRate);
    }

    /// <summary>
    /// Gets the playback rate after pressing the slower key.
    /// </summary>
    /// <param name="rate">The current playback rate.</param>
    /// <returns>The previous preset rate that is at least 10% slower, limited to <see cref="MinimumRate"/>.</returns>
    public static double GetSlowerRate(double rate)
    {
        double target = rate * 0.9;
        for (int i = 1; i < Rates.Length; i++)
        {
            if (Rates[i] >= target)
            {
                target = Rates[i - 1];
                break;
            }
        }

        return Math.Clamp(target, MinimumRate, MaximumRate);
    }

    /// <summary>
    /// Gets the playback rate after pressing the fine faster or fine slower key.
    /// </summary>
    /// <param name="rate">The current playback rate.</param>
    /// <param name="direction">1 to speed up, or -1 to slow down.</param>
    /// <returns>The rate rounded to the next multiple of 0.1 in the specified direction.</returns>
    public static double GetFineRate(double rate, int direction)
    {
        double target = Math.Floor(Math.Abs(rate) / 0.1 + Math.Sign(direction) + 0.05) * 0.1;

        // Round away the binary error of multiplying by 0.1 so that the steps stay at exact tenths.
        return Math.Clamp(Math.Round(target, 1), MinimumRate, MaximumRate);
    }

    /// <summary>
    /// Gets the zoom mode after pressing the zoom or unzoom key.
    /// </summary>
    /// <param name="zoom">The current zoom factor.</param>
    /// <param name="forward"><see langword="true"/> for the next zoom mode; <see langword="false"/> for the previous one.</param>
    /// <returns>One of the zoom factors 0.25, 0.5, 1 and 2. A zoom that is not one of them moves to 0.25.</returns>
    public static double GetNextZoomMode(double zoom, bool forward)
    {
        int index = Array.FindIndex(ZoomModes, mode => Math.Abs(mode - zoom) < 0.001);
        if (index < 0)
        {
            return ZoomModes[0];
        }

        index += forward ? 1 : -1;
        if (index == ZoomModes.Length)
        {
            index = 0;
        }
        else if (index < 0)
        {
            index = ZoomModes.Length - 1;
        }

        return ZoomModes[index];
    }

    /// <summary>
    /// Gets the zoom factor after pressing the scale up key.
    /// </summary>
    /// <param name="zoom">The current zoom factor.</param>
    /// <returns>The zoom factor increased by 0.1, up to a factor of 10.</returns>
    public static double GetScaledUpZoom(double zoom)
    {
        return zoom < 10 ? Math.Round(zoom + 0.1, 2) : zoom;
    }

    /// <summary>
    /// Gets the zoom factor after pressing the scale down key.
    /// </summary>
    /// <param name="zoom">The current zoom factor.</param>
    /// <returns>The zoom factor decreased by 0.1, down to a factor of 0.3.</returns>
    public static double GetScaledDownZoom(double zoom)
    {
        return zoom > 0.3 + 0.001 ? Math.Round(zoom - 0.1, 2) : zoom;
    }

    /// <summary>
    /// Gets the display aspect ratio that follows the specified one.
    /// </summary>
    /// <param name="aspectRatio">The current aspect ratio in <c>num:den</c> form, or an empty string for the default.</param>
    /// <returns>The next aspect ratio in <c>num:den</c> form, or an empty string for the default.</returns>
    public static string GetNextAspectRatio(string aspectRatio)
    {
        return GetNext(AspectRatioValues, aspectRatio);
    }

    /// <summary>
    /// Gets the readable name of a display aspect ratio.
    /// </summary>
    /// <param name="aspectRatio">The aspect ratio in <c>num:den</c> form, such as <c>221:100</c>.</param>
    /// <returns>The name, such as <c>2.21:1</c>, or an empty string for the default aspect ratio.</returns>
    public static string GetAspectRatioLabel(string aspectRatio)
    {
        int index = Array.IndexOf(AspectRatioValues, aspectRatio);
        return index >= 0 ? AspectRatioLabels[index] : aspectRatio;
    }

    /// <summary>
    /// Gets the crop mode that follows the specified one.
    /// </summary>
    /// <param name="cropMode">The current crop mode: <c>Fit</c>, <c>Fill</c> or a ratio such as <c>16:9</c>.</param>
    /// <returns>The next crop mode. A mode that is not in the list moves to <c>Fit</c>.</returns>
    public static string GetNextCropMode(string cropMode)
    {
        return GetNext(CropModes, cropMode);
    }

    /// <summary>
    /// Gets the deinterlace mode that follows the specified one.
    /// </summary>
    /// <param name="mode">The current VLC deinterlace mode, such as <c>blend</c>.</param>
    /// <returns>The next deinterlace mode.</returns>
    public static string GetNextDeinterlaceMode(string mode)
    {
        return GetNext(DeinterlaceModes, mode);
    }

    /// <summary>
    /// Gets the readable name of a deinterlace mode.
    /// </summary>
    /// <param name="mode">The VLC deinterlace mode, such as <c>yadif2x</c>.</param>
    /// <returns>The name, such as <c>Yadif (2x)</c>.</returns>
    public static string GetDeinterlaceModeLabel(string mode)
    {
        int index = Array.IndexOf(DeinterlaceModes, mode);
        return index >= 0 ? DeinterlaceModeLabels[index] : mode;
    }

    /// <summary>
    /// Gets the deinterlace mode to pass to LibVLC, which does not accept the automatic mode.
    /// </summary>
    /// <param name="mode">The VLC deinterlace mode.</param>
    /// <returns>The mode, with the automatic mode replaced by <c>x</c>, the filter VLC picks for it.</returns>
    public static string GetLibVlcDeinterlaceMode(string mode)
    {
        return mode == AutomaticDeinterlaceMode ? "x" : mode;
    }

    /// <summary>
    /// Gets the subtitle track to select after pressing the subtitle track key.
    /// </summary>
    /// <remarks>Like VLC, the cycle passes through "off" between the last and the first track.</remarks>
    /// <param name="selectedIndex">The index of the selected track, or -1 when subtitles are off.</param>
    /// <param name="count">The number of subtitle tracks.</param>
    /// <param name="reverse"><see langword="true"/> to cycle backward.</param>
    /// <returns>The index of the track to select, or -1 to turn subtitles off.</returns>
    public static int GetNextSubtitleTrackIndex(int selectedIndex, int count, bool reverse)
    {
        if (count <= 0 || selectedIndex < -1 || selectedIndex >= count)
        {
            return -1;
        }

        if (reverse)
        {
            return selectedIndex == -1 ? count - 1 : selectedIndex - 1;
        }

        return selectedIndex == count - 1 ? -1 : selectedIndex + 1;
    }

    /// <summary>
    /// Gets the audio track to select after pressing the audio track key.
    /// </summary>
    /// <remarks>Unlike subtitles, the audio cycle skips "off".</remarks>
    /// <param name="selectedIndex">The index of the selected track, or -1 when audio is off.</param>
    /// <param name="count">The number of audio tracks.</param>
    /// <returns>The index of the track to select, or -1 if there are no audio tracks.</returns>
    public static int GetNextAudioTrackIndex(int selectedIndex, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        return selectedIndex < 0 || selectedIndex >= count - 1 ? 0 : selectedIndex + 1;
    }

    /// <summary>
    /// Gets the repeat mode after pressing the loop key.
    /// </summary>
    /// <param name="mode">The current repeat mode.</param>
    /// <returns>The next mode in the order off, all, one.</returns>
    public static MediaPlaybackAutoRepeatMode GetNextRepeatMode(MediaPlaybackAutoRepeatMode mode)
    {
        return mode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        };
    }

    /// <summary>
    /// Gets the chapter to go to after pressing the next chapter key.
    /// </summary>
    /// <param name="startTimes">The start times of the chapters, in ascending order.</param>
    /// <param name="position">The playback position.</param>
    /// <returns>The index of the next chapter, or -1 if the position is in the last chapter.</returns>
    public static int GetNextChapterIndex(IReadOnlyList<TimeSpan> startTimes, TimeSpan position)
    {
        // A seek can land slightly before the start of a chapter. The tolerance keeps
        // a quick second key press from jumping to the chapter that was just started.
        TimeSpan current = position + TimeSpan.FromMilliseconds(500);
        for (int i = 0; i < startTimes.Count; i++)
        {
            if (startTimes[i] > current)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the chapter to go to after pressing the previous chapter key.
    /// </summary>
    /// <remarks>
    /// Like VLC, the key restarts the current chapter, unless playback is within 3 seconds of its start.
    /// </remarks>
    /// <param name="startTimes">The start times of the chapters, in ascending order.</param>
    /// <param name="position">The playback position.</param>
    /// <returns>The index of the chapter to go to, or -1 if there is none.</returns>
    public static int GetPreviousChapterIndex(IReadOnlyList<TimeSpan> startTimes, TimeSpan position)
    {
        TimeSpan current = position + TimeSpan.FromMilliseconds(500);
        int index = -1;
        for (int i = 0; i < startTimes.Count && startTimes[i] <= current; i++)
        {
            index = i;
        }

        if (index >= 0 && position - startTimes[index] < TimeSpan.FromSeconds(3))
        {
            index--;
        }

        return index;
    }

    /// <summary>
    /// Calculates the subtitle delay correction from the subtitle synchronization bookmarks.
    /// </summary>
    /// <remarks>
    /// VLC measures the time between the two key presses. The elapsed time is scaled by the playback rate,
    /// because the subtitle delay applies to media time.
    /// </remarks>
    /// <param name="audioBookmark">The time at which the line was heard.</param>
    /// <param name="subtitleBookmark">The time at which the subtitle appeared.</param>
    /// <param name="rate">The playback rate.</param>
    /// <returns>The delay to add to the subtitle delay, in milliseconds. A negative value shows subtitles earlier.</returns>
    public static double GetSubtitleSyncCorrection(TimeSpan audioBookmark, TimeSpan subtitleBookmark, double rate)
    {
        return Math.Round((audioBookmark - subtitleBookmark).TotalMilliseconds * rate);
    }

    /// <summary>
    /// Formats a playback position for the go to time dialog.
    /// </summary>
    /// <param name="time">The playback position.</param>
    /// <returns>The position in <c>h:mm:ss</c> form.</returns>
    public static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds);
    }

    /// <summary>
    /// Parses a time typed in the go to time dialog.
    /// </summary>
    /// <param name="text">The time as <c>h:mm:ss</c>, <c>m:ss</c> or seconds. The seconds may have a decimal fraction.</param>
    /// <param name="time">When this method returns, contains the parsed time, or <see cref="TimeSpan.Zero"/>.</param>
    /// <returns><see langword="true"/> if the text is a valid time; otherwise, <see langword="false"/>.</returns>
    /// <example>
    /// <code>
    /// VlcPlaybackHelper.TryParseTime("1:02:03", out TimeSpan time); // 1 hour, 2 minutes and 3 seconds
    /// VlcPlaybackHelper.TryParseTime("90", out time);               // 1 minute and 30 seconds
    /// </code>
    /// </example>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double totalSeconds = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            // Hours and minutes are whole numbers. Only the seconds may have a decimal fraction.
            NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
            if (i == parts.Length - 1)
            {
                styles |= NumberStyles.AllowDecimalPoint;
            }

            if (!double.TryParse(parts[i], styles, CultureInfo.InvariantCulture, out double value))
            {
                return false;
            }

            // Only the leading part may exceed 59, so that "90" means 90 seconds but "1:90" is rejected.
            if (i > 0 && value >= 60)
            {
                return false;
            }

            totalSeconds = totalSeconds * 60 + value;
        }

        // Longer times cannot be represented, and no media is that long.
        if (totalSeconds >= TimeSpan.MaxValue.TotalSeconds / 2)
        {
            return false;
        }

        time = TimeSpan.FromSeconds(totalSeconds);
        return true;
    }

    private static string GetNext(string[] values, string current)
    {
        // Like VLC, a value that is not in the list starts the cycle over.
        int index = Array.IndexOf(values, current);
        return index < 0 || index == values.Length - 1 ? values[0] : values[index + 1];
    }
}
