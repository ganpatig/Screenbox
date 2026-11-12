using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.Models;
using Screenbox.Core.Services;

namespace Screenbox.Core.Contexts;

/// <summary>
/// Holds the keyboard shortcuts of the player and saves the user's changes to the settings.
/// </summary>
public sealed partial class HotkeyContext : ObservableObject
{
    /// <summary>
    /// Gets the key combinations assigned to the player actions.
    /// </summary>
    [ObservableProperty] public partial HotkeyMap Map { get; private set; }

    private readonly ISettingsService _settingsService;

    public HotkeyContext(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        Map = HotkeyMap.Parse(settingsService.PlayerHotkeys);
    }

    /// <summary>
    /// Replaces the keyboard shortcuts and saves the ones that differ from the defaults.
    /// </summary>
    /// <param name="map">The new key combinations.</param>
    public void Update(HotkeyMap map)
    {
        Map = map;
        _settingsService.PlayerHotkeys = map.Serialize();
    }
}
