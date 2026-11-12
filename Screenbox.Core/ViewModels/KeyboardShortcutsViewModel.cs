using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;

namespace Screenbox.Core.ViewModels;

/// <summary>
/// Lists the keyboard shortcuts of the player and changes the keys assigned to them.
/// </summary>
public sealed partial class KeyboardShortcutsViewModel : ObservableObject
{
    /// <summary>
    /// Gets the key combinations assigned to the player actions.
    /// </summary>
    [ObservableProperty] public partial HotkeyMap Map { get; private set; }

    /// <summary>
    /// Gets every player action that a keyboard shortcut can trigger, in the order in which they are listed.
    /// </summary>
    public IReadOnlyList<PlayerHotkeyAction> Actions => HotkeyHelper.Actions;

    private readonly HotkeyContext _hotkeyContext;

    public KeyboardShortcutsViewModel(HotkeyContext hotkeyContext)
    {
        _hotkeyContext = hotkeyContext;
        Map = hotkeyContext.Map;
    }

    /// <summary>
    /// Gets the other action that a key combination is already assigned to.
    /// </summary>
    /// <param name="action">The action that the key combination is being assigned to.</param>
    /// <param name="binding">The key combination.</param>
    /// <returns>The other action, or <see cref="PlayerHotkeyAction.None"/> if the key combination is free or already belongs to <paramref name="action"/>.</returns>
    public PlayerHotkeyAction GetConflictingAction(PlayerHotkeyAction action, HotkeyBinding binding)
    {
        return Map.TryGetAction(binding, out PlayerHotkeyAction existing) && existing != action
            ? existing
            : PlayerHotkeyAction.None;
    }

    /// <summary>
    /// Assigns a key combination to an action, removing it from any other action.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <param name="binding">The key combination.</param>
    /// <param name="keepExistingBindings"><see langword="true"/> to keep the action's other key combinations.</param>
    public void SetBinding(PlayerHotkeyAction action, HotkeyBinding binding, bool keepExistingBindings)
    {
        if (!HotkeyHelper.IsAssignable(binding)) return;
        Apply(Map.SetBinding(action, binding, keepExistingBindings));
    }

    /// <summary>
    /// Removes every key combination from an action.
    /// </summary>
    /// <param name="action">The player action.</param>
    public void ClearBindings(PlayerHotkeyAction action)
    {
        Apply(Map.ClearBindings(action));
    }

    /// <summary>
    /// Restores the default key combinations of an action.
    /// </summary>
    /// <param name="action">The player action.</param>
    public void ResetBindings(PlayerHotkeyAction action)
    {
        Apply(Map.ResetBindings(action));
    }

    /// <summary>
    /// Restores the default key combinations of every action.
    /// </summary>
    public void ResetAllBindings()
    {
        Apply(HotkeyMap.Default);
    }

    private void Apply(HotkeyMap map)
    {
        _hotkeyContext.Update(map);
        Map = map;
    }
}
