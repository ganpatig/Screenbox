using System;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using Screenbox.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Dialogs;

/// <summary>
/// Asks for a time to move playback to, like the Go to Time dialog of VLC.
/// </summary>
public sealed partial class JumpToTimeDialog : ContentDialog
{
    /// <summary>
    /// Gets or sets the duration of the media, or <see cref="TimeSpan.Zero"/> if it is unknown.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets the text that shows the duration of the media.
    /// </summary>
    internal string LengthText => Duration > TimeSpan.Zero
        ? Strings.Resources.GoToTimeLength(VlcPlaybackHelper.FormatTime(Duration))
        : string.Empty;

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public JumpToTimeDialog()
    {
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)Window.Current.Content).RequestedTheme;
    }

    /// <summary>
    /// Shows the dialog, starting at the current playback position.
    /// </summary>
    /// <param name="position">The current playback position.</param>
    /// <param name="duration">The duration of the media, or <see cref="TimeSpan.Zero"/> if it is unknown.</param>
    /// <returns>The time to move to, or <see langword="null"/> if the dialog was canceled.</returns>
    public static async Task<TimeSpan?> GetTimeAsync(TimeSpan position, TimeSpan duration)
    {
        JumpToTimeDialog dialog = new() { Duration = duration };
        dialog.Bindings.Update();
        dialog.TimeBox.Text = VlcPlaybackHelper.FormatTime(position);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && dialog.TryGetTime(dialog.TimeBox.Text, out TimeSpan time)
            ? time
            : null;
    }

    private bool CanGo(string text)
    {
        return TryGetTime(text, out _);
    }

    private bool TryGetTime(string text, out TimeSpan time)
    {
        // A time past the end of the media is invalid, as it is in VLC.
        return VlcPlaybackHelper.TryParseTime(text, out time) && (Duration <= TimeSpan.Zero || time <= Duration);
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Select the current time so that typing replaces it.
        TimeBox.Focus(FocusState.Programmatic);
        TimeBox.SelectAll();
    }
}
