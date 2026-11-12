using Screenbox.Core.Helpers;
using Windows.Media;

namespace Screenbox.Core.Tests.Helpers;

public class VlcPlaybackHelperTests
{
    [Test]
    [Arguments(1d, 1.5d)]
    [Arguments(1.5d, 2d)]
    [Arguments(2d, 3d)]
    [Arguments(4d, 8d)]
    [Arguments(1.4999d, 2d)]
    [Arguments(0.5d, 2d / 3)]
    public async Task GetFasterRate_ShouldStepThroughVlcPresets(double rate, double expected)
    {
        await Assert.That(VlcPlaybackHelper.GetFasterRate(rate)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetFasterRate_ShouldStopAtMaximumRate()
    {
        await Assert.That(VlcPlaybackHelper.GetFasterRate(16)).IsEqualTo(VlcPlaybackHelper.MaximumRate);
        await Assert.That(VlcPlaybackHelper.GetFasterRate(VlcPlaybackHelper.MaximumRate)).IsEqualTo(VlcPlaybackHelper.MaximumRate);
    }

    [Test]
    [Arguments(1d, 2d / 3)]
    [Arguments(2d / 3, 0.5d)]
    [Arguments(0.25d, 0.125d)]
    [Arguments(3d, 2d)]
    public async Task GetSlowerRate_ShouldStepThroughVlcPresets(double rate, double expected)
    {
        await Assert.That(VlcPlaybackHelper.GetSlowerRate(rate)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetSlowerRate_ShouldStopAtMinimumRate()
    {
        await Assert.That(VlcPlaybackHelper.GetSlowerRate(VlcPlaybackHelper.MinimumRate)).IsEqualTo(VlcPlaybackHelper.MinimumRate);
    }

    [Test]
    [Arguments(1d, 1, 1.1d)]
    [Arguments(1.1d, 1, 1.2d)]
    [Arguments(1d, -1, 0.9d)]
    [Arguments(2d / 3, 1, 0.7d)]
    // Like VLC, a rate between tenths first drops to the lower tenth, then steps from there.
    [Arguments(1.25d, 1, 1.3d)]
    [Arguments(1.25d, -1, 1.1d)]
    public async Task GetFineRate_ShouldMoveToTheNextTenth(double rate, int direction, double expected)
    {
        await Assert.That(VlcPlaybackHelper.GetFineRate(rate, direction)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetFineRate_ShouldStayWithinTheSupportedRange()
    {
        await Assert.That(VlcPlaybackHelper.GetFineRate(0.1, -1)).IsEqualTo(VlcPlaybackHelper.MinimumRate);
        await Assert.That(VlcPlaybackHelper.GetFineRate(VlcPlaybackHelper.MaximumRate, 1)).IsEqualTo(VlcPlaybackHelper.MaximumRate);
    }

    [Test]
    [Arguments(1d, true, 2d)]
    [Arguments(2d, true, 0.25d)]
    [Arguments(0.25d, false, 2d)]
    [Arguments(0.5d, false, 0.25d)]
    [Arguments(1.3d, true, 0.25d)]
    [Arguments(1.3d, false, 0.25d)]
    public async Task GetNextZoomMode_ShouldCycleLikeVlc(double zoom, bool forward, double expected)
    {
        await Assert.That(VlcPlaybackHelper.GetNextZoomMode(zoom, forward)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetScaledZoom_ShouldStepByATenthWithinLimits()
    {
        await Assert.That(VlcPlaybackHelper.GetScaledUpZoom(1)).IsEqualTo(1.1);
        await Assert.That(VlcPlaybackHelper.GetScaledUpZoom(10)).IsEqualTo(10d);
        await Assert.That(VlcPlaybackHelper.GetScaledDownZoom(0.4)).IsEqualTo(0.3);
        await Assert.That(VlcPlaybackHelper.GetScaledDownZoom(0.3)).IsEqualTo(0.3);
    }

    [Test]
    public async Task GetNextAspectRatio_ShouldCycleThroughVlcAspectRatios()
    {
        await Assert.That(VlcPlaybackHelper.GetNextAspectRatio(string.Empty)).IsEqualTo("16:9");
        await Assert.That(VlcPlaybackHelper.GetNextAspectRatio("16:10")).IsEqualTo("221:100");
        await Assert.That(VlcPlaybackHelper.GetNextAspectRatio("5:4")).IsEqualTo(string.Empty);
        await Assert.That(VlcPlaybackHelper.GetNextAspectRatio("2:1")).IsEqualTo(string.Empty);
        await Assert.That(VlcPlaybackHelper.GetAspectRatioLabel("235:100")).IsEqualTo("2.35:1");
        await Assert.That(VlcPlaybackHelper.GetAspectRatioLabel(string.Empty)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetNextCropMode_ShouldStartOverForUnknownModes()
    {
        await Assert.That(VlcPlaybackHelper.GetNextCropMode("Fit")).IsEqualTo("Fill");
        await Assert.That(VlcPlaybackHelper.GetNextCropMode("Fill")).IsEqualTo("16:10");
        await Assert.That(VlcPlaybackHelper.GetNextCropMode("1:1")).IsEqualTo("Fit");
        await Assert.That(VlcPlaybackHelper.GetNextCropMode("21:9")).IsEqualTo("Fit");
    }

    [Test]
    public async Task DeinterlaceModes_ShouldCycleAndAvoidTheAutomaticModeInLibVlc()
    {
        await Assert.That(VlcPlaybackHelper.GetNextDeinterlaceMode(VlcPlaybackHelper.AutomaticDeinterlaceMode)).IsEqualTo("discard");
        await Assert.That(VlcPlaybackHelper.GetNextDeinterlaceMode("ivtc")).IsEqualTo(VlcPlaybackHelper.AutomaticDeinterlaceMode);
        await Assert.That(VlcPlaybackHelper.GetDeinterlaceModeLabel("yadif2x")).IsEqualTo("Yadif (2x)");
        await Assert.That(VlcPlaybackHelper.GetLibVlcDeinterlaceMode(VlcPlaybackHelper.AutomaticDeinterlaceMode)).IsEqualTo("x");
        await Assert.That(VlcPlaybackHelper.GetLibVlcDeinterlaceMode("blend")).IsEqualTo("blend");
    }

    [Test]
    [Arguments(-1, 2, false, 0)]
    [Arguments(0, 2, false, 1)]
    [Arguments(1, 2, false, -1)]
    [Arguments(-1, 2, true, 1)]
    [Arguments(0, 2, true, -1)]
    [Arguments(-1, 0, false, -1)]
    [Arguments(5, 2, false, -1)]
    public async Task GetNextSubtitleTrackIndex_ShouldPassThroughOff(int selectedIndex, int count, bool reverse, int expected)
    {
        await Assert.That(VlcPlaybackHelper.GetNextSubtitleTrackIndex(selectedIndex, count, reverse)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0, 2, 1)]
    [Arguments(1, 2, 0)]
    [Arguments(-1, 2, 0)]
    [Arguments(0, 1, 0)]
    [Arguments(-1, 0, -1)]
    public async Task GetNextAudioTrackIndex_ShouldSkipOff(int selectedIndex, int count, int expected)
    {
        await Assert.That(VlcPlaybackHelper.GetNextAudioTrackIndex(selectedIndex, count)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetNextRepeatMode_ShouldGoFromOffToAllToOne()
    {
        await Assert.That(VlcPlaybackHelper.GetNextRepeatMode(MediaPlaybackAutoRepeatMode.None)).IsEqualTo(MediaPlaybackAutoRepeatMode.List);
        await Assert.That(VlcPlaybackHelper.GetNextRepeatMode(MediaPlaybackAutoRepeatMode.List)).IsEqualTo(MediaPlaybackAutoRepeatMode.Track);
        await Assert.That(VlcPlaybackHelper.GetNextRepeatMode(MediaPlaybackAutoRepeatMode.Track)).IsEqualTo(MediaPlaybackAutoRepeatMode.None);
    }

    [Test]
    public async Task GetNextChapterIndex_ShouldSkipAChapterThatWasJustStarted()
    {
        TimeSpan[] startTimes = [TimeSpan.Zero, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];

        await Assert.That(VlcPlaybackHelper.GetNextChapterIndex(startTimes, TimeSpan.FromSeconds(10))).IsEqualTo(1);
        await Assert.That(VlcPlaybackHelper.GetNextChapterIndex(startTimes, TimeSpan.FromSeconds(59.8))).IsEqualTo(2);
        await Assert.That(VlcPlaybackHelper.GetNextChapterIndex(startTimes, TimeSpan.FromSeconds(130))).IsEqualTo(-1);
    }

    [Test]
    public async Task GetPreviousChapterIndex_ShouldRestartTheChapterAfterThreeSeconds()
    {
        TimeSpan[] startTimes = [TimeSpan.Zero, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];

        await Assert.That(VlcPlaybackHelper.GetPreviousChapterIndex(startTimes, TimeSpan.FromSeconds(70))).IsEqualTo(1);
        await Assert.That(VlcPlaybackHelper.GetPreviousChapterIndex(startTimes, TimeSpan.FromSeconds(61))).IsEqualTo(0);
        await Assert.That(VlcPlaybackHelper.GetPreviousChapterIndex(startTimes, TimeSpan.FromSeconds(59.8))).IsEqualTo(0);
        await Assert.That(VlcPlaybackHelper.GetPreviousChapterIndex(startTimes, TimeSpan.FromSeconds(1))).IsEqualTo(-1);
    }

    [Test]
    public async Task GetSubtitleSyncCorrection_ShouldShowLateSubtitlesEarlier()
    {
        // The line was heard 2 seconds before its subtitle appeared.
        TimeSpan heard = TimeSpan.FromSeconds(10);
        TimeSpan shown = TimeSpan.FromSeconds(12);

        await Assert.That(VlcPlaybackHelper.GetSubtitleSyncCorrection(heard, shown, 1)).IsEqualTo(-2000d);
        await Assert.That(VlcPlaybackHelper.GetSubtitleSyncCorrection(heard, shown, 2)).IsEqualTo(-4000d);
        await Assert.That(VlcPlaybackHelper.GetSubtitleSyncCorrection(shown, heard, 1)).IsEqualTo(2000d);
    }

    [Test]
    public async Task FormatTime_ShouldUseHoursMinutesAndSeconds()
    {
        await Assert.That(VlcPlaybackHelper.FormatTime(new TimeSpan(1, 2, 3))).IsEqualTo("1:02:03");
        await Assert.That(VlcPlaybackHelper.FormatTime(TimeSpan.Zero)).IsEqualTo("0:00:00");
        await Assert.That(VlcPlaybackHelper.FormatTime(TimeSpan.FromHours(26))).IsEqualTo("26:00:00");
        await Assert.That(VlcPlaybackHelper.FormatTime(TimeSpan.FromSeconds(-5))).IsEqualTo("0:00:00");
    }

    [Test]
    [Arguments("1:02:03", 3723d)]
    [Arguments("02:03", 123d)]
    [Arguments("90", 90d)]
    [Arguments("1:02.5", 62.5d)]
    [Arguments(" 1:2:3 ", 3723d)]
    [Arguments("0", 0d)]
    public async Task TryParseTime_ShouldAcceptTimes(string text, double expectedSeconds)
    {
        bool isValid = VlcPlaybackHelper.TryParseTime(text, out TimeSpan time);

        await Assert.That(isValid).IsTrue();
        await Assert.That(time).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    [Arguments("1:60")]
    [Arguments("1:2:3:4")]
    [Arguments("1.5:00")]
    [Arguments("-5")]
    [Arguments("1:")]
    [Arguments("abc")]
    [Arguments("99999999999999999999")]
    public async Task TryParseTime_ShouldRejectInvalidText(string text)
    {
        await Assert.That(VlcPlaybackHelper.TryParseTime(text, out _)).IsFalse();
    }
}
