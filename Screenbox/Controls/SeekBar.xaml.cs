using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI;
using Screenbox.Core;
using Screenbox.Core.Models;
using Screenbox.Core.ViewModels;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Screenbox.Controls;

public sealed partial class SeekBar : UserControl
{
    public static readonly DependencyProperty ProgressOnlyProperty = DependencyProperty.Register(
        nameof(ProgressOnly),
        typeof(bool),
        typeof(SeekBar),
        new PropertyMetadata(false));

    public bool ProgressOnly
    {
        get => (bool)GetValue(ProgressOnlyProperty);
        set => SetValue(ProgressOnlyProperty, value);
    }

    internal SeekBarViewModel ViewModel => (SeekBarViewModel)DataContext;

    private const double PreviewThumbnailMaxWidth = 192;
    private const double PreviewThumbnailMaxHeight = 108;
    private static readonly Thickness TimePreviewPadding = new(8, 3, 8, 5);
    private static readonly Thickness ThumbnailPreviewPadding = new(4, 4, 4, 5);

    private readonly DispatcherQueueTimer _previewToolTipTimer;
    private Thumb? _seekBarThumb;

    private readonly ToolTip _previewToolTip;
    private readonly TextBlock _previewTimeText;
    private readonly Border _previewThumbnail;
    private readonly ImageBrush _previewThumbnailBrush;
    private WriteableBitmap? _previewThumbnailBitmap;

    public SeekBar()
    {
        this.InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SeekBarViewModel>();
        RegisterSeekBarPointerHandlers();
        DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _previewToolTipTimer = dispatcherQueue.CreateTimer();

        // An image brush is clipped by the corner radius, which a child Image element would not be.
        _previewThumbnailBrush = new ImageBrush { Stretch = Stretch.UniformToFill };
        _previewThumbnail = new Border
        {
            Background = _previewThumbnailBrush,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Visibility = Visibility.Collapsed
        };

        // The thumbnail is decorative. The time text already describes the previewed position.
        AutomationProperties.SetAccessibilityView(_previewThumbnail, AccessibilityView.Raw);
        _previewTimeText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };

