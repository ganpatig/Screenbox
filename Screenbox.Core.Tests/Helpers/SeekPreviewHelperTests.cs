using System.Globalization;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests.Helpers;

public class SeekPreviewHelperTests
{
    [Test]
    public async Task IsSupportedMrl_ShouldAcceptLocalAndStorageFiles()
    {
        await Assert.That(SeekPreviewHelper.IsSupportedMrl("winrt://{0F4D2E96-1C7B-4B0E-9A53-8D2F5C1E7A30}")).IsTrue();
        await Assert.That(SeekPreviewHelper.IsSupportedMrl("WINRT://token")).IsTrue();
        await Assert.That(SeekPreviewHelper.IsSupportedMrl("file:///C:/Videos/movie.mkv")).IsTrue();
    }

    [Test]
    public async Task IsSupportedMrl_ShouldRejectNetworkStreams()
    {
        await Assert.That(SeekPreviewHelper.IsSupportedMrl("https://example.com/video.mp4")).IsFalse();
        await Assert.That(SeekPreviewHelper.IsSupportedMrl("smb://server/share/video.mkv")).IsFalse();
        await Assert.That(SeekPreviewHelper.IsSupportedMrl(string.Empty)).IsFalse();
        await Assert.That(SeekPreviewHelper.IsSupportedMrl(null)).IsFalse();
    }

    [Test]
    public async Task GetDisplayAspectRatio_ShouldApplySampleAspectRatio()
    {
        // NTSC DVD: 720x480 stored with 32:27 pixels is displayed at 16:9.
        double aspectRatio = SeekPreviewHelper.GetDisplayAspectRatio(720, 480, 32, 27);

        await Assert.That(IsClose(aspectRatio, 16d / 9d)).IsTrue();
    }

    [Test]
    public async Task GetDisplayAspectRatio_ShouldTreatUnknownSampleAspectRatioAsSquare()
    {
        double aspectRatio = SeekPreviewHelper.GetDisplayAspectRatio(1920, 1080, 0, 0);

        await Assert.That(IsClose(aspectRatio, 16d / 9d)).IsTrue();
    }

    [Test]
    public async Task GetDisplayAspectRatio_ShouldReturnZeroForUnknownSize()
    {
        await Assert.That(SeekPreviewHelper.GetDisplayAspectRatio(0, 1080, 1, 1)).IsEqualTo(0d);
        await Assert.That(SeekPreviewHelper.GetDisplayAspectRatio(1920, 0, 1, 1)).IsEqualTo(0d);
    }

    [Test]
    public async Task GetOutputAspectRatio_ShouldInvertRatioForRotatedVideo()
    {
        // A phone video stored as 1920x1080 with 90 degree rotation metadata decodes as 1080x1920.
        double aspectRatio = SeekPreviewHelper.GetOutputAspectRatio(16d / 9d, 1920, 1080, 1080, 1920);

        await Assert.That(IsClose(aspectRatio, 9d / 16d)).IsTrue();
    }

    [Test]
    public async Task GetOutputAspectRatio_ShouldKeepRatioWhenOrientationMatches()
    {
        // Decoders may report padded dimensions, such as 1088 rows for 1080p video.
        double aspectRatio = SeekPreviewHelper.GetOutputAspectRatio(16d / 9d, 1920, 1080, 1920, 1088);

        await Assert.That(IsClose(aspectRatio, 16d / 9d)).IsTrue();
    }

    [Test]
    public async Task GetOutputAspectRatio_ShouldKeepSampleAspectRatioOfAnamorphicVideo()
    {
        double aspectRatio = SeekPreviewHelper.GetOutputAspectRatio(16d / 9d, 720, 480, 720, 480);

        await Assert.That(IsClose(aspectRatio, 16d / 9d)).IsTrue();
    }

    [Test]
    public async Task GetOutputAspectRatio_ShouldFallBackWhenRatioIsUnknown()
    {
        double fromDecodedSize = SeekPreviewHelper.GetOutputAspectRatio(0, 0, 0, 640, 480);
        double withoutAnySize = SeekPreviewHelper.GetOutputAspectRatio(0, 0, 0, 0, 0);

        await Assert.That(IsClose(fromDecodedSize, 4d / 3d)).IsTrue();
        await Assert.That(IsClose(withoutAnySize, SeekPreviewHelper.DefaultAspectRatio)).IsTrue();
    }

    [Test]
    public async Task GetFrameSize_ShouldFitLandscapeVideoWithinBounds()
    {
        await Assert.That(SeekPreviewHelper.GetFrameSize(16d / 9d, 320, 180)).IsEqualTo((320u, 180u));
        await Assert.That(SeekPreviewHelper.GetFrameSize(4d / 3d, 320, 180)).IsEqualTo((240u, 180u));
        await Assert.That(SeekPreviewHelper.GetFrameSize(2.39, 320, 180)).IsEqualTo((320u, 134u));
    }

    [Test]
    public async Task GetFrameSize_ShouldFitPortraitVideoWithinBounds()
    {
        await Assert.That(SeekPreviewHelper.GetFrameSize(9d / 16d, 320, 180)).IsEqualTo((102u, 180u));
    }

