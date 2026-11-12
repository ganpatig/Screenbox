using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Screenbox.Core.Coordinators;

/// <summary>
/// Extracts seek bar preview thumbnails with a dedicated, headless LibVLC player.
/// </summary>
/// <remarks>
/// <para>
/// Previews are decoded by a separate <see cref="LibVLC"/> instance. The main instance is created with the
/// swap chain of the video view, so decoding previews on it would compete with playback for its Direct3D device.
/// </para>
/// <para>
/// Each capture restarts the preview input with a <c>:start-time</c> option instead of seeking a running input.
/// While paused, LibVLC 3 redisplays the last picture about every 80 ms, so after a plain seek a stale picture
/// can reach the display callback before the new one and be mistaken for it. Replacing the media stops the
/// previous input and flushes the recycled video output before the capture is armed, so the first picture
/// delivered afterwards always belongs to the current request.
/// </para>
/// <para>
/// A <see cref="SeekPreviewQuality.Exact"/> capture disables fast seeking, which makes LibVLC decode forward from
/// the preceding keyframe and drop every picture before the target. The delivered picture is therefore the frame
/// at the requested time rather than the nearest keyframe, at the cost of decoding a whole group of pictures.
/// A <see cref="SeekPreviewQuality.Fast"/> capture seeks to the preceding keyframe and shows it as it is, which
/// takes about as long as opening the file and keeps previews flowing while the user drags the seek bar.
/// </para>
/// </remarks>
public sealed partial class SeekPreviewCoordinator : ISeekPreviewCoordinator
{
    private const uint MaxFrameWidth = 320;
    private const uint MaxFrameHeight = 180;
    private const uint FrameBufferAlignment = 32;
    private const int FrameCacheCapacity = 48;

    /// <summary>
    /// The length of the positions that share one <see cref="SeekPreviewQuality.Fast"/> frame. Such a frame shows
    /// the keyframe before the position, so nearby positions would decode the same picture again.
    /// </summary>
    private const long FastFrameBucketMilliseconds = 1000;

    /// <summary>
    /// How often a capture that is waiting for a picture checks whether a newer request has arrived.
    /// </summary>
    private static readonly TimeSpan SupersedeCheckInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// The longest time to wait for a frame. High resolution video with long keyframe intervals can take
    /// a few seconds to decode up to the target frame.
    /// </summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the preview input stays open after the last request, so that scrubbing does not recreate
    /// the decoder and video output for every hovered position.
    /// </summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How far before the requested position to retry when a request lands after the last decodable frame.
    /// </summary>
    private static readonly TimeSpan EndOfStreamStepBack = TimeSpan.FromSeconds(1);

    private static readonly string[] LibVlcOptions =
    [
        "--no-audio",
        "--no-spu",
        "--no-osd",
        "--no-stats",
        "--no-snapshot-preview",
        "--no-video-title-show",
        "--no-sub-autodetect-file",
        "--verbose=0"
    ];

    /// <summary>
    /// Options applied to a capture of the frame at the requested position. They are media options rather than
    /// instance options because LibVLC ignores unknown media options, whereas an unknown instance option makes
    /// the instance fail to initialize.
    /// </summary>
    private static readonly string[] ExactCaptureMediaOptions =
    [
        // Decode forward from the preceding keyframe to the exact target instead of snapping to the keyframe.
        ":no-input-fast-seek",
        // Never skip or drop frames on the way to the target, or the delivered frame could land after it.
        ":no-avcodec-hurry-up",
        ":no-skip-frames",
        ":no-drop-late-frames"
    ];

    /// <summary>
    /// Options applied to a capture of the keyframe before the requested position. Fast seeking stops the demuxer
    /// at that keyframe, so the decoder only produces the one picture that is shown.
    /// </summary>
    private static readonly string[] FastCaptureMediaOptions =
    [
        ":input-fast-seek"
    ];

    private static readonly byte[] Rv32Chroma = "RV32"u8.ToArray();

    private readonly PlayerContext _playerContext;
    private readonly ILogger<SeekPreviewCoordinator> _logger;

