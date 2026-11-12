using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Models;

/// <summary>
/// Represents the key combinations assigned to the player actions, starting from the defaults and applying the user's changes.
/// </summary>
/// <remarks>
/// A key combination triggers at most one action. The map is immutable: the methods that change assignments return a new map,
/// so a map can be read from key handlers while the shortcut editor creates the next one.
/// </remarks>
public sealed class HotkeyMap
{
    private readonly Dictionary<PlayerHotkeyAction, HotkeyBinding[]> _bindings;
    private readonly Dictionary<HotkeyBinding, PlayerHotkeyAction> _actions;

    /// <summary>
    /// Gets the map with the default key combinations.
    /// </summary>
    public static HotkeyMap Default { get; } = new(HotkeyHelper.Actions.ToDictionary(
        action => action,
        action => HotkeyHelper.GetDefaultBindings(action).ToArray()));

    private HotkeyMap(Dictionary<PlayerHotkeyAction, HotkeyBinding[]> bindings)
    {
        _bindings = bindings;
        _actions = new Dictionary<HotkeyBinding, PlayerHotkeyAction>();
        foreach (PlayerHotkeyAction action in HotkeyHelper.Actions)
        {
            foreach (HotkeyBinding binding in GetBindings(action))
            {
                // Earlier actions win if the stored settings assign a key twice.
                _actions.TryAdd(binding, action);
            }
        }
    }

    /// <summary>
    /// Gets the action that a key combination triggers.
    /// </summary>
    /// <param name="binding">The key combination that was pressed.</param>
    /// <param name="action">When this method returns, contains the action, or <see cref="PlayerHotkeyAction.None"/>.</param>
    /// <returns><see langword="true"/> if the key combination triggers an action; otherwise, <see langword="false"/>.</returns>
    public bool TryGetAction(HotkeyBinding binding, out PlayerHotkeyAction action)
    {
        return _actions.TryGetValue(binding, out action);
    }

    /// <summary>
    /// Gets the key combinations assigned to an action.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <returns>The key combinations, in the order they were assigned, or an empty list if the action has none.</returns>
    public IReadOnlyList<HotkeyBinding> GetBindings(PlayerHotkeyAction action)
    {
        return _bindings.TryGetValue(action, out HotkeyBinding[]? bindings) ? bindings : Array.Empty<HotkeyBinding>();
    }

    /// <summary>
    /// Determines whether an action has its default key combinations.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <returns><see langword="true"/> if the action has its default key combinations; otherwise, <see langword="false"/>.</returns>
    public bool IsDefault(PlayerHotkeyAction action)
    {
        return GetBindings(action).SequenceEqual(HotkeyHelper.GetDefaultBindings(action));
    }

    /// <summary>
    /// Assigns a key combination to an action and removes it from any other action.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <param name="binding">The key combination to assign.</param>
    /// <param name="keepExistingBindings">
    /// <see langword="true"/> to add the key combination to the action's other key combinations;
    /// <see langword="false"/> to make it the action's only key combination.
    /// </param>
    /// <returns>The map with the new assignment.</returns>
    public HotkeyMap SetBinding(PlayerHotkeyAction action, HotkeyBinding binding, bool keepExistingBindings)
    {
        Dictionary<PlayerHotkeyAction, HotkeyBinding[]> bindings = RemoveBinding(binding);
        HotkeyBinding[] existing = keepExistingBindings && bindings.TryGetValue(action, out HotkeyBinding[]? current) ? current : [];
        bindings[action] = [.. existing, binding];
        return new HotkeyMap(bindings);
    }

    /// <summary>
    /// Removes every key combination from an action.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <returns>The map without key combinations for the action.</returns>
    public HotkeyMap ClearBindings(PlayerHotkeyAction action)
    {
        Dictionary<PlayerHotkeyAction, HotkeyBinding[]> bindings = new(_bindings)
        {
            [action] = [],
        };

        return new HotkeyMap(bindings);
    }

    /// <summary>
    /// Restores the default key combinations of an action and removes them from any other action.
    /// </summary>
    /// <param name="action">The player action.</param>
    /// <returns>The map with the default key combinations for the action.</returns>
    public HotkeyMap ResetBindings(PlayerHotkeyAction action)
    {
        HotkeyMap map = ClearBindings(action);
        foreach (HotkeyBinding binding in HotkeyHelper.GetDefaultBindings(action))
        {
            map = map.SetBinding(action, binding, keepExistingBindings: true);
        }

        return map;
    }

    /// <summary>
    /// Converts the assignments that differ from the defaults to the text that is stored in the settings.
    /// </summary>
    /// <returns>
    /// The changed actions and their key combinations, such as <c>Faster=4:187,0:107;NormalRate=</c>,
    /// or an empty string if every action has its default key combinations.
    /// </returns>
    public string Serialize()
    {
        StringBuilder builder = new();
        foreach (PlayerHotkeyAction action in HotkeyHelper.Actions)
        {
            if (IsDefault(action))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(';');
            }

            builder.Append(action.ToString()).Append('=');
            builder.AppendJoin(',', GetBindings(action).Select(binding => binding.ToStorageString()));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates a map from the text that is stored in the settings.
    /// </summary>
    /// <remarks>
    /// Unknown actions and invalid key combinations are skipped, so that settings written by other versions still load.
    /// </remarks>
    /// <param name="text">The text created by <see cref="Serialize"/>, or <see langword="null"/> for the defaults.</param>
    /// <returns>The map with the stored assignments applied to the defaults.</returns>
    public static HotkeyMap Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Default;
        }

        Dictionary<PlayerHotkeyAction, HotkeyBinding[]> bindings = new(Default._bindings);
        foreach (string entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('=');
            if (separator <= 0 ||
                !Enum.TryParse(entry.AsSpan(0, separator).Trim(), out PlayerHotkeyAction action) ||
                action is PlayerHotkeyAction.None ||
                !bindings.ContainsKey(action))
            {
                continue;
            }

            List<HotkeyBinding> actionBindings = new();
            foreach (string value in entry[(separator + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (HotkeyBinding.TryParse(value, out HotkeyBinding binding) &&
                    HotkeyHelper.IsAssignable(binding) &&
                    !actionBindings.Contains(binding))
                {
                    actionBindings.Add(binding);
                }
            }

            bindings[action] = actionBindings.ToArray();
        }

        return new HotkeyMap(bindings);
    }

    private Dictionary<PlayerHotkeyAction, HotkeyBinding[]> RemoveBinding(HotkeyBinding binding)
    {
        Dictionary<PlayerHotkeyAction, HotkeyBinding[]> bindings = new(_bindings);
        foreach ((PlayerHotkeyAction action, HotkeyBinding[] actionBindings) in _bindings)
        {
            if (actionBindings.Contains(binding))
            {
                bindings[action] = actionBindings.Where(existing => existing != binding).ToArray();
            }
        }

        return bindings;
    }
}