    [Test]
    public async Task GetFrameSize_ShouldUseDefaultRatioForInvalidInput()
    {
        await Assert.That(SeekPreviewHelper.GetFrameSize(double.NaN, 320, 180)).IsEqualTo((320u, 180u));
        await Assert.That(SeekPreviewHelper.GetFrameSize(0, 320, 180)).IsEqualTo((320u, 180u));
        await Assert.That(SeekPreviewHelper.GetFrameSize(-1, 320, 180)).IsEqualTo((320u, 180u));
        await Assert.That(SeekPreviewHelper.GetFrameSize(double.PositiveInfinity, 320, 180)).IsEqualTo((320u, 180u));
    }

    [Test]
    public async Task GetFrameSize_ShouldProduceEvenDimensionsWithinBounds()
    {
        double[] aspectRatios = [0.01, 0.4, 9d / 16d, 1, 1.33, 1.85, 2.39, 3.6, 100];
        bool allValid = true;
        foreach (double aspectRatio in aspectRatios)
        {
            (uint width, uint height) = SeekPreviewHelper.GetFrameSize(aspectRatio, 320, 180);
            allValid &= width % 2 == 0 && height % 2 == 0;
            allValid &= width is >= 2 and <= 320 && height is >= 2 and <= 180;
        }

        await Assert.That(allValid).IsTrue();
    }

    [Test]
    public async Task AlignUp_ShouldRoundUpToMultipleOfAlignment()
    {
        await Assert.That(SeekPreviewHelper.AlignUp(0, 32)).IsEqualTo(0u);
        await Assert.That(SeekPreviewHelper.AlignUp(1, 32)).IsEqualTo(32u);
        await Assert.That(SeekPreviewHelper.AlignUp(32, 32)).IsEqualTo(32u);
        await Assert.That(SeekPreviewHelper.AlignUp(33, 32)).IsEqualTo(64u);
        await Assert.That(SeekPreviewHelper.AlignUp(1280, 32)).IsEqualTo(1280u);
    }

    [Test]
    public async Task AlignUp_ShouldRejectAlignmentThatIsNotPowerOfTwo()
    {
        Exception? zeroAlignmentException = CaptureException(() => SeekPreviewHelper.AlignUp(5, 0));
        Exception? oddAlignmentException = CaptureException(() => SeekPreviewHelper.AlignUp(5, 3));

        await Assert.That(zeroAlignmentException is ArgumentOutOfRangeException).IsTrue();
        await Assert.That(oddAlignmentException is ArgumentOutOfRangeException).IsTrue();
    }

    [Test]
    public async Task CreateStartTimeOption_ShouldUseInvariantDecimalSeparator()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        string option;
        try
        {
            // German uses a comma as the decimal separator, which LibVLC would not parse.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            option = SeekPreviewHelper.CreateStartTimeOption(TimeSpan.FromMilliseconds(83_450));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        await Assert.That(option).IsEqualTo(":start-time=83.45");
    }

    [Test]
    public async Task CreateStartTimeOption_ShouldKeepMillisecondPrecision()
    {
        string option = SeekPreviewHelper.CreateStartTimeOption(new TimeSpan(0, 1, 52, 7, 123));

        await Assert.That(option).IsEqualTo(":start-time=6727.123");
    }

    [Test]
    public async Task CreateStartTimeOption_ShouldClampNegativePositionToZero()
    {
        string option = SeekPreviewHelper.CreateStartTimeOption(TimeSpan.FromSeconds(-3));

        await Assert.That(option).IsEqualTo(":start-time=0");
    }

    [Test]
    public async Task CopyBgraFrame_ShouldSkipRowPaddingAndForceOpaqueAlpha()
    {
        // Two 2x1 pixel rows with 4 bytes of padding each. Padding bytes are 99 and alpha bytes are undefined.
        byte[] source =
        [
            1, 2, 3, 0, 5, 6, 7, 17, 99, 99, 99, 99,
            9, 10, 11, 0, 13, 14, 15, 42, 99, 99, 99, 99
        ];
        byte[] expected = [1, 2, 3, 255, 5, 6, 7, 255, 9, 10, 11, 255, 13, 14, 15, 255];
        byte[] destination = new byte[expected.Length];

        SeekPreviewHelper.CopyBgraFrame(source, 12, destination, 2, 2);
        bool isMatch = destination.AsSpan().SequenceEqual(expected);

        await Assert.That(isMatch).IsTrue();
    }

    [Test]
    public async Task CopyBgraFrame_ShouldRejectBuffersThatAreTooSmall()
    {
        Exception? smallSourceException = CaptureException(() =>
            SeekPreviewHelper.CopyBgraFrame(new byte[15], 8, new byte[16], 2, 2));
        Exception? smallDestinationException = CaptureException(() =>
            SeekPreviewHelper.CopyBgraFrame(new byte[16], 8, new byte[15], 2, 2));
        Exception? narrowStrideException = CaptureException(() =>
            SeekPreviewHelper.CopyBgraFrame(new byte[16], 7, new byte[16], 2, 2));

        await Assert.That(smallSourceException is ArgumentException).IsTrue();
        await Assert.That(smallDestinationException is ArgumentException).IsTrue();
        await Assert.That(narrowStrideException is ArgumentOutOfRangeException).IsTrue();
    }

    private static bool IsClose(double actual, double expected)
    {
        return Math.Abs(actual - expected) < 1e-9;
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            return e;
        }

        return null;
    }
}
