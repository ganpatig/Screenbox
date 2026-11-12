using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Screenbox.Controls;

public sealed partial class VolumeControl : UserControl
{
    /// <summary>
    /// Identifies the <see cref="VolumeToggleButtonStyle"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty VolumeToggleButtonStyleProperty = DependencyProperty.Register(
        nameof(VolumeToggleButtonStyle),
        typeof(Style),
        typeof(VolumeControl),
        new PropertyMetadata(null, OnVolumeToggleButtonStylePropertyChanged));

    /// <summary>
    /// Gets or sets the Style that defines the look of the volume toggle button.
    /// </summary>
    public Style? VolumeToggleButtonStyle
    {
        [DynamicWindowsRuntimeCast(typeof(Style))]
        get { return (Style?)GetValue(VolumeToggleButtonStyleProperty); }
        set { SetValue(VolumeToggleButtonStyleProperty, value); }
    }

    public static readonly DependencyProperty ShowValueTextProperty = DependencyProperty.Register(
        nameof(ShowValueText), typeof(bool), typeof(VolumeControl), new PropertyMetadata(true));

    public bool ShowValueText
    {
        get { return (bool)GetValue(ShowValueTextProperty); }
        set { SetValue(ShowValueTextProperty, value); }
    }

    internal VolumeViewModel ViewModel => (VolumeViewModel)DataContext;

    public VolumeControl()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<VolumeViewModel>();
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    [DynamicWindowsRuntimeCast(typeof(Style))]
    private static void OnVolumeToggleButtonStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VolumeControl control && control.VolumeToggleButton != null)
        {
            control.VolumeToggleButton.Style = (Style?)e.NewValue;
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VolumeViewModel.MaxVolume))
        {
            UpdateIndicatorBoostWidth();
        }
    }

    [DynamicWindowsRuntimeCast(typeof(UIElement))]
    private void VolumeSlider_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint((UIElement)sender);
        int mouseWheelDelta = pointer.Properties.MouseWheelDelta;
        int volumeChange = mouseWheelDelta > 0 ? 5 : -5;
        ViewModel.SetVolume(volumeChange, true);
    }

    private void VolumeControl_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateIndicatorBoostWidth();
    }

    /// <summary>
    /// Sizes the part of the slider track that marks the volume above 100%.
    /// </summary>
    /// <remarks>
    /// The indicator shares its grid cell with the slider, so the width given to it is part of the width this
    /// control asks for. Measuring it from the width the slider was just given therefore feeds layout back into
    /// itself: with a maximum volume above 100% the two settle a fraction of a pixel apart and keep swapping,
    /// until XAML abandons the layout pass and the app ends. Whole pixels, and ignoring a width that did not
    /// really change, keep that from starting.
    /// </remarks>
    private void UpdateIndicatorBoostWidth()
    {
        int maxVolume = ViewModel.MaxVolume;
        double width = maxVolume < 100
            ? 0
            : Math.Round(VolumeSlider.ActualWidth * (maxVolume - 100) / maxVolume);

        if (BoostIndicator.Width == width) return;
        BoostIndicator.Width = width;
    }
}
