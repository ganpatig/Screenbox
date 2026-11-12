using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Screenbox.Helpers;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Dialogs;

/// <summary>
/// Lists the keyboard shortcuts of the player and assigns different keys to them.
/// </summary>
public sealed partial class KeyboardShortcutsDialog : ContentDialog
{
    /// <summary>
    /// Gets a value that indicates whether the dialog is recording a key combination.
    /// </summary>
    /// <remarks>While it is, the keys are for the dialog, so the window must not use them for anything else.</remarks>
    public static bool IsRecordingKeys { get; private set; }

    internal KeyboardShortcutsViewModel ViewModel { get; }

    private readonly ISettingsService _settingsService;
    private readonly Dictionary<PlayerHotkeyAction, TextBlock> _keysTextBlocks = new();

    private readonly Flyout _editorFlyout = new() { Placement = FlyoutPlacementMode.Bottom };
    private readonly TextBlock _editorTitle = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _currentKeysText = new() { Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly Button _recordButton = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 56 };
    private readonly TextBlock _recordText = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _conflictText = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button _setButton = new() { Content = Strings.Resources.HotkeySet, MinWidth = 96 };
    private readonly Button _addButton = new() { Content = Strings.Resources.HotkeyAdd, MinWidth = 96 };
    private readonly Button _removeButton = new() { Content = Strings.Resources.HotkeyRemove };
    private readonly Button _resetButton = new() { Content = Strings.Resources.HotkeyReset };

