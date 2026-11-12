namespace Screenbox.Core.Enums;

/// <summary>
/// Specifies how closely a seek preview frame has to match the requested position.
/// </summary>
public enum SeekPreviewQuality
{
    /// <summary>
    /// The nearest keyframe at or before the position, which decodes in a few dozen milliseconds.
    /// </summary>
    Fast,

    /// <summary>
    /// The frame at the position, which takes as long as decoding from the preceding keyframe.
    /// </summary>
    Exact
}
