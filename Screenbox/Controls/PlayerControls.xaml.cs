using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;
using Screenbox.Core.ViewModels;
using Screenbox.Dialogs;
using Screenbox.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Screenbox.Controls;

public sealed partial class PlayerControls : UserControl
{
    public static readonly DependencyProperty BackgroundTransitionProperty = DependencyProperty.Register(
        nameof(BackgroundTransition),
        typeof(BrushTransition),
        typeof(PlayerControls),
        new PropertyMetadata(null));

    public BrushTransition BackgroundTransition
    {
        [DynamicWindowsRuntimeCast(typeof(BrushTransition))]
        get => (BrushTransition)GetValue(BackgroundTransitionProperty);
        set => SetValue(BackgroundTransitionProperty, value);
    }

    public MenuFlyout? PlayerContextMenu
    {
        [DynamicWindowsRuntimeCast(typeof(MenuFlyout))]
        get => (MenuFlyout?)MoreButton.Flyout;
    }

    internal PlayerControlsViewModel ViewModel => (PlayerControlsViewModel)DataContext;

    internal CommonViewModel Common { get; }

    private Flyout? _castFlyout;

    public PlayerControls()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<PlayerControlsViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();
        AudioTrackSubtitlePicker.ShowSubtitleOptionsCommand = new RelayCommand(ShowSubtitleOptions);
        AudioTrackSubtitlePicker.ShowAudioOptionsCommand = new RelayCommand(ShowAudioOptions);
        AudioTrackSubtitlePicker.SearchOnlineSubtitlesCommand = new AsyncRelayCommand(SearchOnlineSubtitlesAsync);
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keyboard shortcuts change the speed and the aspect ratio without the menu, so keep its checked items in sync.
        switch (e.PropertyName)
        {
            case nameof(PlayerControlsViewModel.PlaybackRate):
                SelectAlternatePlaybackSpeedItem(ViewModel.PlaybackRate);
                break;
            case nameof(PlayerControlsViewModel.AspectRatio):
                SelectAspectRatioItem(ViewModel.AspectRatio);
                break;
            case nameof(PlayerControlsViewModel.SubtitleTimingOffset):
                SyncTimingOffsetSlider(SubtitleTimingOffsetSlider, ViewModel.SubtitleTimingOffset);
                break;
            case nameof(PlayerControlsViewModel.AudioTimingOffset):
                SyncTimingOffsetSlider(AudioTimingOffsetSlider, ViewModel.AudioTimingOffset);
                break;
        }
    }

    /// <summary>
    /// Moves a timing offset slider to an offset, widening its range when the offset is outside it.
    /// </summary>
    /// <remarks>
    /// The keyboard shortcuts can set offsets beyond the usual range of 3 seconds. A slider clamps a value outside
    /// its range and its two-way binding writes the clamped value back, so the range has to include the offset
    /// before the binding updates the slider. This handler runs before the binding, because the binding subscribes
    /// to the view model later, when the flyout is first opened.
    /// </remarks>
    /// <param name="slider">The slider, or <see langword="null"/> if its flyout has never been opened.</param>
    /// <param name="offset">The timing offset, in milliseconds.</param>
    private static void SyncTimingOffsetSlider(Slider? slider, double offset)
    {
        if (slider is null) return;

        const double DefaultLimit = 3000;
        double limit = Math.Max(DefaultLimit, Math.Ceiling(Math.Abs(offset)));

        // Widen the range first, then move the slider, then narrow the range. In any other order,
        // a narrower range could clamp the old value and write it back over the new offset.
        slider.Maximum = Math.Max(slider.Maximum, limit);
        slider.Minimum = Math.Min(slider.Minimum, -limit);
        slider.Value = offset;
        slider.Maximum = limit;
        slider.Minimum = -limit;
    }

    /// <summary>
    /// Closes the track picker and opens the online subtitle search for the playing media.
    /// </summary>
    private async Task SearchOnlineSubtitlesAsync()
    {
        AudioSubtitlePickerFlyout.Hide();
        try
        {
            await OnlineSubtitleDialog.ShowForPlayingMediaAsync();
        }
        catch (COMException)
        {
            // Only one content dialog can be open at a time, and another one is already showing.
        }
    }

    [DynamicWindowsRuntimeCast(typeof(Flyout))]
    private void ShowSubtitleOptions()
    {
        AudioSubtitlePickerFlyout.Hide();
        Flyout flyout = (Flyout)Resources["SubtitleOptionsFlyout"];

        // Looking up the flyout creates the slider on first use. Fit its range to the offset before it shows.
        SyncTimingOffsetSlider(SubtitleTimingOffsetSlider, ViewModel.SubtitleTimingOffset);
        flyout.ShowAt(AudioAndCaptionButton);
    }

    [DynamicWindowsRuntimeCast(typeof(Flyout))]
    private void ShowAudioOptions()
    {
        AudioSubtitlePickerFlyout.Hide();
        Flyout flyout = (Flyout)Resources["AudioOptionsFlyout"];
        SyncTimingOffsetSlider(AudioTimingOffsetSlider, ViewModel.AudioTimingOffset);
        flyout.ShowAt(AudioAndCaptionButton);
    }

    public void FocusFirstButton(FocusState value = FocusState.Programmatic)
    {
        PlayPauseButton.Focus(value);
    }

    private void CastMenuFlyoutItem_OnClick(object sender, RoutedEventArgs e)
    {
        _castFlyout ??= CastControl.GetFlyout();
        _castFlyout.ShowAt(MoreButton, new FlyoutShowOptions { Placement = GlobalizationHelper.MirrorWhenRightToLeft(FlyoutPlacementMode.TopEdgeAlignedRight) });
    }

    [DynamicWindowsRuntimeCast(typeof(Flyout))]
    private void CustomSpeedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Flyout customSpeedFlyout = (Flyout)Resources["CustomPlaybackSpeedFlyout"];
        customSpeedFlyout.ShowAt(MoreButton);

        // The speed keys go beyond the range of the slider. Setting such a speed on the slider
        // would clamp it and slow playback down, so the slider is left as it is.
        if (ViewModel.PlaybackRate > SpeedSlider.Maximum)
        {
            SelectAlternatePlaybackSpeedItem(ViewModel.PlaybackRate);
        }
        else if (SpeedSlider.Value != ViewModel.PlaybackRate)
        {
            SpeedSlider.Value = ViewModel.PlaybackRate;
        }
        else
        {
            SelectAlternatePlaybackSpeedItem(ViewModel.PlaybackRate);
        }
    }

    [DynamicWindowsRuntimeCast(typeof(Flyout))]
    private void CustomAspectRatioMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Flyout customAspectFlyout = (Flyout)Resources["CustomAspectRatioFlyout"];
        customAspectFlyout.ShowAt(MoreButton);
    }

    private void SpeedSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        double newValue = Math.Max(e.NewValue, 0.05);
        if (Math.Abs(SpeedSlider.Value - newValue) > 0.0001)
        {
            SpeedSlider.Value = newValue;
        }

        ViewModel.SetPlaybackRateCommand.Execute(newValue);
        SelectAlternatePlaybackSpeedItem(newValue);
    }

    [DynamicWindowsRuntimeCast(typeof(RadioMenuFlyoutItem))]
    private void SelectAlternatePlaybackSpeedItem(double playbackSpeed)
    {
        bool isMenuValue = (int)(playbackSpeed * 100) % 25 == 0;
        if (isMenuValue &&
            PlaybackSpeedSubMenu.Items?.FirstOrDefault(x =>
                    x.Tag is double predefinedSpeed && Math.Abs(predefinedSpeed - playbackSpeed) < 0.0001) is
                RadioMenuFlyoutItem matchItem)
        {
            matchItem.IsChecked = true;
        }
        else
        {
            CustomPlaybackSpeedMenuItem.IsChecked = true;
        }
    }

    private bool IsCastButtonEnabled(bool hasActiveItem)
    {
        if (_castFlyout?.Content is CastControl control)
        {
            return control.ViewModel.IsCasting || hasActiveItem;
        }

        return hasActiveItem;
    }

    [DynamicWindowsRuntimeCast(typeof(RadioMenuFlyoutItem))]
    private void AspectRatioTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        string aspectRatio = AspectRatioTextBox.Text;
        if (!aspectRatio.Contains(':')) return;
        if (AspectRatioSubMenu.Items?.FirstOrDefault(x => (string)x.Tag == aspectRatio) is RadioMenuFlyoutItem
            matchItem)
        {
            matchItem.IsChecked = true;
            matchItem.Command?.Execute(matchItem.CommandParameter);
        }
        else
        {
            CustomAspectRatioMenuItem.IsChecked = true;
            ViewModel.SetAspectRatioCommand.Execute(aspectRatio);
        }
    }

    [DynamicWindowsRuntimeCast(typeof(RadioMenuFlyoutItem))]
    private void SelectAspectRatioItem(string aspectRatio)
    {
        if (AspectRatioSubMenu.Items?.FirstOrDefault(x => x is RadioMenuFlyoutItem { CommandParameter: string option } && option == aspectRatio) is
            RadioMenuFlyoutItem matchItem)
        {
            matchItem.IsChecked = true;
        }
        else
        {
            CustomAspectRatioMenuItem.IsChecked = true;
        }
    }

    private void NormalPlayerContextMenu_OnOpening(object sender, object e)
    {
        // The menu items have no keyboard accelerators, because the player page handles the shortcuts
        // that the user can change. Show the current keys next to the items instead.
        HotkeyMap hotkeys = ViewModel.Hotkeys;
        OpenFilesMenuItem.KeyboardAcceleratorTextOverride = GetFirstKeyText(hotkeys, PlayerHotkeyAction.OpenFiles);
        PropertiesMenuItem.KeyboardAcceleratorTextOverride = GetFirstKeyText(hotkeys, PlayerHotkeyAction.ShowMediaInfo);
        SaveFrameMenuItem.KeyboardAcceleratorTextOverride = GetFirstKeyText(hotkeys, PlayerHotkeyAction.SaveFrame);
        CastMenuItem.KeyboardAcceleratorTextOverride = GetFirstKeyText(hotkeys, PlayerHotkeyAction.Cast);
        CompactOverlayMenuItem.KeyboardAcceleratorTextOverride = GetFirstKeyText(hotkeys, PlayerHotkeyAction.ToggleCompactOverlay);
        FullscreenMenuItem.KeyboardAcceleratorTextOverride = GetFirstKeyText(hotkeys, PlayerHotkeyAction.ToggleFullscreen);
    }

    private static string GetFirstKeyText(HotkeyMap hotkeys, PlayerHotkeyAction action)
    {
        IReadOnlyList<HotkeyBinding> bindings = hotkeys.GetBindings(action);
        return bindings.Count > 0 ? bindings[0].ToDisplayName() : string.Empty;
    }

    /// <summary>
    /// Shows the list of devices to cast the playing media to.
    /// </summary>
    internal void ShowCastFlyout()
    {
        CastMenuFlyoutItem_OnClick(this, new RoutedEventArgs());
    }

    private Visibility GetChapterVisibility(bool isEnabled, int count)
    {
        return isEnabled && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
