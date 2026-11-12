using System;
using Screenbox.Core.Models;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml.Input;

namespace Screenbox.Helpers;

/// <summary>
/// Provides <see langword="static"/> helper methods for working with keyboard accelerators
/// and related key information.
/// </summary>
public static class KeyboardAcceleratorHelper
{
    /// <summary>The Equals (=) and Plus (+) key or button for any country/region (VK_OEM_PLUS).</summary>
    /// <remarks>
    /// When used on an <b>AppBarButton</b>, <b>AppBarToggleButton</b>, <b>MenuFlyoutItem</b>
    /// and derived controls, the <c>KeyboardAcceleratorTextOverride</c> property is required.
    /// For other controls, set <c>KeyboardAcceleratorPlacementMode</c> to
    /// <see cref="KeyboardAcceleratorPlacementMode.Hidden"/> to prevent crashes.
    /// </remarks>
    public const VirtualKey OemPlus = (VirtualKey)0xBB;

    /// <summary>The Comma (,) and Less Than (&lt;) key or button for any country/region (VK_OEM_COMMA).</summary>
    /// <remarks>
    /// When used on an <b>AppBarButton</b>, <b>AppBarToggleButton</b>, <b>MenuFlyoutItem</b>
    /// and derived controls, the <c>KeyboardAcceleratorTextOverride</c> property is required.
    /// For other controls, set <c>KeyboardAcceleratorPlacementMode</c> to
    /// <see cref="KeyboardAcceleratorPlacementMode.Hidden"/> to prevent crashes.
    /// </remarks>
    public const VirtualKey OemComma = (VirtualKey)0xBC;

    /// <summary>The Dash (-) and Underscore (_) key or button for any country/region (VK_OEM_MINUS).</summary>
    /// <remarks>
    /// When used on an <b>AppBarButton</b>, <b>AppBarToggleButton</b>, <b>MenuFlyoutItem</b>
    /// and derived controls, the <c>KeyboardAcceleratorTextOverride</c> property is required.
    /// For other controls, set <c>KeyboardAcceleratorPlacementMode</c> to
    /// <see cref="KeyboardAcceleratorPlacementMode.Hidden"/> to prevent crashes.
    /// </remarks>
    public const VirtualKey OemMinus = (VirtualKey)0xBD;

    /// <summary>The Period (.) and Greater Than (&gt;) key or button for any country/region (VK_OEM_PERIOD).</summary>
    /// <remarks>
    /// When used on an <b>AppBarButton</b>, <b>AppBarToggleButton</b>, <b>MenuFlyoutItem</b>
    /// and derived controls, the <c>KeyboardAcceleratorTextOverride</c> property is required.
    /// For other controls, set <c>KeyboardAcceleratorPlacementMode</c> to
    /// <see cref="KeyboardAcceleratorPlacementMode.Hidden"/> to prevent crashes.
    /// </remarks>
    public const VirtualKey OemPeriod = (VirtualKey)0xBE;

    /// <summary>
    /// Gets a value that indicates whether the Control key is currently pressed down.
    /// </summary>
    /// <value><see langword="true"/> if the control key is pressed down; otherwise, <see langword="false"/>.</value>
    public static bool IsControlKeyDown => IsKeyDown(VirtualKey.Control);

    /// <summary>
    /// Gets a value that indicates whether the Shift key is currently pressed down.
    /// </summary>
    /// <value><see langword="true"/> if the shift key is pressed down; otherwise, <see langword="false"/>.</value>
    public static bool IsShiftKeyDown => IsKeyDown(VirtualKey.Shift);