    private PlayerHotkeyAction _editedAction;
    private HotkeyBinding? _recordedBinding;
    private bool _isRecording;
    private bool _suppressAltRelease;

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public KeyboardShortcutsDialog()
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)Window.Current.Content).RequestedTheme;
        ViewModel = Ioc.Default.GetRequiredService<KeyboardShortcutsViewModel>();
        _settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

        CreateEditor();
        AddShortcuts();
        Closed += (_, _) => StopRecording();
    }

    /// <summary>
    /// Shows the keyboard shortcuts, unless another dialog is already open.
    /// </summary>
    public static async Task ShowShortcutsAsync()
    {
        try
        {
            await new KeyboardShortcutsDialog().ShowAsync();
        }
        catch (COMException)
        {
            // Only one content dialog can be open at a time, and another one is already showing.
        }
    }

    private void AddShortcuts()
    {
        Grid introduction = new() { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 4) };
        introduction.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        introduction.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Button resetAllButton = new()
        {
            Content = Strings.Resources.HotkeyResetAll,
            Flyout = CreateResetAllFlyout(),
            VerticalAlignment = VerticalAlignment.Top,
        };

        Grid.SetColumn(resetAllButton, 1);
        introduction.Children.Add(new TextBlock { Text = Strings.Resources.HotkeyEditorHint, TextWrapping = TextWrapping.Wrap });
        introduction.Children.Add(resetAllButton);
        ShortcutsPanel.Children.Add(introduction);

        HotkeyCategory? category = null;
        foreach (PlayerHotkeyAction action in ViewModel.Actions)
        {
            HotkeyCategory actionCategory = HotkeyHelper.GetCategory(action);
            if (actionCategory != category)
            {
                category = actionCategory;
                AddHeader(HotkeyLabelHelper.GetCategoryName(actionCategory));
            }

            AddShortcut(action);
        }

        ShortcutsPanel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 16, 0, 0),
            Opacity = 0.8,
            Text = Strings.Resources.ShortcutsUnsupportedNote,
            TextWrapping = TextWrapping.Wrap,
        });

        UpdateKeysText();
    }

    private void AddHeader(string header)
    {
        TextBlock headerText = new()
        {
            Margin = new Thickness(0, 16, 0, 2),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Text = header,
        };

        AutomationProperties.SetHeadingLevel(headerText, AutomationHeadingLevel.Level2);
        ShortcutsPanel.Children.Add(headerText);
    }

    private void AddShortcut(PlayerHotkeyAction action)
    {
        string description = HotkeyLabelHelper.GetDescription(action, _settingsService);

        Grid row = new() { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock descriptionText = new()
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        TextBlock keysText = new()
        {
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Button changeButton = new()
        {
            Content = Strings.Resources.HotkeyChange,
            VerticalAlignment = VerticalAlignment.Center,
        };

        AutomationProperties.SetName(changeButton, Strings.Resources.HotkeyChangeDescription(description));
        changeButton.Click += (_, _) => ShowEditor(action, changeButton);

        Grid.SetColumn(keysText, 1);
        Grid.SetColumn(changeButton, 2);
        row.Children.Add(descriptionText);
        row.Children.Add(keysText);
        row.Children.Add(changeButton);
        ShortcutsPanel.Children.Add(row);
        _keysTextBlocks[action] = keysText;
    }

    private Flyout CreateResetAllFlyout()
    {
        Flyout flyout = new();
        Button confirmButton = new()
        {
            Content = Strings.Resources.HotkeyReset,
            Style = GetAccentButtonStyle(),
        };

        confirmButton.Click += (_, _) =>
        {
            ViewModel.ResetAllBindings();
            UpdateKeysText();
            flyout.Hide();
        };

        StackPanel content = new() { MaxWidth = 280, Spacing = 12 };
        content.Children.Add(new TextBlock { Text = Strings.Resources.HotkeyResetAllConfirmation, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(confirmButton);
        flyout.Content = content;
        return flyout;
    }

    [DynamicWindowsRuntimeCast(typeof(Brush))]
    private void CreateEditor()
    {
        _setButton.Style = GetAccentButtonStyle();
        if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out object brush))
        {
            _conflictText.Foreground = brush as Brush;
        }

        _recordButton.Content = _recordText;
        _recordButton.GotFocus += (_, _) => StartRecording();
        _recordButton.LostFocus += (_, _) => StopRecording();
        AutomationProperties.SetName(_recordButton, Strings.Resources.HotkeyPressKeys);
        AutomationProperties.SetLiveSetting(_recordText, AutomationLiveSetting.Polite);

        _setButton.Click += (_, _) => ApplyRecordedBinding(keepExistingBindings: false);
        _addButton.Click += (_, _) => ApplyRecordedBinding(keepExistingBindings: true);
        _removeButton.Click += (_, _) =>
        {
            ViewModel.ClearBindings(_editedAction);
            CloseEditor();
        };

        _resetButton.Click += (_, _) =>
        {
            ViewModel.ResetBindings(_editedAction);
            CloseEditor();
        };

        StackPanel assignButtons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        assignButtons.Children.Add(_setButton);
        assignButtons.Children.Add(_addButton);

        StackPanel otherButtons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        otherButtons.Children.Add(_removeButton);
        otherButtons.Children.Add(_resetButton);

        StackPanel content = new() { Width = 320, Spacing = 12 };
        content.Children.Add(_editorTitle);
        content.Children.Add(_currentKeysText);
        content.Children.Add(new TextBlock { Text = Strings.Resources.HotkeyPressKeys, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_recordButton);
        content.Children.Add(_conflictText);
        content.Children.Add(assignButtons);
        content.Children.Add(otherButtons);

        _editorFlyout.Content = content;
        _editorFlyout.Opened += (_, _) => _recordButton.Focus(FocusState.Keyboard);
        _editorFlyout.Closed += (_, _) => StopRecording();
    }

    /// <summary>
    /// Gets the style of the accent button, or <see langword="null"/> if the app resources do not have it.
    /// </summary>
    /// <remarks>The style only changes how a button looks, so a missing style must not keep the dialog from opening.</remarks>
    [DynamicWindowsRuntimeCast(typeof(Style))]
    private static Style? GetAccentButtonStyle()
    {
        return Application.Current.Resources.TryGetValue("AccentButtonStyle", out object style) ? style as Style : null;
    }

    private void ShowEditor(PlayerHotkeyAction action, FrameworkElement target)
    {
        _editedAction = action;
        _recordedBinding = null;
        _editorTitle.Text = HotkeyLabelHelper.GetDescription(action, _settingsService);
        UpdateEditor();
        _editorFlyout.ShowAt(target);
    }

    private void CloseEditor()
    {
        UpdateKeysText();
        _editorFlyout.Hide();
    }

    private void ApplyRecordedBinding(bool keepExistingBindings)
    {
        if (_recordedBinding is { } binding)
        {
            ViewModel.SetBinding(_editedAction, binding, keepExistingBindings);
        }

        CloseEditor();
    }

    private void UpdateKeysText()
    {
        foreach ((PlayerHotkeyAction action, TextBlock keysText) in _keysTextBlocks)
        {
            keysText.Text = HotkeyLabelHelper.GetKeysText(ViewModel.Map, action);
        }
    }

    private void UpdateEditor()
    {
        HotkeyMap map = ViewModel.Map;
        IReadOnlyList<HotkeyBinding> bindings = map.GetBindings(_editedAction);
        _currentKeysText.Text = HotkeyLabelHelper.GetKeysText(map, _editedAction);

        PlayerHotkeyAction conflictingAction = PlayerHotkeyAction.None;
        bool isAssigned = false;
        if (_recordedBinding is { } binding)
        {
            conflictingAction = ViewModel.GetConflictingAction(_editedAction, binding);
            isAssigned = bindings.Contains(binding);
        }

        // Set replaces the keys, so it only changes something unless the recorded keys are already the only keys.
        _setButton.IsEnabled = _recordedBinding is not null && !(isAssigned && bindings.Count == 1);
        _addButton.IsEnabled = _recordedBinding is not null && !isAssigned && bindings.Count > 0;
        _removeButton.IsEnabled = bindings.Count > 0;
        _resetButton.IsEnabled = !map.IsDefault(_editedAction);

        if (conflictingAction is not PlayerHotkeyAction.None && _recordedBinding is { } conflictingBinding)
        {
            _conflictText.Text = Strings.Resources.HotkeyConflictWarning(
                conflictingBinding.ToDisplayName(),
                HotkeyLabelHelper.GetDescription(conflictingAction, _settingsService));
            _conflictText.Visibility = Visibility.Visible;
        }
        else
        {
            _conflictText.Visibility = Visibility.Collapsed;
        }

        UpdateRecordText();
    }

    private void UpdateRecordText()
    {
        _recordText.Opacity = _recordedBinding is null ? 0.7 : 1;
        _recordText.Text = _recordedBinding is { } binding
            ? binding.ToDisplayName()
            : _isRecording ? Strings.Resources.HotkeyWaitingForKeys : Strings.Resources.HotkeySelectToRecord;
    }

    private void StartRecording()
    {
        if (_isRecording) return;
        _isRecording = true;
        IsRecordingKeys = true;
        _suppressAltRelease = false;
        Dispatcher.AcceleratorKeyActivated += Dispatcher_AcceleratorKeyActivated;
        UpdateRecordText();
    }

    private void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        IsRecordingKeys = false;
        Dispatcher.AcceleratorKeyActivated -= Dispatcher_AcceleratorKeyActivated;
        UpdateRecordText();
    }

    private void Dispatcher_AcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs args)
    {
        VirtualKey key = args.VirtualKey;

        // Tab moves the focus out of the key box, so that the buttons can be reached without a mouse.
        if (key is VirtualKey.Tab)
        {
            return;
        }

        switch (args.EventType)
        {
            case CoreAcceleratorKeyEventType.KeyDown or CoreAcceleratorKeyEventType.SystemKeyDown:
                HotkeyBinding binding = new(key, KeyboardAcceleratorHelper.GetModifiers(args.KeyStatus));

                // Modifier keys wait for the key they are combined with. Combinations with the Windows key
                // belong to the system, and Alt+F4 still closes the app.
                if (!HotkeyHelper.IsAssignable(binding) || KeyboardAcceleratorHelper.IsWindowsKeyDown ||
                    binding == new HotkeyBinding(VirtualKey.F4, VirtualKeyModifiers.Menu))
                {
                    return;
                }

                args.Handled = true;
                if (!args.KeyStatus.WasKeyDown)
                {
                    _recordedBinding = binding;
                    _suppressAltRelease = binding.Modifiers.HasFlag(VirtualKeyModifiers.Menu);
                    UpdateEditor();
                    AnnounceRecordedKeys();
                }

                break;

            case CoreAcceleratorKeyEventType.KeyUp or CoreAcceleratorKeyEventType.SystemKeyUp:
                if (key is VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu)
                {
                    // Releasing Alt after a combination would otherwise look like a lone Alt press.
                    args.Handled = _suppressAltRelease;
                    _suppressAltRelease = false;
                }
                else if (!HotkeyHelper.IsAssignable(new HotkeyBinding(key)))
                {
                    return;
                }
                else
                {
                    // The key box keeps the release too, so that Space or Enter do not press a button.
                    args.Handled = true;
                }

                break;
        }
    }

    private void AnnounceRecordedKeys()
    {
        if (FrameworkElementAutomationPeer.FromElement(_recordText) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
