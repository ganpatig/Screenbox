using System;
using System.Globalization;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Provides the calculations behind seek bar preview frame extraction.
/// </summary>
public static class SeekPreviewHelper
{
    /// <summary>
    /// The aspect ratio assumed when neither the media nor the decoder reports one.
    /// </summary>
    public const double DefaultAspectRatio = 16d / 9d;

    /// <summary>
    /// Determines whether previews can be decoded for the specified media resource locator.
    /// </summary>
    /// <remarks>
    /// Only local files and files opened through the WinRT storage APIs are supported. Decoding previews
    /// for network streams would open a second connection and download data for every hovered position.
    /// </remarks>
    /// <param name="mrl">The media resource locator of the playing media.</param>
    /// <returns><see langword="true"/> if previews can be decoded; otherwise, <see langword="false"/>.</returns>
    public static bool IsSupportedMrl(string? mrl)
    {
        return mrl is not null
            && (mrl.StartsWith("winrt://", StringComparison.OrdinalIgnoreCase)
                || mrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calculates the display aspect ratio of a video track from its dimensions and sample aspect ratio.
    /// </summary>
    /// <param name="width">The visible width of the video, in pixels.</param>
    /// <param name="height">The visible height of the video, in pixels.</param>
    /// <param name="sarNum">The numerator of the sample (pixel) aspect ratio, or 0 if unknown.</param>
    /// <param name="sarDen">The denominator of the sample (pixel) aspect ratio, or 0 if unknown.</param>
    /// <returns>The display aspect ratio, or 0 if the video dimensions are unknown.</returns>
    /// <example>
    /// <code>
    /// // A 720x480 DVD frame with 32:27 pixels is displayed at 16:9.
    /// double aspectRatio = SeekPreviewHelper.GetDisplayAspectRatio(720, 480, 32, 27); // 1.777...
    /// </code>
    /// </example>
    public static double GetDisplayAspectRatio(double width, double height, double sarNum, double sarDen)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        // An unknown sample aspect ratio means square pixels.
        double sampleAspectRatio = sarNum > 0 && sarDen > 0 ? sarNum / sarDen : 1;
        return width * sampleAspectRatio / height;
    }

    /// <summary>
    /// Resolves the aspect ratio that decoded preview frames should be rendered with.
    /// </summary>
    /// <remarks>
    /// Track information reports how a video is stored, while the decoder reports its dimensions after
    /// rotation metadata is applied. When the two disagree on orientation, the video is rotated by 90 or
    /// 270 degrees and the stored display aspect ratio must be inverted.
    /// </remarks>
    /// <param name="displayAspectRatio">The display aspect ratio from track information, or 0 if unknown.</param>
    /// <param name="storedWidth">The stored width of the video, or 0 if unknown.</param>
    /// <param name="storedHeight">The stored height of the video, or 0 if unknown.</param>
    /// <param name="decodedWidth">The frame width reported by the decoder.</param>
    /// <param name="decodedHeight">The frame height reported by the decoder.</param>
    /// <returns>The aspect ratio of the frames to render.</returns>
    public static double GetOutputAspectRatio(double displayAspectRatio, double storedWidth, double storedHeight,
        double decodedWidth, double decodedHeight)
    {
        bool hasDecodedSize = decodedWidth > 0 && decodedHeight > 0;
        if (displayAspectRatio <= 0)
        {
            return hasDecodedSize ? decodedWidth / decodedHeight : DefaultAspectRatio;
        }

        // Rotation cannot be detected without both sizes, and a square frame looks the same either way.
        if (!hasDecodedSize || storedWidth <= 0 || storedHeight <= 0
            || storedWidth == storedHeight || decodedWidth == decodedHeight)
        {
            return displayAspectRatio;
        }

        bool isRotated = (storedWidth > storedHeight) != (decodedWidth > decodedHeight);
        return isRotated ? 1 / displayAspectRatio : displayAspectRatio;
    }

    /// <summary>
    /// Calculates the largest frame size with the specified aspect ratio that fits within the given bounds.
    /// </summary>
    /// <remarks>
    /// Dimensions are rounded to even numbers, which scalers working on subsampled chroma planes handle exactly.
    /// </remarks>
    /// <param name="aspectRatio">The aspect ratio of the frame. Invalid values fall back to <see cref="DefaultAspectRatio"/>.</param>
    /// <param name="maxWidth">The maximum width, in pixels. Must be at least 2.</param>
    /// <param name="maxHeight">The maximum height, in pixels. Must be at least 2.</param>
    /// <returns>The frame width and height, in pixels.</returns>
    public static (uint Width, uint Height) GetFrameSize(double aspectRatio, uint maxWidth, uint maxHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 2u);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 2u);
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
        {
            aspectRatio = DefaultAspectRatio;
        }

