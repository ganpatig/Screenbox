using System;

namespace Screenbox.Core.Models;

/// <summary>
/// Represents a decoded video frame shown in the seek bar preview.
/// </summary>
/// <param name="Position">The playback position that the frame was decoded for.</param>
/// <param name="Width">The width of the frame, in pixels.</param>
/// <param name="Height">The height of the frame, in pixels.</param>
/// <param name="Pixels">
/// The opaque pixels of the frame in BGRA8 format, stored row by row with a stride of
/// <paramref name="Width"/> multiplied by four.
/// </param>
public sealed record SeekPreviewFrame(TimeSpan Position, int Width, int Height, byte[] Pixels);
