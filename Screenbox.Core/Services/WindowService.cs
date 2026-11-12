using System;
using System.Threading.Tasks;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;

namespace Screenbox.Core.Services;

public sealed class WindowService : IWindowService
{
    /// <summary>
    /// The largest compact overlay window that Windows allows, in effective pixels. The system limits such a
    /// window further, to half of the screen in each direction.
    /// </summary>
    private const double MaxCompactOverlayLength = 500;

    /// <summary>
    /// The smallest compact overlay window that Windows allows, in effective pixels.
    /// </summary>
    private const double MinCompactOverlayLength = 150;

    public WindowViewMode ViewMode => _windowContext.ViewMode;

    private readonly WindowContext _windowContext;
    private readonly ISettingsService _settingsService;
    private CoreCursor? _cursor;
    private bool _isLeavingCompactOverlay;

    public WindowService(WindowContext windowContext, ISettingsService settingsService)
    {
        _windowContext = windowContext;
        _settingsService = settingsService;
        Window.Current.SizeChanged += OnWindowSizeChanged;
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        ApplicationView view = ApplicationView.GetForCurrentView();
        if (_windowContext.ViewMode == WindowViewMode.FullScreen && !view.IsFullScreenMode)
            _windowContext.ViewMode = WindowViewMode.Default;

        // Dragging the border of the compact overlay window changes its size, which is the size to reopen it at.
        if (_windowContext.ViewMode == WindowViewMode.Compact && !_isLeavingCompactOverlay)
        {
            RememberCompactOverlaySize(e.Size);
        }
    }

    /// <summary>
    /// Remembers the size of the compact overlay window, so that it opens at that size the next time.
    /// </summary>
    /// <param name="size">The size of the window, in effective pixels.</param>
    private void RememberCompactOverlaySize(Size size)
    {
        Size workAreaSize = GetMaxWindowSize();
        if (size.Width < MinCompactOverlayLength || size.Height < MinCompactOverlayLength ||
            size.Width > workAreaSize.Width || size.Height > workAreaSize.Height)
        {
            return;
        }

        _settingsService.PlayerCompactOverlaySize = size;
    }

    public bool TryEnterFullScreen()
    {
        ApplicationView? view = ApplicationView.GetForCurrentView();
        if (view.TryEnterFullScreenMode())
        {
            _windowContext.ViewMode = WindowViewMode.FullScreen;
            return true;
        }

        return false;
    }

    public void ExitFullScreen()
    {
        ApplicationView? view = ApplicationView.GetForCurrentView();
        view?.ExitFullScreenMode();
        if (_windowContext.ViewMode == WindowViewMode.FullScreen)
            _windowContext.ViewMode = WindowViewMode.Default;
    }

    public async Task<bool> TryExitCompactLayoutAsync()
    {
        ApplicationView? view = ApplicationView.GetForCurrentView();

        // Remember how large the window was left, and ignore the size of the window that is restored.
        if (_windowContext.ViewMode == WindowViewMode.Compact)
        {
            Rect bounds = Window.Current.Bounds;
            RememberCompactOverlaySize(new Size(bounds.Width, bounds.Height));
            _isLeavingCompactOverlay = true;
        }

        try
        {
            if (await view.TryEnterViewModeAsync(ApplicationViewMode.Default))
            {
                if (_windowContext.ViewMode == WindowViewMode.Compact)
                    _windowContext.ViewMode = WindowViewMode.Default;
                return true;
            }
        }
        finally
        {
            _isLeavingCompactOverlay = false;
        }

        return false;
    }

    public async Task<bool> TryEnterCompactLayoutAsync(Size viewSize)
    {
        ApplicationView? view = ApplicationView.GetForCurrentView();
        ViewModePreferences? preferences = ViewModePreferences.CreateDefault(ApplicationViewMode.CompactOverlay);
        if (!viewSize.IsEmpty)
        {
            preferences.ViewSizePreference = ViewSizePreference.Custom;
            preferences.CustomSize = ClampToCompactOverlaySize(viewSize);
        }

        if (await view.TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay, preferences))
        {
            _windowContext.ViewMode = WindowViewMode.Compact;
            return true;
        }