    // Inspection of the playing media, guarded by _inspectionLock.
    private readonly Lock _inspectionLock = new();
    private PlaybackItem? _inspectedItem;
    private int _inspectedTrackIndex;
    private PreviewSource? _inspectedSource;

    // Request queue and frame caches, guarded by _requestLock.
    private readonly Lock _requestLock = new();
    private readonly SemaphoreSlim _requestSignal = new(0, 1);
    private readonly LruCache<long, SeekPreviewFrame> _frameCache = new(FrameCacheCapacity);
    private readonly LruCache<long, SeekPreviewFrame> _fastFrameCache = new(FrameCacheCapacity);
    private PreviewSource? _cacheSource;
    private PreviewRequest? _pendingRequest;
    private PreviewRequest? _activeRequest;
    private Task? _workerTask;
    private bool _isUnavailable;
    private bool _isDisposed;

    // Preview player, owned by the worker thread.
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _media;
    private PreviewSource? _playerSource;
    private bool _isPlayerOpen;
    private int _captureCounter;

    // Frame buffer and capture state, shared with the LibVLC video output thread and guarded by _frameLock.
    // The worker must never call into LibVLC while holding this lock: stopping an input waits for the video
    // output thread, which may itself be waiting for the lock inside a callback.
    private readonly Lock _frameLock = new();
    private readonly SemaphoreSlim _frameSignal = new(0, 1);
    private readonly MediaPlayer.LibVLCVideoFormatCb _videoFormatCallback;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _videoCleanupCallback;
    private readonly MediaPlayer.LibVLCVideoLockCb _videoLockCallback;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _videoDisplayCallback;
    private PreviewSource? _outputSource;
    private IntPtr _frameBuffer;
    private uint _frameBufferWidth;
    private uint _frameBufferHeight;
    private uint _frameBufferPitch;
    private int _armedCaptureId;
    private byte[]? _capturedPixels;
    private int _capturedWidth;
    private int _capturedHeight;
    private bool _hasCaptureEnded;

    public SeekPreviewCoordinator(PlayerContext playerContext, ILogger<SeekPreviewCoordinator> logger)
    {
        _playerContext = playerContext;
        _logger = logger;

        // The native player calls these delegates, so they are kept alive for the lifetime of the coordinator.
        _videoFormatCallback = OnVideoFormat;
        _videoCleanupCallback = OnVideoCleanup;
        _videoLockCallback = OnVideoLock;
        _videoDisplayCallback = OnVideoDisplay;
    }

