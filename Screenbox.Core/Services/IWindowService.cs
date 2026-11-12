using System.Threading.Tasks;
using Screenbox.Core.Enums;
using Windows.Foundation;

namespace Screenbox.Core.Services;

public interface IWindowService
{
    public WindowViewMode ViewMode { get; }
    public bool TryEnterFullScreen();
    public void ExitFullScreen();
    public Task<bool> TryExitCompactLayoutAsync();
    public Task<bool> TryEnterCompactLayoutAsync(Size viewSize);
    Size GetMaxWindowSize();

    /// <summary>
    /// Gets the largest compact overlay ("picture in picture") window that the system allows.
    /// </summary>
    /// <returns>The maximum size, in effective pixels.</returns>
    Size GetMaxCompactOverlaySize();

    double ResizeWindow(Size desiredSize, double scalar = 1);
    void HideCursor();
    void ShowCursor();
}