        return false;
    }

    public Size GetMaxWindowSize()
    {
        DisplayInformation displayInformation = DisplayInformation.GetForCurrentView();
        ApplicationView view = ApplicationView.GetForCurrentView();
        return GetMaxWindowSize(view, displayInformation);
    }

    /// <inheritdoc/>
    public Size GetMaxCompactOverlaySize()
    {
        return GetMaxCompactOverlaySize(DisplayInformation.GetForCurrentView());
    }

    /// <summary>
    /// Fits a size into a window that can be shown, keeping its shape.
    /// </summary>
    /// <remarks>
    /// The size is only kept within the screen. Windows reduces a compact overlay window that is larger than
    /// it allows, so asking for more than that costs nothing and uses whatever a future release permits.
    /// </remarks>
    /// <param name="size">The wanted size, in effective pixels.</param>
    /// <returns>The size to ask for.</returns>
    private Size ClampToCompactOverlaySize(Size size)
    {
        Size maxSize = GetMaxWindowSize();
        if (size.Width <= 0 || size.Height <= 0)
        {
            return GetMaxCompactOverlaySize();
        }

        double aspectRatio = size.Width / size.Height;
        double width = Math.Clamp(size.Width, MinCompactOverlayLength, maxSize.Width);
        double height = width / aspectRatio;
        if (height > maxSize.Height)
        {
            height = maxSize.Height;
            width = Math.Min(maxSize.Width, height * aspectRatio);
        }
        else if (height < MinCompactOverlayLength)
        {
            height = MinCompactOverlayLength;
            width = Math.Min(maxSize.Width, height * aspectRatio);
        }

        return new Size(width, height);
    }

    private static Size GetMaxCompactOverlaySize(DisplayInformation displayInformation)
    {
        // Windows allows a compact overlay window of at most 500 effective pixels, and at most half the screen.
        double screenWidth = displayInformation.ScreenWidthInRawPixels / displayInformation.RawPixelsPerViewPixel;
        double screenHeight = displayInformation.ScreenHeightInRawPixels / displayInformation.RawPixelsPerViewPixel;
        return new Size(Math.Min(MaxCompactOverlayLength, screenWidth / 2),
            Math.Min(MaxCompactOverlayLength, screenHeight / 2));
    }

    private static Size GetMaxWindowSize(ApplicationView view, DisplayInformation displayInformation)
    {
        double maxWidth = displayInformation.ScreenWidthInRawPixels / displayInformation.RawPixelsPerViewPixel;
        double maxHeight = (displayInformation.ScreenHeightInRawPixels / displayInformation.RawPixelsPerViewPixel) - 48;
        if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
        {
            DisplayRegion? displayRegion = view.GetDisplayRegions()[0]; // Active display region
            maxWidth = displayRegion.WorkAreaSize.Width / displayInformation.RawPixelsPerViewPixel;
            maxHeight = displayRegion.WorkAreaSize.Height / displayInformation.RawPixelsPerViewPixel;
        }

        // Cannot use the full work area size. Subtract some padding.
        maxHeight -= 16;
        maxWidth -= 16;
        return new Size(maxWidth, maxHeight);
    }

    public double ResizeWindow(Size desiredSize, double scalar = 1)
    {
        if (scalar < 0 || desiredSize.IsEmpty) return -1;
        ApplicationView view = ApplicationView.GetForCurrentView();
        DisplayInformation displayInformation = DisplayInformation.GetForCurrentView();

        // The compact overlay window has its own, much smaller limits. Asking for more than the system allows
        // fails the resize outright, which would make the resize shortcuts do nothing in that mode.
        Size maxWindowSize = _windowContext.ViewMode == WindowViewMode.Compact
            ? GetMaxCompactOverlaySize(displayInformation)
            : GetMaxWindowSize(view, displayInformation);
        double maxWidth = maxWindowSize.Width;
        double maxHeight = maxWindowSize.Height;

        if (scalar == 0)
        {
            double widthRatio = maxWidth / desiredSize.Width;
            double heightRatio = maxHeight / desiredSize.Height;
            scalar = Math.Min(widthRatio, heightRatio);
        }

        double aspectRatio = desiredSize.Width / desiredSize.Height;
        double newWidth = desiredSize.Width * scalar;
        if (newWidth > maxWidth) newWidth = maxWidth;
        double newHeight = newWidth / aspectRatio;
        if (newHeight > maxHeight)
        {
            // A window that is taller than the screen, or than a compact overlay window may be, is refused.
            newHeight = maxHeight;
            newWidth = newHeight * aspectRatio;
        }

        scalar = newWidth / desiredSize.Width;
        if (view.TryResizeView(new Size(newWidth, newHeight)))
        {
            return scalar;
        }

        return -1;
    }

    public void HideCursor()
    {
        CoreWindow? coreWindow = Window.Current.CoreWindow;
        if (coreWindow.PointerCursor?.Type == CoreCursorType.Arrow)
        {
            _cursor = coreWindow.PointerCursor;
            coreWindow.PointerCursor = null;
        }
    }

    public void ShowCursor()
    {
        CoreWindow? coreWindow = Window.Current.CoreWindow;
        coreWindow.PointerCursor ??= _cursor;
    }
}

