using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI;
using Screenbox.Commands;
using Screenbox.Controls;
using Screenbox.Converters;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.ViewModels;
using Screenbox.Dialogs;
using Screenbox.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Screenbox.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PlayerPage : Page
{
    internal PlayerPageViewModel ViewModel => (PlayerPageViewModel)DataContext;

    private static readonly bool _isArabicLanguage = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "ar";

    private readonly DispatcherQueueTimer _delayFlyoutOpenTimer;
    private readonly ShowPropertiesCommand _showPropertiesCommand = new();
    private CancellationTokenSource? _animationCancellationTokenSource;
    private bool _startup;
    private bool _isAltShortcutPressed;
    private VirtualKey _playPauseKey;

    public PlayerPage()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<PlayerPageViewModel>();
        _delayFlyoutOpenTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();

        RegisterSeekBarPointerHandlers();
        UpdatePreviewType();

        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        AlbumArtImage.RegisterPropertyChangedCallback(ImageBrush.ImageSourceProperty, AlbumArtImageOnSourceChanged);
        LayoutRoot.ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>
    /// Handles a key event that belongs to the keyboard shortcuts of the player.
    /// </summary>
    /// <remarks>
    /// The main page passes every key event of the window to this method before the focused control receives it,
    /// so that the shortcuts work wherever the focus is, like they do in VLC. The keys of each action come from
    /// <see cref="PlayerPageViewModel.Hotkeys"/>, which the user can change.
    /// </remarks>
    /// <param name="args">The key event of the window.</param>
    /// <returns><see langword="true"/> if the key event was a shortcut and was handled; otherwise, <see langword="false"/>.</returns>
    public bool TryHandleKeyboardShortcut(AcceleratorKeyEventArgs args)
    {
        return args.EventType switch
        {
            CoreAcceleratorKeyEventType.KeyDown or CoreAcceleratorKeyEventType.SystemKeyDown => HandleKeyDown(args.VirtualKey, args.KeyStatus),
            CoreAcceleratorKeyEventType.KeyUp or CoreAcceleratorKeyEventType.SystemKeyUp => HandleKeyUp(args.VirtualKey),
            _ => false,
        };
    }

    private bool HandleKeyDown(VirtualKey key, CorePhysicalKeyStatus keyStatus)
    {
        HotkeyBinding binding = new(key, KeyboardAcceleratorHelper.GetModifiers(keyStatus));

        // Modifier keys alone are not shortcuts, and key combinations with the Windows key belong to the system.
        if (!HotkeyHelper.IsAssignable(binding) || KeyboardAcceleratorHelper.IsWindowsKeyDown)
        {
            return false;
        }

        bool isShortcut = ViewModel.Hotkeys.TryGetAction(binding, out PlayerHotkeyAction action);
        bool isArrowKey = key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down;
        bool isHandled = isShortcut && TryHandleShortcutAction(action, keyStatus.WasKeyDown, isArrowKey, key);
        if (isHandled && binding.Modifiers.HasFlag(VirtualKeyModifiers.Menu))
        {
            _isAltShortcutPressed = true;
        }

        return isHandled;
    }

    private bool HandleKeyUp(VirtualKey key)
    {
        if (_playPauseKey is not VirtualKey.None && key == _playPauseKey)
        {
            // Releasing the key plays or pauses, unless holding the key sped up playback.
            _playPauseKey = VirtualKey.None;
            ViewModel.HandlePlayPauseKeyUp();
            return true;
        }

        if (key is VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu)
        {
            // The page never saw the key that was pressed with Alt, so releasing Alt would
            // look like a lone Alt press and show the access keys. Hide that release from the page.
            bool wasAltShortcutPressed = _isAltShortcutPressed;
            _isAltShortcutPressed = false;
            return wasAltShortcutPressed;
        }

        return false;
    }

    private bool TryHandleShortcutAction(PlayerHotkeyAction action, bool isRepeat, bool isArrowKey, VirtualKey key)
    {
        if (!CanHandleShortcut(action, isArrowKey))
        {
            return false;
        }

        // Holding a key repeats adjustments, but not toggles or dialogs. The repeated key presses
        // are still handled, so that they do not reach the focused control either.
        if (isRepeat && !HotkeyHelper.IsRepeatable(action))
        {
            return true;
        }

        switch (action)
        {
            case PlayerHotkeyAction.PlayPause:
                // Playback toggles when the key is released, because holding the key plays at double speed instead.
                _playPauseKey = key;
                ViewModel.HandlePlayPauseKeyDown();
                break;
            case PlayerHotkeyAction.ToggleMute:
                VolumeViewModel volume = Ioc.Default.GetRequiredService<VolumeViewModel>();
                volume.IsMute = !volume.IsMute;
                break;
            case PlayerHotkeyAction.OpenFiles:
                Ioc.Default.GetRequiredService<CommonViewModel>().OpenFilesCommand.Execute(null);
                break;
            case PlayerHotkeyAction.ShowMediaInfo:
                _showPropertiesCommand.Execute(PlayerControls.ViewModel.PlayQueue.CurrentItem);
                break;
            case PlayerHotkeyAction.Cast:
                PlayerControls.ShowCastFlyout();
                break;
            case PlayerHotkeyAction.ExitFullscreen:
                ExitFullscreenOrHideControls();
                break;
            case PlayerHotkeyAction.ResizeWindowHalf or PlayerHotkeyAction.ResizeWindowOriginal or PlayerHotkeyAction.ResizeWindowOneAndHalf
                or PlayerHotkeyAction.ResizeWindowToScreen or PlayerHotkeyAction.GrowWindow or PlayerHotkeyAction.ShrinkWindow:
                Rect bounds = Window.Current.CoreWindow.Bounds;
                ViewModel.ResizeWindow(action, new Size(bounds.Width, bounds.Height));
                break;
            case PlayerHotkeyAction.ShowControls:
                ViewModel.ShowControls();
                break;
            case PlayerHotkeyAction.ShowContextMenu:
                VideoView.ContextFlyout?.ShowAt(PlayerControls,
                    new FlyoutShowOptions { Placement = GlobalizationHelper.MirrorWhenRightToLeft(FlyoutPlacementMode.TopEdgeAlignedRight) });
                break;
            case PlayerHotkeyAction.ShowPlayQueue:
                ShowPlayQueue();
                break;
            case PlayerHotkeyAction.JumpToTime:
                _ = ShowJumpToTimeDialogAsync();
                break;
            case PlayerHotkeyAction.ShowKeyboardShortcuts:
                _ = KeyboardShortcutsDialog.ShowShortcutsAsync();
                break;
            default:
                PlayerControls.ViewModel.ExecuteHotkey(action);
                break;
        }

        return true;
    }

    [DynamicWindowsRuntimeCast(typeof(TextBox))]
    [DynamicWindowsRuntimeCast(typeof(PasswordBox))]
    [DynamicWindowsRuntimeCast(typeof(RichEditBox))]
    [DynamicWindowsRuntimeCast(typeof(Slider))]
    [DynamicWindowsRuntimeCast(typeof(ContentDialog))]
    [DynamicWindowsRuntimeCast(typeof(DependencyObject))]
    private bool CanHandleShortcut(PlayerHotkeyAction action, bool isArrowKey)
    {
        object? focusedElement = FocusManager.GetFocusedElement();

        // Keys typed into a text field are text, not shortcuts.
        if (focusedElement is TextBox or PasswordBox or RichEditBox)
        {
            return false;
        }

        // Dialogs, menus and flyouts use the keys themselves. Shortcuts must not act behind them.
        foreach (Popup popup in VisualTreeHelper.GetOpenPopups(Window.Current))
        {
            if (popup.Child is ContentDialog ||
                (focusedElement is DependencyObject element && IsInVisualTree(element, popup.Child)))
            {
                return false;
            }
        }

        // A slider that is engaged for keyboard or gamepad input moves with the arrow keys.
        if (isArrowKey && focusedElement is Slider { IsFocusEngaged: true })
        {
            return false;
        }

        if (ViewModel.PlayerVisibility != PlayerVisibilityState.Visible && !HotkeyHelper.IsAvailableOutsidePlayer(action))
        {
            return false;
        }

        return !HotkeyHelper.RequiresMedia(action) || ViewModel.Media is not null;
    }

    private static bool IsInVisualTree(DependencyObject element, DependencyObject? root)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, root))
            {
                return true;
            }
        }

        return false;
    }

    private void ExitFullscreenOrHideControls()
    {
        if (ViewModel.ViewMode is WindowViewMode.Compact or WindowViewMode.FullScreen)
        {
            ViewModel.GoBack();
        }
        else
        {
            ViewModel.TryHideControls();
        }
    }

    private void ShowPlayQueue()
    {
        if (ViewModel.PlayerVisibility != PlayerVisibilityState.Visible)
        {
            ViewModel.ShowPlayQueuePage();
        }
        else if (ViewModel.ViewMode != WindowViewMode.Compact)
        {
            ViewModel.ControlsHidden = false;
            PlayQueueFlyout.ShowAt(PlayQueueButton,
                new FlyoutShowOptions { Placement = GlobalizationHelper.MirrorWhenRightToLeft(FlyoutPlacementMode.BottomEdgeAlignedLeft) });
        }
    }

    private async Task ShowJumpToTimeDialogAsync()
    {
        PlayerControlsViewModel controls = PlayerControls.ViewModel;
        try
        {
            TimeSpan? time = await JumpToTimeDialog.GetTimeAsync(controls.Position, controls.NaturalDuration);
            if (time is { } value)
            {
                controls.JumpToTime(value);
            }
        }
        catch (COMException)
        {
            // Only one content dialog can be open at a time, and another one is already showing.
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        UpdateBackgroundAcrylicOpacity(ActualTheme);

        // DO NOT SET CONTENT VISUAL STATE HERE
        // It will cause element theme to not propagate correctly
        // VisualStateManager.GoToState(this, "Video", false);

        if (e.Parameter is true)
        {
            LayoutRoot.Transitions.Clear();
            ViewModel.PlayerVisibility = PlayerVisibilityState.Visible;
            ViewModel.OnFileLaunched();
            _startup = true;
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (ViewModel.PlayerVisibility != PlayerVisibilityState.Visible)
        {
            base.OnKeyDown(e);
            return;
        }

        bool shouldHideControls = ViewModel is { ControlsHidden: false, ViewMode: WindowViewMode.Default };

        switch (e.Key)
        {
            case VirtualKey.GamepadY when ViewModel.ViewMode != WindowViewMode.Compact:
                ViewModel.ControlsHidden = false;
                PlayQueueFlyout.ShowAt(PlayQueueButton,
                    new FlyoutShowOptions { Placement = GlobalizationHelper.MirrorWhenRightToLeft(FlyoutPlacementMode.BottomEdgeAlignedLeft) });
                break;
            case VirtualKey.GamepadMenu:
                VideoView.ContextFlyout.ShowAt(PlayerControls,
                    new FlyoutShowOptions { Placement = GlobalizationHelper.MirrorWhenRightToLeft(FlyoutPlacementMode.TopEdgeAlignedRight) });
                break;
            case VirtualKey.GamepadB when shouldHideControls:
                ViewModel.TryHideControls(true);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
    }

    private void AlbumArtImageOnSourceChanged(DependencyObject sender, DependencyProperty dp)
    {
        PlayBackgroundArtChangeCrossFadeAnimation();
    }

    private void OnLoading(FrameworkElement sender, object args)
    {
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden)
            VisualStateManager.GoToState(this, "Hidden", false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (LayoutRoot.Transitions.Count != 0) return;
        PaneThemeTransition transition = new()
        {
            Edge = EdgeTransitionLocation.Bottom
        };

        LayoutRoot.Transitions.Add(transition);

        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible)
        {
            PlayerTitleBar.RefreshDragRegion();
            // Focus can fail if player is file activated
            // Controls are disabled by default until playback is ready
            PlayerControls.FocusFirstButton();
        }
    }

    private void BackgroundElementOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double overshoot = 200;   // extra space for animation
        double edgeLength = Math.Max(e.NewSize.Width, e.NewSize.Height) + overshoot;

        // Sizing a child from the size of its parent feeds layout back into itself, so an unchanged size
        // must not start another pass.
        if (Math.Abs(BackgroundArt.Width - edgeLength) < 0.5) return;

        BackgroundArt.Width = edgeLength;
        BackgroundArt.Height = edgeLength;
    }

    private void RegisterSeekBarPointerHandlers()
    {
        SeekBar? seekBar = PlayerControls.FindDescendant<SeekBar>();
        Guard.IsNotNull(seekBar, nameof(seekBar));
        seekBar.AddHandler(PointerPressedEvent, (PointerEventHandler)SeekBarPointerPressedOrEnteredEventHandler, true);
        seekBar.AddHandler(PointerReleasedEvent, (PointerEventHandler)SeekBarPointerReleasedEventHandler, true);
        seekBar.AddHandler(PointerCanceledEvent, (PointerEventHandler)SeekBarPointerReleasedEventHandler, true);
        seekBar.AddHandler(PointerEnteredEvent, (PointerEventHandler)SeekBarPointerPressedOrEnteredEventHandler, false);
        seekBar.AddHandler(PointerExitedEvent, (PointerEventHandler)SeekBarPointerExitedEventHandler, false);
    }

    private void SeekBarPointerPressedOrEnteredEventHandler(object s, PointerRoutedEventArgs e)
    {
        ViewModel.SeekBarPointerInteracting = true;
    }

    private void SeekBarPointerReleasedEventHandler(object s, PointerRoutedEventArgs e)
    {
        ViewModel.SeekBarPointerInteracting = false;
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible)
            PlayerControls.FocusFirstButton();
    }

    private void SeekBarPointerExitedEventHandler(object s, PointerRoutedEventArgs e)
    {
        ViewModel.SeekBarPointerInteracting = false;
    }

    private void OnLayoutVisualStateChanged(object _, VisualStateChangedEventArgs args)
    {
        bool expanding = args.OldState?.Name == nameof(MiniPlayer) || (args.OldState?.Name == nameof(Hidden) &&
            (args.NewState == null || args.NewState.Name == nameof(Normal)));

        bool collapsing = args.OldState?.Name == nameof(Normal) && args.NewState?.Name == nameof(MiniPlayer);

        if (expanding || collapsing) PlayerControls.FocusFirstButton();
        UpdateRootTheme();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerPageViewModel.ControlsHidden):
                VisualStateManager.GoToState(this, ViewModel.ControlsHidden ? "ControlsHidden" : "ControlsVisible", true);
                if (!ViewModel.ControlsHidden)
                {
                    PlayerControls.FocusFirstButton();
                }

                break;
            case nameof(PlayerPageViewModel.ViewMode):
                switch (ViewModel.ViewMode)
                {
                    case WindowViewMode.Default:
                        VisualStateManager.GoToState(this, "Normal", true);
                        break;
                    case WindowViewMode.Compact:
                        ViewModel.PlayerVisibility = PlayerVisibilityState.Visible;
                        VisualStateManager.GoToState(this, "CompactOverlay", true);
                        break;
                    case WindowViewMode.FullScreen:
                        ViewModel.PlayerVisibility = PlayerVisibilityState.Visible;
                        VisualStateManager.GoToState(this, "Fullscreen", true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                UpdateContentState();
                break;
            case nameof(PlayerPageViewModel.AudioOnly):
                UpdateContentState();
                UpdateRootTheme();
                UpdatePreviewType();
                break;
            case nameof(PlayerPageViewModel.PlayerVisibility):
                switch (ViewModel.PlayerVisibility)
                {
                    case PlayerVisibilityState.Visible:
                        VisualStateManager.GoToState(this, "NoPreview", true);
                        VisualStateManager.GoToState(this, "Normal", true);
                        PlayerTitleBar.RefreshDragRegion();
                        break;
                    case PlayerVisibilityState.Minimal:
                        VisualStateManager.GoToState(this, "MiniPlayer", true);
                        break;
                    case PlayerVisibilityState.Hidden:
                        VisualStateManager.GoToState(this, "Hidden", true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                UpdateContentState();
                UpdateRootTheme();
                UpdatePreviewType();
                UpdateMiniPlayerMargin();
                break;
            case nameof(PlayerPageViewModel.NavigationViewDisplayMode) when ViewModel.ViewMode == WindowViewMode.Default:
                UpdateMiniPlayerMargin();
                break;
            case nameof(PlayerPageViewModel.IsPlaying):
                if (ViewModel.IsPlaying)
                {
                    if (_startup)
                    {
                        // Only when the app is file activated
                        // Wait till playback starts then focus the player controls
                        _startup = false;
                        PlayerControls.FocusFirstButton();
                    }

                    BackgroundArtAnimation.Resume();
                }
                else
                {
                    BackgroundArtAnimation.Pause();
                }

                break;
            case nameof(PlayerPageViewModel.ShowVisualizer):
                if (ViewModel.ShowVisualizer)
                {
                    // Load the visualizer if not already
                    FindName(nameof(LivelyWallpaperPlayer));
                }

                UpdateContentState();
                break;
            case nameof(PlayerPageViewModel.ShouldClosePlayQueueFlyout) when ViewModel.ShouldClosePlayQueueFlyout:
                if (PlayQueueFlyout.IsOpen)
                {
                    PlayQueueFlyout.Hide();
                }

                ViewModel.ShouldClosePlayQueueFlyout = false;
                break;
        }
    }

    private async void PlayBackgroundArtChangeCrossFadeAnimation()
    {
        // AnimationSet does not throw exception on cancellation
        _animationCancellationTokenSource?.Cancel();
        if (BackgroundElement.Visibility == Visibility.Collapsed ||
        BackgroundArt.Visibility == Visibility.Collapsed)
        {
            BackgroundImage.Source = AlbumArtImage.ImageSource;
            return;
        }

        using CancellationTokenSource cts = _animationCancellationTokenSource = new CancellationTokenSource();
        if (ViewModel.Media == null)
        {
            await BackgroundArtFadeOutAnimation.StartAsync(cts.Token);
            BackgroundImage.Source = null;
        }
        else if (BackgroundImage.Source == null)
        {
            BackgroundImageNext.Visibility = Visibility.Collapsed;
            BackgroundImage.GetVisual().Opacity = 0;
            BackgroundImage.Source = AlbumArtImage.ImageSource;
            await BackgroundArtFadeInAnimation.StartAsync(cts.Token);
        }
        else
        {
            BackgroundImageNext.Visibility = Visibility.Visible;
            await BackgroundArtFadeOutAnimation.StartAsync(cts.Token);
            BackgroundImage.Source = AlbumArtImage.ImageSource;
            await BackgroundArtFadeInAnimation.StartAsync(cts.Token);
            BackgroundImageNext.Visibility = Visibility.Collapsed;
        }

        if (cts == _animationCancellationTokenSource)
            _animationCancellationTokenSource = null;
    }

    private void UpdateContentState()
    {
        var contentVisualStateName = ViewModel.AudioOnly
            ? (ViewModel is { ShowVisualizer: true, PlayerVisibility: PlayerVisibilityState.Visible, ViewMode: not WindowViewMode.Compact }
                ? "AudioWithVisualizer"
                : "AudioOnly")
            : "Video";
        VisualStateManager.GoToState(this, contentVisualStateName, true);

        // Update lively options button visibility
        // TODO: Use XAML visual state to encode this logic
        LivelyOptionsButton.Visibility = ViewModel.PlayerVisibility == PlayerVisibilityState.Visible &&
                                         ViewModel.ViewMode != WindowViewMode.Compact &&
                                         ViewModel.AudioOnly
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdatePreviewType()
    {
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible || ViewModel.ViewMode == WindowViewMode.Compact)
        {
            VisualStateManager.GoToState(this, "NoPreview", true);
        }
        else
        {
            VisualStateManager.GoToState(this, ViewModel.AudioOnly ? "AudioPreview" : "VideoPreview", true);
        }
    }

    private void UpdateMiniPlayerMargin()
    {
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Visible || ViewModel.ViewMode == WindowViewMode.Compact)
        {
            VisualStateManager.GoToState(this, "NoMargin", false);
        }
        else
        {
            switch (ViewModel.NavigationViewDisplayMode)
            {
                case NavigationViewDisplayMode.Minimal when ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden:
                    VisualStateManager.GoToState(this, "HiddenMinimalMargin", false);
                    break;
                case NavigationViewDisplayMode.Minimal:
                    VisualStateManager.GoToState(this, "MinimalMargin", false);
                    break;
                case NavigationViewDisplayMode.Compact when ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden:
                case NavigationViewDisplayMode.Expanded when ViewModel.PlayerVisibility == PlayerVisibilityState.Hidden:
                    VisualStateManager.GoToState(this, "HiddenNormalMargin", false);
                    break;
                case NavigationViewDisplayMode.Compact:
                case NavigationViewDisplayMode.Expanded:
                    VisualStateManager.GoToState(this, "NormalMargin", false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private void UpdateRootTheme()
    {
        LayoutRoot.RequestedTheme = !ViewModel.AudioOnly && ViewModel.PlayerVisibility == PlayerVisibilityState.Visible
            ? ElementTheme.Dark
            : ElementTheme.Default;
    }

    private void PlayQueueFlyout_OnOpening(object sender, object e)
    {
        // Delay load PlayQueueControl until flyout is opening
        // Save loading time when launching from file
        FindName(nameof(PlayQueue));
    }

    private async void PlayQueueFlyout_OnOpened(object sender, object e)
    {
        if (PlayQueue == null) return;
        await PlayQueue.SmoothScrollActiveItemIntoViewAsync();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateBackgroundAcrylicOpacity(ActualTheme);
    }

    private void UpdateBackgroundAcrylicOpacity(ElementTheme theme)
    {
        // Set in code due to XAML compiler not setting it in Release
        BackgroundAcrylicBrush.TintLuminosityOpacity = theme == ElementTheme.Light ? 0.5 : 0.4;
    }

    private void PlayQueueButton_OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        _delayFlyoutOpenTimer.Debounce(() => PlayQueueFlyout.ShowAt(PlayQueueButton), TimeSpan.FromMilliseconds(500));
    }

    private void PlayQueueButton_OnDragLeave(object sender, DragEventArgs e)
    {
        _delayFlyoutOpenTimer.Stop();
    }

    private void PlayerControlsBackground_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        PlayerControls.FocusFirstButton(FocusState.Pointer);
        e.Handled = true;
    }

    private async void VideoView_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        // Reset focus after manipulation
        // Must be queued in Dispatcher or risk losing focus right after
        await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
            () => PlayerControls.FocusFirstButton(FocusState.Programmatic));
    }

    private void ControlsVisibilityStates_OnCurrentStateChanged(object sender, VisualStateChangedEventArgs e)
    {
        if (e.NewState.Name == nameof(ControlsHidden))
        {
            // Handle Space key when the controls are not visible.
            // Also hide tooltip if there is any.
            HiddenButton.Focus(FocusState.Programmatic);
        }
    }

    private void VideoView_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.OnPlayerClick())
        {
            PlayerControls.FocusFirstButton();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Link;
        if (e.DragUIOverride != null) e.DragUIOverride.Caption = Strings.Resources.Play;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        await ViewModel.OnDropAsync(e.DataView);
    }

    private void LayoutRoot_OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // The keyboard play and pause key is a remappable shortcut. The gamepad X button keeps playing and pausing.
        if (e.OriginalKey is VirtualKey.GamepadX)
        {
            e.Handled = true;
            ViewModel.HandlePlayPauseKeyDown();
        }
    }

    private void LayoutRoot_OnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalKey is VirtualKey.GamepadX)
        {
            e.Handled = true;
            ViewModel.HandlePlayPauseKeyUp();
        }
    }

    private void VideoView_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        PlayerControls.ViewModel.ToggleFullscreenCommand.Execute(null);
    }

    [DynamicWindowsRuntimeCast(typeof(MediaPlaybackAutoRepeatMode))]
    private string GetMessageText(PlaybackCommandKind kind, object? value)
    {
        return kind switch
        {
            //PlaybackCommandKind.Rewind or PlaybackCommandKind.FastForward when value is TimeSpan time => Humanizer.ToDuration(time),
            // Use LRE/PDF marks to prevent RTL BiDi reordering, except for Arabic where it forces Western digit shaping.
            PlaybackCommandKind.Rewind or PlaybackCommandKind.FastForward when value is string timeStr
                => _isArabicLanguage ? timeStr : $"\u202A{timeStr}\u202C",
            PlaybackCommandKind.Volume when value is int volume => Strings.Resources.VolumeChangeStatusMessage(volume),
            // Use LRE/PDF marks to prevent RTL BiDi reordering, except for Arabic where it forces Western digit shaping.
            PlaybackCommandKind.RateUp or PlaybackCommandKind.RateDown when value is double rate
                => _isArabicLanguage ? $"{rate.ToString("0.##", CultureInfo.CurrentCulture)}×" : $"\u202A{rate.ToString("0.##", CultureInfo.CurrentCulture)}×\u202C",
            PlaybackCommandKind.AspectRatio when value is string ratio
                => Strings.Resources.AspectRatioStatusMessage(ItemLabelHelper.GetValueOrFallback(ResourceNameToResourceStringConverter.FromName(ratio), ratio)),
            PlaybackCommandKind.Scale when value is double scale => Strings.Resources.ScaleStatus($"{scale * 100:0.##}%"),
            PlaybackCommandKind.Subtitle when value is string label => Strings.Resources.SubtitleStatus(label),
            PlaybackCommandKind.Subtitle when value is PlayerOsdItem track => Strings.Resources.SubtitleStatus(GetItemLabel(track, isChapter: false)),
            PlaybackCommandKind.SubtitleOff => Strings.Resources.SubtitleStatus(Strings.Resources.None),
            PlaybackCommandKind.NextChapter or PlaybackCommandKind.PreviousChapter when value is PlayerOsdItem chapter
                => GetItemLabel(chapter, isChapter: true),
            PlaybackCommandKind.Loop when value is MediaPlaybackAutoRepeatMode repeatMode => Strings.Resources.RepeatMode((long)repeatMode),
            PlaybackCommandKind.Shuffle when value is bool isShuffled => Strings.Resources.ShuffleMode(isShuffled ? 1L : 0L),
            PlaybackCommandKind.AudioTrack when value is PlayerOsdItem track => Strings.Resources.AudioTrackStatus(GetItemLabel(track, isChapter: false)),
            PlaybackCommandKind.AudioDevice when value is string deviceName => Strings.Resources.AudioDeviceStatus(deviceName),
            PlaybackCommandKind.AudioDelay when value is double delay => Strings.Resources.AudioDelayStatus((int)Math.Round(delay)),
            PlaybackCommandKind.SubtitleDelay when value is double delay => Strings.Resources.SubtitleDelayStatus((int)Math.Round(delay)),
            PlaybackCommandKind.SubtitleDelay => Strings.Resources.NoActiveSubtitleStatus,
            PlaybackCommandKind.SubtitleSync when value is SubtitleSyncStatus status => GetSubtitleSyncText(status),
            PlaybackCommandKind.Crop when value is string crop
                => Strings.Resources.CropStatus(ItemLabelHelper.GetValueOrFallback(ResourceNameToResourceStringConverter.FromName(crop), crop)),
            PlaybackCommandKind.Zoom when value is double zoom => zoom > 0
                ? Strings.Resources.ZoomStatus($"{(zoom * 100).ToString("0.##", CultureInfo.CurrentCulture)}%")
                : Strings.Resources.ZoomToFitStatus,
            PlaybackCommandKind.Deinterlace when value is DeinterlaceStatus status => status switch
            {
                { IsEnabled: true } => Strings.Resources.DeinterlaceOnStatus(status.ModeLabel ?? string.Empty),
                { ModeLabel: { } modeLabel } => Strings.Resources.DeinterlaceOffModeStatus(modeLabel),
                _ => Strings.Resources.DeinterlaceOffStatus,
            },
            _ when value is string str => str,
            _ => string.Empty,
        };
    }

    private static string GetItemLabel(PlayerOsdItem item, bool isChapter)
    {
        if (!string.IsNullOrWhiteSpace(item.Label))
        {
            return item.Label;
        }

        return isChapter ? Strings.Resources.ChapterName(item.Number) : Strings.Resources.TrackIndex(item.Number);
    }

    private static string GetSubtitleSyncText(SubtitleSyncStatus status)
    {
        return status.Step switch
        {
            SubtitleSyncStep.AudioBookmarked => Strings.Resources.SubtitleSyncAudioBookmarkedStatus,
            SubtitleSyncStep.SubtitleBookmarked => Strings.Resources.SubtitleSyncSubtitleBookmarkedStatus,
            SubtitleSyncStep.MissingBookmarks => Strings.Resources.SubtitleSyncMissingBookmarksStatus,
            SubtitleSyncStep.Applied => Strings.Resources.SubtitleSyncAppliedStatus(status.CorrectionMilliseconds, status.TotalDelayMilliseconds),
            SubtitleSyncStep.Reset => Strings.Resources.SubtitleSyncResetStatus,
            _ => Strings.Resources.NoActiveSubtitleStatus,
        };
    }

    private HorizontalAlignment GetBadgeHorizontalAlignment(PlaybackCommandKind kind)
    {
        if (ViewModel.PlayerVisibility == PlayerVisibilityState.Minimal)
            return HorizontalAlignment.Center;

        return kind switch
        {
            PlaybackCommandKind.Rewind => GlobalizationHelper.IsRightToLeftLanguage ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            PlaybackCommandKind.FastForward => GlobalizationHelper.IsRightToLeftLanguage ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
    }

    private bool ShouldMirrorBadgeIcon(PlaybackCommandKind kind)
    {
        return kind is not (PlaybackCommandKind.RateUp
                or PlaybackCommandKind.RateDown
                or PlaybackCommandKind.Rewind
                or PlaybackCommandKind.FastForward);
    }
}
