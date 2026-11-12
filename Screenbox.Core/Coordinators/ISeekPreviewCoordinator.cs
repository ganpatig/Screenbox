using System;
using System.Threading.Tasks;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;

namespace Screenbox.Core.Coordinators;

/// <summary>
/// Represents a stateful coordinator that owns a headless player and extracts
/// seek bar preview thumbnails for the media that is currently playing.
/// </summary>
public interface ISeekPreviewCoordinator : IDisposable
{
    /// <summary>
    /// Gets the video frame at the specified position of the media that is currently playing.
    /// </summary>
    /// <remarks>
    /// Frames are decoded one at a time and only the most recent request is queued. A request that is superseded,
    /// whether it is queued or already decoding, completes with <see langword="null"/>. The returned task never faults.
    /// </remarks>
    /// <param name="position">The playback position to get the frame for.</param>
    /// <param name="quality">How closely the frame has to match the position.</param>
    /// <returns>
    /// A task that completes with the decoded frame, or with <see langword="null"/> if the request was superseded,
    /// the current media has no previewable video, or the frame could not be decoded.
    /// </returns>
    Task<SeekPreviewFrame?> GetFrameAsync(TimeSpan position, SeekPreviewQuality quality);

    /// <summary>
    /// Discards the queued request, if any, without interrupting the frame that is currently being decoded.
    /// </summary>
    void CancelPendingRequest();
}