        StackPanel previewContent = new();
        previewContent.Children.Add(_previewThumbnail);
        previewContent.Children.Add(_previewTimeText);
        _previewToolTip = new ToolTip
        {
            Content = previewContent,
            Padding = TimePreviewPadding,
            FontSize = 15,
            VerticalOffset = 13
        };

        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.ShouldShowPreview) when !ViewModel.ShouldShowPreview:
                ResetPreviewToolTip();
                break;
            case nameof(ViewModel.PreviewFrame):
                ShowPreviewFrame(ViewModel.PreviewFrame);
                break;
        }
    }

    private void RegisterSeekBarPointerHandlers()
    {
        SeekBarSlider.AddHandler(PointerPressedEvent, (PointerEventHandler)PointerPressedEventHandler, true);
        SeekBarSlider.AddHandler(PointerReleasedEvent, (PointerEventHandler)PointerReleasedEventHandler, true);
        SeekBarSlider.AddHandler(PointerCanceledEvent, (PointerEventHandler)PointerReleasedEventHandler, true);
        SeekBarSlider.AddHandler(PointerMovedEvent, (PointerEventHandler)PointerMovedEventHandler, true);
    }

    private void PointerReleasedEventHandler(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.OnSeekBarPointerEvent(false);
    }

    private void PointerPressedEventHandler(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.OnSeekBarPointerEvent(true);
    }

    private void PointerMovedEventHandler(object s, PointerRoutedEventArgs e)
    {
        PointerPoint pointerPoint = e.GetCurrentPoint(SeekBarSlider);
        UpdatePreviewTime(pointerPoint);
        double halfWidth = SeekBarSlider.ActualWidth / 2;
        _previewToolTip.Translation = new Vector3((float)(-halfWidth + pointerPoint.Position.X), 0, 0);
        if (!ViewModel.ShouldShowPreview || !_previewToolTip.IsOpen) return;
        _previewToolTipTimer.Stop();
        _previewToolTip.IsOpen = true;
    }

    [DynamicWindowsRuntimeCast(typeof(ToolTip))]
    private void SeekBarSlider_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (ToolTipService.GetToolTip(SeekBarSlider) is not ToolTip)
        {
            _previewToolTip.PlacementRect = new Rect(new Point(0, 0), SeekBarSlider.RenderSize);
            ToolTipService.SetToolTip(SeekBarSlider, _previewToolTip);
        }

        ViewModel.ShouldShowPreview = true;
        _previewToolTipTimer.Debounce(() => _previewToolTip.IsOpen = true, TimeSpan.FromMilliseconds(100));
    }

    private void SeekBarSlider_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.ShouldShowPreview = false;
        _previewToolTipTimer.Stop();
        _previewToolTip.IsOpen = false;
        ToolTipService.SetToolTip(SeekBarSlider, null);
    }

    private void SeekBarSlider_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Only while the preview is on screen. Placing a popup from a size change runs layout again, and the
        // seek bar changes size throughout the animation that opens the player.
        if (_previewToolTip.IsOpen)
        {
            _previewToolTip.PlacementRect = new Rect(new Point(0, 0), e.NewSize);
        }
    }

    private void SeekBarSlider_OnLoaded(object sender, RoutedEventArgs e)
    {
        _seekBarThumb = SeekBarSlider.FindDescendant<Thumb>();
        if (_seekBarThumb != null)
        {
            // When Thumb is pressed, it hijacks pointer events until it's released
            _seekBarThumb.PointerReleased += SeekBarThumb_PointerReleased;
        }
    }

    private void SeekBarThumb_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(SeekBarSlider).Position;
        bool inBound = position.X >= 0 && position.X <= SeekBarSlider.ActualWidth &&
                       position.Y >= 0 && position.Y <= SeekBarSlider.ActualHeight;
        if (!inBound)
        {
            SeekBarSlider_OnPointerExited(sender, e);
        }
    }

    [DynamicWindowsRuntimeCast(typeof(UIElement))]
    private void SeekBarSlider_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (e.IsGenerated) return;
        PointerPoint pointer = e.GetCurrentPoint((UIElement)sender);
        int mouseWheelDelta = pointer.Properties.MouseWheelDelta;
        ViewModel.OnSeekBarPointerWheelChanged(mouseWheelDelta, e.KeyModifiers);
    }

    private void ResetPreviewToolTip()
    {
        _previewToolTipTimer.Stop();
        _previewToolTip.IsOpen = false;
    }

    private void UpdatePreviewTime(PointerPoint pointerPoint)
    {
        double pointerOffset = pointerPoint.Position.X;
        double pointerOffsetRelative = pointerOffset / SeekBarSlider.ActualWidth; // have not accounted for padding
        double thumbOffset = 0;
        if (_seekBarThumb != null)
        {
            double thumbWidth = _seekBarThumb.ActualWidth;
            thumbOffset = thumbWidth * (pointerOffsetRelative - 0.5);
        }

        double normalizedPosition = (pointerOffset + thumbOffset) / SeekBarSlider.ActualWidth;
        ViewModel.UpdatePreviewTime(normalizedPosition);
        _previewTimeText.Text = Humanizer.ToDuration(ViewModel.PreviewTime);
    }

    /// <summary>
    /// Shows a decoded frame above the preview time, or hides the thumbnail when there is no frame.
    /// </summary>
    /// <remarks>
    /// Frames are written into a reused <see cref="WriteableBitmap"/>. XAML keeps rendering from the image it was
    /// given, so releasing a per-frame bitmap after handing it over makes XAML fail later with RO_E_CLOSED.
    /// </remarks>
    /// <param name="frame">The frame to show, or <see langword="null"/> to hide the thumbnail.</param>
    private void ShowPreviewFrame(SeekPreviewFrame? frame)
    {
        if (frame is null)
        {
            HidePreviewThumbnail();
            return;
        }

        try
        {
            if (_previewThumbnailBitmap is null
                || _previewThumbnailBitmap.PixelWidth != frame.Width
                || _previewThumbnailBitmap.PixelHeight != frame.Height)
            {
                _previewThumbnailBitmap = new WriteableBitmap(frame.Width, frame.Height);
                _previewThumbnailBrush.ImageSource = _previewThumbnailBitmap;
            }

            frame.Pixels.CopyTo(_previewThumbnailBitmap.PixelBuffer);
            _previewThumbnailBitmap.Invalidate();
        }
        catch (Exception)
        {
            // A missing thumbnail must never interrupt playback, so fall back to showing only the time.
            HidePreviewThumbnail();
            return;
        }

        double aspectRatio = (double)frame.Width / frame.Height;
        double thumbnailWidth = Math.Min(PreviewThumbnailMaxWidth, PreviewThumbnailMaxHeight * aspectRatio);
        _previewThumbnail.Width = thumbnailWidth;
        _previewThumbnail.Height = thumbnailWidth / aspectRatio;
        _previewThumbnail.Visibility = Visibility.Visible;
        _previewToolTip.Padding = ThumbnailPreviewPadding;
    }

    /// <summary>
    /// Collapses the preview thumbnail so that the preview shows only the time.
    /// </summary>
    private void HidePreviewThumbnail()
    {
        _previewThumbnail.Visibility = Visibility.Collapsed;
        _previewToolTip.Padding = TimePreviewPadding;
    }
}
