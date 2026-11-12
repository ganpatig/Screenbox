using Screenbox.Core.Enums;

namespace Screenbox.Core.Models;

/// <summary>
/// Describes a track or chapter for the on-screen display.
/// </summary>
/// <param name="Label">The name of the track or chapter, or an empty string if it has none.</param>
/// <param name="Number">The one-based position of the track or chapter, used when it has no name.</param>
public sealed record PlayerOsdItem(string Label, int Number);

/// <summary>
/// Describes a subtitle synchronization step for the on-screen display.
/// </summary>
/// <param name="Step">The outcome of the key press.</param>
/// <param name="CorrectionMilliseconds">The delay added by <see cref="SubtitleSyncStep.Applied"/>, in milliseconds.</param>
/// <param name="TotalDelayMilliseconds">The resulting subtitle delay of <see cref="SubtitleSyncStep.Applied"/>, in milliseconds.</param>
public sealed record SubtitleSyncStatus(SubtitleSyncStep Step, int CorrectionMilliseconds = 0, int TotalDelayMilliseconds = 0);

/// <summary>
/// Describes the deinterlacing state for the on-screen display.
/// </summary>
/// <param name="IsEnabled">Whether deinterlacing is on.</param>
/// <param name="ModeLabel">The name of the deinterlace mode, or <see langword="null"/> to leave it out.</param>
public sealed record DeinterlaceStatus(bool IsEnabled, string? ModeLabel);
