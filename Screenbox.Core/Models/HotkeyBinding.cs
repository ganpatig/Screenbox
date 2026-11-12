using System;
using System.Globalization;
using Windows.System;

namespace Screenbox.Core.Models;

/// <summary>
/// Represents a key combination that triggers a player action.
/// </summary>
/// <param name="Key">The key that is pressed, such as <see cref="VirtualKey.Space"/>.</param>
/// <param name="Modifiers">The modifier keys that are held while the key is pressed.</param>
public readonly record struct HotkeyBinding(VirtualKey Key, VirtualKeyModifiers Modifiers = VirtualKeyModifiers.None)
{
    /// <summary>
    /// Converts the key combination to the text that is stored in the settings.
    /// </summary>
    /// <returns>The modifiers and the key as numbers, such as <c>4:187</c> for Shift and the "=" key.</returns>
    public string ToStorageString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{(int)Modifiers}:{(int)Key}");
    }

    /// <summary>
    /// Reads a key combination from the text that is stored in the settings.
    /// </summary>
    /// <param name="text">The text created by <see cref="ToStorageString"/>.</param>
    /// <param name="binding">When this method returns, contains the key combination, or the default value if the text is invalid.</param>
    /// <returns><see langword="true"/> if the text is a valid key combination; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int modifiers) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int key) ||
            key is <= 0 or > 0xFE ||
            (modifiers & ~(int)(VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift)) != 0)
        {
            return false;
        }

        binding = new HotkeyBinding((VirtualKey)key, (VirtualKeyModifiers)modifiers);
        return true;
    }
}