    /// <inheritdoc/>
    public Task<SeekPreviewFrame?> GetFrameAsync(TimeSpan position, SeekPreviewQuality quality)
    {
        PreviewSource? source = GetCurrentSource();
        if (source is null || position < TimeSpan.Zero)
        {
            return Task.FromResult<SeekPreviewFrame?>(null);
        }

        lock (_requestLock)
        {
            if (_isDisposed || _isUnavailable)
            {
                return Task.FromResult<SeekPreviewFrame?>(null);
            }

            if (source != _cacheSource)
            {
                // Frames of a different media or video track must never be served from the cache.
                _cacheSource = source;
                _frameCache.Clear();
                _fastFrameCache.Clear();
            }

            if (TryGetCachedFrame(position, quality, out SeekPreviewFrame? cachedFrame))
            {
                return Task.FromResult(cachedFrame);
            }

            // Pointer jitter often repeats the position that is already queued or being decoded.
            if (_activeRequest is { } activeRequest && activeRequest.Matches(source, position, quality))
            {
                return activeRequest.Completion.Task;
            }

            if (_pendingRequest is { } pendingRequest && pendingRequest.Matches(source, position, quality))
            {
                return pendingRequest.Completion.Task;
            }

            // Only the latest hovered position matters, so a request that has not started yet is superseded.
            _pendingRequest?.Completion.TrySetResult(null);
            PreviewRequest request = new(source, position, quality);
            _pendingRequest = request;
            _workerTask ??= Task.Factory.StartNew(RunWorker, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            SignalIfNotSignaled(_requestSignal);
            return request.Completion.Task;
        }
    }

    /// <inheritdoc/>
    public void CancelPendingRequest()
    {
        lock (_requestLock)
        {
            _pendingRequest?.Completion.TrySetResult(null);
            _pendingRequest = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_requestLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _pendingRequest?.Completion.TrySetResult(null);
            _pendingRequest = null;

            // The worker releases the player on its own thread because LibVLC teardown blocks. The semaphores are
            // not disposed, since the worker may still be waiting on them.
            if (_workerTask is not null)
            {
                SignalIfNotSignaled(_requestSignal);
            }
        }
    }

    /// <summary>
    /// Looks for a decoded frame that can answer a request. Callers must hold <see cref="_requestLock"/>.
    /// </summary>
    /// <param name="position">The requested position.</param>
    /// <param name="quality">The requested quality.</param>
    /// <param name="frame">When this method returns, contains the cached frame, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a cached frame can be served; otherwise, <see langword="false"/>.</returns>
    private bool TryGetCachedFrame(TimeSpan position, SeekPreviewQuality quality, out SeekPreviewFrame? frame)
    {
        // An exact frame also answers a fast request, because it is the better picture of the same position.
        return _frameCache.TryGetValue((long)position.TotalMilliseconds, out frame) ||
               (quality is SeekPreviewQuality.Fast &&
                _fastFrameCache.TryGetValue(GetFastFrameBucket(position), out frame));
    }

    /// <summary>
    /// Gets the key of the positions that share one fast frame.
    /// </summary>
    private static long GetFastFrameBucket(TimeSpan position)
    {
        return (long)position.TotalMilliseconds / FastFrameBucketMilliseconds;
    }

    /// <summary>
    /// Resolves the media and video track that previews should be decoded from.
    /// </summary>
    /// <returns>The preview source, or <see langword="null"/> if the playing media cannot be previewed.</returns>
    private PreviewSource? GetCurrentSource()
    {
        // Audio-only media never has video tracks, and the tracks of video media appear once it has been parsed.
        if (_playerContext.MediaPlayer is not { CanSeek: true, PlaybackItem: { } item } || item.VideoTracks.Count == 0)
        {
            return null;
        }

        lock (_inspectionLock)
        {
            int trackIndex = item.VideoTracks.SelectedIndex;
            if (!ReferenceEquals(item, _inspectedItem) || trackIndex != _inspectedTrackIndex)
            {
                _inspectedSource = CreateSource(item, trackIndex);
                _inspectedItem = item;
                _inspectedTrackIndex = trackIndex;
            }

            return _inspectedSource;
        }
    }

    /// <summary>
    /// Creates the preview source for the selected video track of a playback item.
    /// </summary>
    /// <param name="item">The playback item to inspect.</param>
    /// <param name="trackIndex">The index of the selected video track, or a negative value to use the first one.</param>
    /// <returns>The preview source, or <see langword="null"/> if the item cannot be previewed.</returns>
    private PreviewSource? CreateSource(PlaybackItem item, int trackIndex)
    {
        try
        {
            string mrl = item.Media.Mrl;
            if (!SeekPreviewHelper.IsSupportedMrl(mrl))
            {
                return null;
            }

            int trackId = trackIndex >= 0 && trackIndex < item.VideoTracks.Count
                ? item.VideoTracks[trackIndex].VlcTrackId
                : -1;

            foreach (LibVLCSharp.Shared.MediaTrack track in item.Media.Tracks)
            {
                if (track.TrackType != TrackType.Video || (trackId >= 0 && track.Id != trackId))
                {
                    continue;
                }

                double aspectRatio = SeekPreviewHelper.GetDisplayAspectRatio(track.Data.Video.Width,
                    track.Data.Video.Height, track.Data.Video.SarNum, track.Data.Video.SarDen);
                return new PreviewSource(mrl, track.Id, aspectRatio, track.Data.Video.Width, track.Data.Video.Height);
            }
        }
        catch (Exception e)
        {
            // Inspection only reads track metadata; if it fails, the media is simply not previewed.
            _logger.LogWarning(e, "Failed to inspect the playing media for seek previews.");
        }

        return null;
    }

    /// <summary>
    /// Decodes queued requests one at a time on a dedicated thread, because LibVLC blocks while inputs start and stop.
    /// </summary>
    private void RunWorker()
    {
        while (true)
        {
            // Keep the input open between requests while the user scrubs, and release it once they stop.
            bool isSignaled = _requestSignal.Wait(_isPlayerOpen ? IdleTimeout : Timeout.InfiniteTimeSpan);

            PreviewRequest? request;
            lock (_requestLock)
            {
                if (_isDisposed)
                {
                    break;
                }

                request = _pendingRequest;
                _pendingRequest = null;
                _activeRequest = request;
            }

            if (request is null)
            {
                if (!isSignaled)
                {
                    ClosePlayer();
                }

                continue;
            }

            SeekPreviewFrame? frame = null;
            try
            {
                frame = ProcessRequest(request);
            }
            catch (Exception e)
            {
                // A failed capture must not stop the worker. The player is rebuilt on the next request.
                _logger.LogWarning(e, "Failed to decode a seek preview frame.");
                DisposePlayer();
            }
            finally
            {
                lock (_requestLock)
                {
                    _activeRequest = null;
                    if (frame is not null && request.Source == _cacheSource)
                    {
                        if (request.Quality is SeekPreviewQuality.Exact)
                        {
                            _frameCache.Set((long)request.Position.TotalMilliseconds, frame);
                        }
                        else
                        {
                            _fastFrameCache.Set(GetFastFrameBucket(request.Position), frame);
                        }
                    }
                }

                request.Completion.TrySetResult(frame);
            }
        }

        DisposePlayer();
    }

    /// <summary>
    /// Decodes the frame for a request.
    /// </summary>
    /// <param name="request">The request to decode.</param>
    /// <returns>The decoded frame, or <see langword="null"/> if no frame could be decoded.</returns>
    private SeekPreviewFrame? ProcessRequest(PreviewRequest request)
    {
        if (!TryEnsurePlayer())
        {
            return null;
        }

        UseSource(request.Source);
        SeekPreviewFrame? frame = CaptureFrame(request, request.Position, out bool hasReachedEnd);
        if (frame is null && hasReachedEnd && request.Position > TimeSpan.Zero)
        {
            // A position after the last decodable frame ends the input without delivering a picture,
            // so show the closest frame before it instead.
            TimeSpan fallbackPosition = request.Position > EndOfStreamStepBack
                ? request.Position - EndOfStreamStepBack
                : TimeSpan.Zero;
            frame = CaptureFrame(request, fallbackPosition, out _);
        }

        return frame;
    }

    /// <summary>
    /// Creates the preview LibVLC instance and player on first use.
    /// </summary>
    /// <returns><see langword="true"/> if the player is available; otherwise, <see langword="false"/>.</returns>
    private bool TryEnsurePlayer()
    {
        if (_player is not null)
        {
            return true;
        }

        try
        {
            _libVlc = new LibVLC(LibVlcOptions);
        }
        catch (VLCException e)
        {
            // Without a LibVLC instance previews can never work, so stop accepting requests.
            _logger.LogError(e, "Failed to initialize LibVLC for seek previews.");
            lock (_requestLock)
            {
                _isUnavailable = true;
            }

            return false;
        }

        _player = new MediaPlayer(_libVlc);
        _player.SetVideoFormatCallbacks(_videoFormatCallback, _videoCleanupCallback);
        _player.SetVideoCallbacks(_videoLockCallback, null, _videoDisplayCallback);
        _player.EndReached += OnPlayerEndReached;
        _player.EncounteredError += OnPlayerEncounteredError;
        return true;
    }

    /// <summary>
    /// Switches the preview player to the specified source if it is not already using it.
    /// </summary>
    /// <remarks>
    /// Stopping destroys the video output. A recycled output would keep the frame buffer sized for the previous
    /// source, because the format callback only runs when the video output opens.
    /// </remarks>
    /// <param name="source">The source to decode from.</param>
    private void UseSource(PreviewSource source)
    {
        if (source == _playerSource)
        {
            return;
        }

        _player?.Stop();
        _isPlayerOpen = false;
        _playerSource = source;
        lock (_frameLock)
        {
            _outputSource = source;
        }
    }

    /// <summary>
    /// Restarts the preview input at a position and waits for the first picture that it delivers.
    /// </summary>
    /// <param name="request">The request being decoded, which provides the source, the quality and the position to report.</param>
    /// <param name="startPosition">The position to start decoding at.</param>
    /// <param name="hasReachedEnd">When this method returns, whether the input ended before delivering a picture.</param>
    /// <returns>The decoded frame, or <see langword="null"/> if no picture was delivered in time.</returns>
    private SeekPreviewFrame? CaptureFrame(PreviewRequest request, TimeSpan startPosition, out bool hasReachedEnd)
    {
        PreviewSource source = request.Source;
        MediaPlayer player = _player!;
        string[] qualityOptions = request.Quality is SeekPreviewQuality.Exact
            ? ExactCaptureMediaOptions
            : FastCaptureMediaOptions;
        List<string> options = new(qualityOptions.Length + 2);
        options.AddRange(qualityOptions);
        options.Add(SeekPreviewHelper.CreateStartTimeOption(startPosition));
        if (source.VideoTrackId >= 0)
        {
            options.Add(":video-track-id=" + source.VideoTrackId.ToString(CultureInfo.InvariantCulture));
        }

        Media media = new(_libVlc!, source.Mrl, FromType.FromLocation, options.ToArray());

        // Replacing the media stops the previous input and flushes the recycled video output before this call
        // returns, so no picture displayed after this point can belong to an earlier request.
        player.Media = media;
        _media?.Dispose();
        _media = media;

        // Drain a signal left behind by a capture that completed after its timeout. This must happen before the
        // capture is armed, or it could consume the signal of this capture.
        _frameSignal.Wait(0);

        int captureId = ++_captureCounter;
        lock (_frameLock)
        {
            _armedCaptureId = captureId;
            _capturedPixels = null;
            _hasCaptureEnded = false;
        }

        if (player.Play())
        {
            WaitForFrame();
        }

        byte[]? pixels;
        int width;
        int height;
        lock (_frameLock)
        {
            _armedCaptureId = 0;
            pixels = _capturedPixels;
            width = _capturedWidth;
            height = _capturedHeight;
            hasReachedEnd = _hasCaptureEnded;
            _capturedPixels = null;
        }

        if (pixels is null)
        {
            // Reset an input that failed, ended, or timed out so that the next capture starts cleanly.
            player.Stop();
            _isPlayerOpen = false;
            return null;
        }

        // Pausing keeps the input and video output alive, so the next capture only has to reopen the media.
        player.SetPause(true);
        _isPlayerOpen = true;
        return new SeekPreviewFrame(request.Position, width, height, pixels);
    }

    /// <summary>
    /// Waits for the armed capture to deliver a picture.
    /// </summary>
    /// <remarks>
    /// Decoding up to an exact frame can take seconds. Dragging the seek bar past such a position would otherwise
    /// make every later preview wait for it, so the wait ends as soon as a newer request is queued.
    /// </remarks>
    private void WaitForFrame()
    {
        long deadline = Environment.TickCount64 + (long)FrameTimeout.TotalMilliseconds;
        while (!_frameSignal.Wait(SupersedeCheckInterval))
        {
            if (Environment.TickCount64 >= deadline || IsSuperseded())
            {
                return;
            }
        }
    }

    /// <summary>
    /// Determines whether a newer request is waiting, which makes the frame being decoded pointless.
    /// </summary>
    private bool IsSuperseded()
    {
        lock (_requestLock)
        {
            return _isDisposed || _pendingRequest is not null;
        }
    }

    /// <summary>
    /// Stops the preview input to release the decoder and video output after the user stops scrubbing.
    /// </summary>
    private void ClosePlayer()
    {
        try
        {
            _player?.Stop();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to stop the seek preview player.");
            DisposePlayer();
        }

        _isPlayerOpen = false;
    }

    /// <summary>
    /// Releases the preview player and its LibVLC instance.
    /// </summary>
    private void DisposePlayer()
    {
        MediaPlayer? player = _player;
        Media? media = _media;
        LibVLC? libVlc = _libVlc;
        _player = null;
        _media = null;
        _libVlc = null;
        _playerSource = null;
        _isPlayerOpen = false;

        try
        {
            if (player is not null)
            {
                player.EndReached -= OnPlayerEndReached;
                player.EncounteredError -= OnPlayerEncounteredError;
                player.Stop();
                player.Dispose();
            }

            media?.Dispose();
            libVlc?.Dispose();

            // The cleanup callback frees the buffer when a video output closes normally. This covers a player that
            // was torn down some other way. It only runs after a successful teardown, because a video output that
            // survived a failed one could still write into the buffer.
            lock (_frameLock)
            {
                FreeFrameBuffer();
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to release the seek preview player.");
        }
    }

    /// <summary>
    /// Configures the picture format and allocates the frame buffer when LibVLC opens a video output.
    /// </summary>
    /// <returns>The number of picture buffers allocated, or 0 to reject the video output.</returns>
    private unsafe uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        // LibVLCSharp uses opaque to find its MediaPlayer, so it must not be modified here.
        try
        {
            lock (_frameLock)
            {
                PreviewSource? source = _outputSource;
                double aspectRatio = SeekPreviewHelper.GetOutputAspectRatio(source?.DisplayAspectRatio ?? 0,
                    source?.StoredWidth ?? 0, source?.StoredHeight ?? 0, width, height);
                (uint frameWidth, uint frameHeight) =
                    SeekPreviewHelper.GetFrameSize(aspectRatio, MaxFrameWidth, MaxFrameHeight);
                uint pitch = SeekPreviewHelper.AlignUp(frameWidth * 4, FrameBufferAlignment);
                uint lineCount = SeekPreviewHelper.AlignUp(frameHeight, FrameBufferAlignment);

                FreeFrameBuffer();
                nuint bufferSize = (nuint)pitch * lineCount;
                void* buffer = NativeMemory.AlignedAlloc(bufferSize, FrameBufferAlignment);
                NativeMemory.Clear(buffer, bufferSize);
                _frameBuffer = (IntPtr)buffer;
                _frameBufferWidth = frameWidth;
                _frameBufferHeight = frameHeight;
                _frameBufferPitch = pitch;

                Marshal.Copy(Rv32Chroma, 0, chroma, Rv32Chroma.Length);
                width = frameWidth;
                height = frameHeight;
                pitches = pitch;
                lines = lineCount;
                return 1;
            }
        }
        catch (Exception e)
        {
            // An exception must not escape into native code. Rejecting the output makes the capture time out.
            _logger.LogWarning(e, "Failed to configure the seek preview video output.");
            return 0;
        }
    }

    /// <summary>
    /// Frees the frame buffer when LibVLC closes the video output.
    /// </summary>
    private void OnVideoCleanup(ref IntPtr opaque)
    {
        lock (_frameLock)
        {
            FreeFrameBuffer();
        }
    }

    /// <summary>
    /// Provides the frame buffer that LibVLC renders the next picture into.
    /// </summary>
    /// <returns>The picture identifier passed to the display callback, which is unused.</returns>
    private IntPtr OnVideoLock(IntPtr opaque, IntPtr planes)
    {
        lock (_frameLock)
        {
            Marshal.WriteIntPtr(planes, _frameBuffer);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Copies the displayed picture out of the frame buffer if a capture is waiting for it.
    /// </summary>
    /// <remarks>
    /// This callback also runs when LibVLC redisplays a picture while paused. Those calls are ignored because no
    /// capture is armed at that time.
    /// </remarks>
    private unsafe void OnVideoDisplay(IntPtr opaque, IntPtr picture)
    {
        try
        {
            lock (_frameLock)
            {
                if (_armedCaptureId == 0 || _capturedPixels is not null || _frameBuffer == IntPtr.Zero)
                {
                    return;
                }

                int width = (int)_frameBufferWidth;
                int height = (int)_frameBufferHeight;
                int stride = (int)_frameBufferPitch;
                byte[] pixels = new byte[width * height * 4];
                ReadOnlySpan<byte> source = new((void*)_frameBuffer, stride * height);
                SeekPreviewHelper.CopyBgraFrame(source, stride, pixels, width, height);

                _capturedPixels = pixels;
                _capturedWidth = width;
                _capturedHeight = height;
                SignalIfNotSignaled(_frameSignal);
            }
        }
        catch (Exception e)
        {
            // An exception must not escape into native code. The capture times out instead.
            _logger.LogWarning(e, "Failed to copy a seek preview frame.");
        }
    }

    /// <summary>
    /// Ends the armed capture when the input reaches its end before delivering a picture.
    /// </summary>
    private void OnPlayerEndReached(object? sender, EventArgs e)
    {
        EndCapture(hasReachedEnd: true);
    }

    /// <summary>
    /// Ends the armed capture when the input fails to open or decode.
    /// </summary>
    private void OnPlayerEncounteredError(object? sender, EventArgs e)
    {
        EndCapture(hasReachedEnd: false);
    }

    /// <summary>
    /// Wakes the worker waiting for the armed capture so that it does not wait for the full timeout.
    /// </summary>
    /// <param name="hasReachedEnd">Whether the input ended, as opposed to failing.</param>
    private void EndCapture(bool hasReachedEnd)
    {
        lock (_frameLock)
        {
            if (_armedCaptureId == 0)
            {
                return;
            }

            _hasCaptureEnded = hasReachedEnd;
            SignalIfNotSignaled(_frameSignal);
        }
    }

    /// <summary>
    /// Frees the native frame buffer. Callers must hold <see cref="_frameLock"/>.
    /// </summary>
    private unsafe void FreeFrameBuffer()
    {
        if (_frameBuffer == IntPtr.Zero)
        {
            return;
        }

        NativeMemory.AlignedFree((void*)_frameBuffer);
        _frameBuffer = IntPtr.Zero;
        _frameBufferWidth = 0;
        _frameBufferHeight = 0;
        _frameBufferPitch = 0;
    }

    /// <summary>
    /// Releases a single-count semaphore unless it is already signaled. Callers must hold the lock that
    /// guards the semaphore, so that the check and the release cannot interleave with another release.
    /// </summary>
    private static void SignalIfNotSignaled(SemaphoreSlim signal)
    {
        if (signal.CurrentCount == 0)
        {
            signal.Release();
        }
    }

    /// <summary>
    /// Identifies the media and video track that previews are decoded from.
    /// </summary>
    /// <param name="Mrl">The media resource locator of the playing media.</param>
    /// <param name="VideoTrackId">The LibVLC identifier of the video track.</param>
    /// <param name="DisplayAspectRatio">The display aspect ratio of the track, or 0 if unknown.</param>
    /// <param name="StoredWidth">The stored width of the track, before rotation metadata is applied.</param>
    /// <param name="StoredHeight">The stored height of the track, before rotation metadata is applied.</param>
    private sealed record PreviewSource(string Mrl, int VideoTrackId, double DisplayAspectRatio,
        double StoredWidth, double StoredHeight);

    /// <summary>
    /// Represents a request for the frame at a position and the task that completes with that frame.
    /// </summary>
    private sealed class PreviewRequest(PreviewSource source, TimeSpan position, SeekPreviewQuality quality)
    {
        public PreviewSource Source { get; } = source;

        public TimeSpan Position { get; } = position;

        public SeekPreviewQuality Quality { get; } = quality;

        public TaskCompletionSource<SeekPreviewFrame?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Determines whether this request is for the same frame as a request for the given source, position and quality.
        /// </summary>
        public bool Matches(PreviewSource otherSource, TimeSpan otherPosition, SeekPreviewQuality otherQuality)
        {
            return Source == otherSource && Position == otherPosition && Quality == otherQuality;
        }
    }
}