    /// <summary>
    /// Gets a value that indicates whether either Windows key is currently pressed down.
    /// </summary>
    /// <value><see langword="true"/> if a Windows key is pressed down; otherwise, <see langword="false"/>.</value>
    public static bool IsWindowsKeyDown => IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);

    /// <summary>
    /// Gets the modifier keys that are held down during a key event of the window.
    /// </summary>
    /// <param name="keyStatus">The status of the key that the event belongs to.</param>
    /// <returns>A combination of <see cref="VirtualKeyModifiers.Control"/>, <see cref="VirtualKeyModifiers.Menu"/>
    /// and <see cref="VirtualKeyModifiers.Shift"/>.</returns>
    public static VirtualKeyModifiers GetModifiers(CorePhysicalKeyStatus keyStatus)
    {
        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None;
        if (IsControlKeyDown) modifiers |= VirtualKeyModifiers.Control;
        if (IsShiftKeyDown) modifiers |= VirtualKeyModifiers.Shift;
        if (keyStatus.IsMenuKeyDown) modifiers |= VirtualKeyModifiers.Menu;
        return modifiers;
    }

    /// <summary>
    /// Converts the value of the specified <see cref="KeyboardAccelerator"/> to its equivalent
    /// <see cref="string"/> representation.
    /// </summary>
    /// <param name="value">The <see cref="KeyboardAccelerator"/> to convert.</param>
    /// <returns>The localized string representation of <paramref name="value"/>,
    /// or an empty string if the <paramref name="value"/> is <see langword="null"/>.</returns>
    public static string ToDisplayName(this KeyboardAccelerator value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return ToDisplayName(value.Key, value.Modifiers);
    }

    /// <summary>
    /// Converts a keyboard shortcut to its equivalent <see cref="string"/> representation.
    /// </summary>
    /// <param name="binding">The key combination of the shortcut.</param>
    /// <returns>The localized string representation of the key combination, such as "Ctrl+Shift+K".</returns>
    public static string ToDisplayName(this HotkeyBinding binding)
    {
        // The number pad plus and minus keys look the same as the main keys without a prefix.
        string keyText = binding.Key switch
        {
            VirtualKey.Add => $"{Strings.KeyboardResources.VirtualKeyNumberPadPrefix} +",
            VirtualKey.Subtract => $"{Strings.KeyboardResources.VirtualKeyNumberPadPrefix} -",
            _ => string.Empty,
        };

        return keyText.Length > 0
            ? FormatDisplayName(keyText, binding.Modifiers)
            : ToDisplayName(binding.Key, binding.Modifiers);
    }

    private static string ToDisplayName(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        return FormatDisplayName(key.ToDisplayName(), modifiers);
    }

    private static string FormatDisplayName(string keyText, VirtualKeyModifiers modifiers)
    {
        string separator = Strings.KeyboardResources.KeyboardAcceleratorValueSeparator;

        string[] parts = new string[5];
        int count = 0;

        // Modifier keys in the tooltip appear in a different sequence than specified by the enum.
        if ((modifiers & VirtualKeyModifiers.Control) != 0)
        {
            parts[count++] = VirtualKeyModifiers.Control.ToDisplayName();
        }
        if ((modifiers & VirtualKeyModifiers.Menu) != 0)
        {
            parts[count++] = VirtualKeyModifiers.Menu.ToDisplayName();
        }
        if ((modifiers & VirtualKeyModifiers.Windows) != 0)
        {
            parts[count++] = VirtualKeyModifiers.Windows.ToDisplayName();
        }
        if ((modifiers & VirtualKeyModifiers.Shift) != 0)
        {
            parts[count++] = VirtualKeyModifiers.Shift.ToDisplayName();
        }

        // Avoid adding the key if it is already represented as a modifier.
        if (!string.IsNullOrEmpty(keyText) && Array.IndexOf(parts, keyText, 0, count) == -1)
        {
            parts[count++] = keyText;
        }

        return string.Join(separator, parts, 0, count);
    }

    /// <summary>
    /// Converts the value of the specified <see cref="VirtualKey"/> to its equivalent
    /// string representation.
    /// </summary>
    /// <param name="value">The <see cref="VirtualKey"/> to convert.</param>
    /// <returns>The localized string representation of <paramref name="value"/>.</returns>
    private static string ToDisplayName(this VirtualKey value)
    {
        return value switch
        {
            VirtualKey.None => string.Empty, // Only the AppBarButton/AppBarToggleButton and MenuFlyoutItem/ToggleMenuFlyoutItem tooltip returns a value.
            VirtualKey.LeftButton => Strings.KeyboardResources.VirtualKeyLeftButton,
            VirtualKey.RightButton => Strings.KeyboardResources.VirtualKeyRightButton,
            VirtualKey.Cancel => Strings.KeyboardResources.VirtualKeyCancel,
            VirtualKey.MiddleButton => Strings.KeyboardResources.VirtualKeyMiddleButton,
            VirtualKey.XButton1 => Strings.KeyboardResources.VirtualKeyXButton1,
            VirtualKey.XButton2 => Strings.KeyboardResources.VirtualKeyXButton2,
            VirtualKey.Back => Strings.KeyboardResources.VirtualKeyBack,
            VirtualKey.Tab => Strings.KeyboardResources.VirtualKeyTab,
            VirtualKey.Clear => Strings.KeyboardResources.VirtualKeyClear,
            VirtualKey.Enter => Strings.KeyboardResources.VirtualKeyEnter,
            VirtualKey.Shift => Strings.KeyboardResources.VirtualKeyModifiersShift,
            VirtualKey.Control => Strings.KeyboardResources.VirtualKeyModifiersControl,
            VirtualKey.Menu => Strings.KeyboardResources.VirtualKeyModifiersMenu,
            VirtualKey.Pause => Strings.KeyboardResources.VirtualKeyPause,
            VirtualKey.CapitalLock => Strings.KeyboardResources.VirtualKeyCapitalLock,
            VirtualKey.Hangul or VirtualKey.Kana => Strings.KeyboardResources.VirtualKeyHangul,
            VirtualKey.Junja => Strings.KeyboardResources.VirtualKeyJunja,
            VirtualKey.Final => Strings.KeyboardResources.VirtualKeyFinal,
            VirtualKey.Hanja or VirtualKey.Kanji => Strings.KeyboardResources.VirtualKeyHanja,
            VirtualKey.Escape => Strings.KeyboardResources.VirtualKeyEscape,
            VirtualKey.Convert => Strings.KeyboardResources.VirtualKeyConvert,
            VirtualKey.NonConvert => Strings.KeyboardResources.VirtualKeyNonConvert,
            VirtualKey.Accept => Strings.KeyboardResources.VirtualKeyAccept,
            VirtualKey.ModeChange => Strings.KeyboardResources.VirtualKeyModeChange,
            VirtualKey.Space => Strings.KeyboardResources.VirtualKeySpace,
            VirtualKey.PageUp => Strings.KeyboardResources.VirtualKeyPageUp,
            VirtualKey.PageDown => Strings.KeyboardResources.VirtualKeyPageDown,
            VirtualKey.End => Strings.KeyboardResources.VirtualKeyEnd,
            VirtualKey.Home => Strings.KeyboardResources.VirtualKeyHome,
            VirtualKey.Left => Strings.KeyboardResources.VirtualKeyLeft,
            VirtualKey.Up => Strings.KeyboardResources.VirtualKeyUp,
            VirtualKey.Right => Strings.KeyboardResources.VirtualKeyRight,
            VirtualKey.Down => Strings.KeyboardResources.VirtualKeyDown,
            VirtualKey.Select => Strings.KeyboardResources.VirtualKeySelect,
            VirtualKey.Print => Strings.KeyboardResources.VirtualKeyPrint,
            VirtualKey.Execute => Strings.KeyboardResources.VirtualKeyExecute,
            VirtualKey.Snapshot => Strings.KeyboardResources.VirtualKeySnapshot,
            VirtualKey.Insert => Strings.KeyboardResources.VirtualKeyInsert,
            VirtualKey.Delete => Strings.KeyboardResources.VirtualKeyDelete,
            VirtualKey.Help => Strings.KeyboardResources.VirtualKeyHelp,
            VirtualKey.Number0 => "0",
            VirtualKey.Number1 => "1",
            VirtualKey.Number2 => "2",
            VirtualKey.Number3 => "3",
            VirtualKey.Number4 => "4",
            VirtualKey.Number5 => "5",
            VirtualKey.Number6 => "6",
            VirtualKey.Number7 => "7",
            VirtualKey.Number8 => "8",
            VirtualKey.Number9 => "9",
            VirtualKey.LeftWindows => Strings.KeyboardResources.VirtualKeyModifiersWindows,
            VirtualKey.RightWindows => Strings.KeyboardResources.VirtualKeyModifiersWindows,
            VirtualKey.Application => Strings.KeyboardResources.VirtualKeyApplication,
            VirtualKey.Sleep => Strings.KeyboardResources.VirtualKeySleep,
            VirtualKey.NumberPad0 => Strings.KeyboardResources.VirtualKeyNumberPad0,
            VirtualKey.NumberPad1 => Strings.KeyboardResources.VirtualKeyNumberPad1,
            VirtualKey.NumberPad2 => Strings.KeyboardResources.VirtualKeyNumberPad2,
            VirtualKey.NumberPad3 => Strings.KeyboardResources.VirtualKeyNumberPad3,
            VirtualKey.NumberPad4 => Strings.KeyboardResources.VirtualKeyNumberPad4,
            VirtualKey.NumberPad5 => Strings.KeyboardResources.VirtualKeyNumberPad5,
            VirtualKey.NumberPad6 => Strings.KeyboardResources.VirtualKeyNumberPad6,
            VirtualKey.NumberPad7 => Strings.KeyboardResources.VirtualKeyNumberPad7,
            VirtualKey.NumberPad8 => Strings.KeyboardResources.VirtualKeyNumberPad8,
            VirtualKey.NumberPad9 => Strings.KeyboardResources.VirtualKeyNumberPad9,
            VirtualKey.Multiply => "*",
            VirtualKey.Add => "+",
            VirtualKey.Separator => Strings.KeyboardResources.VirtualKeySeparator,
            VirtualKey.Subtract => "-",
            VirtualKey.Decimal => ".",
            VirtualKey.Divide => "/",
            // The punctuation keys, named after the characters they type on the US keyboard layout.
            (VirtualKey)0xBA => ";",
            (VirtualKey)0xBB => "=",
            (VirtualKey)0xBC => ",",
            (VirtualKey)0xBD => "-",
            (VirtualKey)0xBE => ".",
            (VirtualKey)0xBF => "/",
            (VirtualKey)0xC0 => "`",
            (VirtualKey)0xDB => "[",
            (VirtualKey)0xDC => "\\",
            (VirtualKey)0xDD => "]",
            (VirtualKey)0xDE => "'",
            VirtualKey.NumberKeyLock => Strings.KeyboardResources.VirtualKeyNumberKeyLock,
            VirtualKey.Scroll => Strings.KeyboardResources.VirtualKeyScroll,
            VirtualKey.LeftShift => Strings.KeyboardResources.VirtualKeyModifiersShift,
            VirtualKey.RightShift => Strings.KeyboardResources.VirtualKeyModifiersShift,
            VirtualKey.LeftControl => Strings.KeyboardResources.VirtualKeyModifiersControl,
            VirtualKey.RightControl => Strings.KeyboardResources.VirtualKeyModifiersControl,
            VirtualKey.LeftMenu => Strings.KeyboardResources.VirtualKeyModifiersMenu,
            VirtualKey.RightMenu => Strings.KeyboardResources.VirtualKeyModifiersMenu,
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Converts the value of the specified <see cref="VirtualKeyModifiers"/> to its equivalent
    /// string representation.
    /// </summary>
    /// <param name="value">The <see cref="VirtualKeyModifiers"/> to convert (single flag only).</param>
    /// <returns>The localized string representation of <paramref name="value"/>.</returns>
    private static string ToDisplayName(this VirtualKeyModifiers value)
    {
        return value switch
        {
            VirtualKeyModifiers.Control => Strings.KeyboardResources.VirtualKeyModifiersControl,
            VirtualKeyModifiers.Menu => Strings.KeyboardResources.VirtualKeyModifiersMenu,
            VirtualKeyModifiers.Shift => Strings.KeyboardResources.VirtualKeyModifiersShift,
            VirtualKeyModifiers.Windows => Strings.KeyboardResources.VirtualKeyModifiersWindows,
            _ => string.Empty
        };
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        CoreWindow? window = CoreWindow.GetForCurrentThread();
        if (window is null)
        {
            return false;
        }

        // The key state that belongs to the current key event might not be up to date yet while the window's
        // AcceleratorKeyActivated event is raised. The physical state of the key is checked as well, because
        // a modifier key is still held down when the other key of the combination is pressed.
        return window.GetKeyState(key).HasFlag(CoreVirtualKeyStates.Down) ||
               window.GetAsyncKeyState(key).HasFlag(CoreVirtualKeyStates.Down);
    }
}