        double width = maxWidth;
        double height = width / aspectRatio;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * aspectRatio;
        }

        return (RoundToEven(width, maxWidth), RoundToEven(height, maxHeight));
    }

    /// <summary>
    /// Rounds a value up to the nearest multiple of a power-of-two alignment.
    /// </summary>
    /// <param name="value">The value to align.</param>
    /// <param name="alignment">The alignment. Must be a power of two.</param>
    /// <returns>The smallest multiple of <paramref name="alignment"/> that is greater than or equal to <paramref name="value"/>.</returns>
    public static uint AlignUp(uint value, uint alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be a power of two.");
        }

        return (value + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>
    /// Creates the LibVLC media option that starts playback at the specified position.
    /// </summary>
    /// <remarks>
    /// LibVLC parses option values with a period as the decimal separator regardless of the current culture.
    /// </remarks>
    /// <param name="position">The position to start at. Negative values are treated as zero.</param>
    /// <returns>The media option.</returns>
    /// <example>
    /// <code>
    /// string option = SeekPreviewHelper.CreateStartTimeOption(TimeSpan.FromMilliseconds(83_450)); // ":start-time=83.45"
    /// </code>
    /// </example>
    public static string CreateStartTimeOption(TimeSpan position)
    {
        double seconds = Math.Max(position.TotalSeconds, 0);
        return ":start-time=" + seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Copies a decoded RV32 frame into a tightly packed, opaque BGRA8 buffer.
    /// </summary>
    /// <remarks>
    /// LibVLC can pad each row beyond <c>width * 4</c> bytes, and it leaves the fourth byte of every RV32
    /// pixel undefined. The alpha channel is forced to fully opaque so that consumers expecting
    /// premultiplied alpha do not render the frame as transparent.
    /// </remarks>
    /// <param name="source">The decoded frame, row by row.</param>
    /// <param name="sourceStride">The number of bytes per row in <paramref name="source"/>.</param>
    /// <param name="destination">The buffer that receives <c>width * height * 4</c> bytes.</param>
    /// <param name="width">The width of the frame, in pixels.</param>
    /// <param name="height">The height of the frame, in pixels.</param>
    public static void CopyBgraFrame(ReadOnlySpan<byte> source, int sourceStride, Span<byte> destination, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int rowLength = width * 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceStride, rowLength);
        if (source.Length < (sourceStride * (height - 1)) + rowLength)
        {
            throw new ArgumentException("The source buffer is too small for the frame size.", nameof(source));
        }

        if (destination.Length < rowLength * height)
        {
            throw new ArgumentException("The destination buffer is too small for the frame size.", nameof(destination));
        }

        for (int y = 0; y < height; y++)
        {
            Span<byte> row = destination.Slice(y * rowLength, rowLength);
            source.Slice(y * sourceStride, rowLength).CopyTo(row);
            for (int alphaIndex = 3; alphaIndex < rowLength; alphaIndex += 4)
            {
                row[alphaIndex] = byte.MaxValue;
            }
        }
    }

    /// <summary>
    /// Rounds a dimension to the nearest even number within the range from 2 to the given bound.
    /// </summary>
    private static uint RoundToEven(double value, uint max)
    {
        uint rounded = (uint)Math.Round(value / 2) * 2;
        return Math.Clamp(rounded, 2u, max & ~1u);
    }
}
