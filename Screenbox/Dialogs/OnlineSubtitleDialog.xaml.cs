using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;
using Screenbox.Core.ViewModels;
using Screenbox.Helpers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Dialogs;

/// <summary>
/// Searches online subtitles for the playing media and loads the chosen subtitle into the player.
/// </summary>
public sealed partial class OnlineSubtitleDialog : ContentDialog
{
    internal OnlineSubtitleSearchViewModel ViewModel { get; }

    [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
    public OnlineSubtitleDialog()
    {
        ViewModel = Ioc.Default.GetRequiredService<OnlineSubtitleSearchViewModel>();
        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)Window.Current.Content).RequestedTheme;
        ViewModel.SubtitleLoaded += ViewModelOnSubtitleLoaded;
    }

    /// <summary>
    /// Shows the dialog for the media that is currently playing.
    /// </summary>
    public static async Task ShowForPlayingMediaAsync()
    {
        OnlineSubtitleDialog dialog = new();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Gets the name of a subtitle language in the display language of the app.
    /// </summary>
    /// <param name="code">The provider's language code, or an empty string for every language.</param>
    /// <param name="languageTag">The BCP-47 tag of the language.</param>
    /// <param name="englishName">The English name, used when Windows has no name for the language.</param>
    /// <returns>The language name.</returns>
    public static string GetLanguageName(string code, string languageTag, string englishName)
    {
        if (code.Length == 0)
        {
            return Strings.Resources.OnlineSubtitlesAllLanguages;
        }

        try
        {
            string displayName = new Windows.Globalization.Language(languageTag).DisplayName;
            return string.IsNullOrEmpty(displayName) || displayName == languageTag ? englishName : displayName;
        }
        catch (Exception)
        {
            // Windows rejects language tags that it does not recognize.
            return englishName;
        }
    }

    /// <summary>
    /// Describes a subtitle result below its file name.
    /// </summary>
    /// <returns>The language, download count, and whether the subtitle matches the file or is for the hearing impaired.</returns>
    public static string GetSubtitleDetails(string languageName, int downloadCount, bool isHashMatch, bool isHearingImpaired)
    {
        List<string> details = [languageName, Strings.Resources.OnlineSubtitlesDownloadCount(downloadCount)];
        if (isHashMatch)
        {
            details.Add(Strings.Resources.OnlineSubtitlesHashMatch);
        }

        if (isHearingImpaired)
        {
            details.Add(Strings.Resources.OnlineSubtitlesHearingImpaired);
        }

        return string.Join(" · ", details.Where(detail => detail.Length > 0));
    }

    // The bindings below call these helpers through the dialog instance, so they cannot be static.
    private bool IsBusy(OnlineSubtitleSearchState state) =>
        state is OnlineSubtitleSearchState.Searching or OnlineSubtitleSearchState.Downloading;

    private bool CanDownload(OnlineSubtitle? subtitle, OnlineSubtitleSearchState state) =>
        subtitle is not null && !IsBusy(state);

    private string GetEmptyResultsText(OnlineSubtitleSearchState state)
    {
        return state switch
        {
            OnlineSubtitleSearchState.NoResults => Strings.Resources.OnlineSubtitlesNoResults,
            OnlineSubtitleSearchState.SearchFailed => Strings.Resources.OnlineSubtitlesSearchFailed,
            OnlineSubtitleSearchState.NoMedia => Strings.Resources.OnlineSubtitlesNoMedia,
            _ => string.Empty
        };
    }

    private string GetErrorText(OnlineSubtitleSearchState state)
    {
        return state is OnlineSubtitleSearchState.DownloadFailed
            ? Strings.Resources.OnlineSubtitlesDownloadFailed
            : string.Empty;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        await ViewModel.InitializeAsync();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open while the subtitle downloads. It closes once the subtitle has been loaded.
        args.Cancel = true;
        await ViewModel.DownloadCommand.ExecuteAsync(null);
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Stop requests whose results nobody is waiting for anymore.
        ViewModel.SearchCommand.Cancel();
        ViewModel.DownloadCommand.Cancel();
        ViewModel.SubtitleLoaded -= ViewModelOnSubtitleLoaded;
    }

    private void ViewModelOnSubtitleLoaded(object? sender, EventArgs e)
    {
        Hide();
    }

    private void SearchBox_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        if (ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private async void ResultsListView_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await DownloadSelectedSubtitleAsync();
    }

    private async void ResultsListView_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await DownloadSelectedSubtitleAsync();
    }

    private async Task DownloadSelectedSubtitleAsync()
    {
        if (ViewModel.DownloadCommand.CanExecute(null))
        {
            await ViewModel.DownloadCommand.ExecuteAsync(null);
        }
    }
}
