using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;

namespace Screenbox.Core.Tests.Helpers;

public class TrackPreferenceHelperTests
{
    [Test]
    public async Task CreateChoice_ShouldDescribeSelectedTrack()
    {
        SubtitleTrack[] tracks = CreateTracks(("en", "English"), ("it", "Italiano"));

        MediaTrackChoice choice = TrackPreferenceHelper.CreateChoice(tracks, 1);

        await Assert.That(choice.Index).IsEqualTo(1);
        await Assert.That(choice.LanguageTag).IsEqualTo("it");
        await Assert.That(choice.Label).IsEqualTo("Italiano");
        await Assert.That(choice.IsDisabled).IsFalse();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(2)]
    public async Task CreateChoice_ShouldReturnDisabledWhenNothingIsSelected(int selectedIndex)
    {
        SubtitleTrack[] tracks = CreateTracks(("en", "English"), ("it", "Italiano"));

        MediaTrackChoice choice = TrackPreferenceHelper.CreateChoice(tracks, selectedIndex);

        await Assert.That(choice.IsDisabled).IsTrue();
    }

    [Test]
    public async Task FindTrack_ShouldMatchLanguageAndLabel()
    {
        SubtitleTrack[] tracks = CreateTracks(("en", "English"), ("en", "English SDH"), ("it", "Italiano"));

        int index = TrackPreferenceHelper.FindTrack(tracks, new MediaTrackChoice(0, "en", "English SDH"));

        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task FindTrack_ShouldMatchLanguageWhenLabelChanged()
    {
        SubtitleTrack[] tracks = CreateTracks(("en", "English"), ("it", "Italiano"));

        int index = TrackPreferenceHelper.FindTrack(tracks, new MediaTrackChoice(0, "it", "Italian forced"));

        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task FindTrack_ShouldMatchLabelOfExternalSubtitle()
    {
        SubtitleTrack[] tracks = CreateTracks(("en", "English"), ("", "Movie.it.srt"));

        int index = TrackPreferenceHelper.FindTrack(tracks, new MediaTrackChoice(5, string.Empty, "movie.it.srt"));

        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task FindTrack_ShouldFallBackToPositionForUnnamedTrack()
    {
        SubtitleTrack[] tracks = CreateTracks(("", ""), ("", ""));

        int index = TrackPreferenceHelper.FindTrack(tracks, new MediaTrackChoice(1, string.Empty, string.Empty));

        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task FindTrack_ShouldReturnNegativeWhenNothingMatches()
    {
        SubtitleTrack[] tracks = CreateTracks(("en", "English"));

        await Assert.That(TrackPreferenceHelper.FindTrack(tracks, new MediaTrackChoice(0, "ja", "日本語"))).IsEqualTo(-1);
        await Assert.That(TrackPreferenceHelper.FindTrack(tracks, MediaTrackChoice.Disabled)).IsEqualTo(-1);
        await Assert.That(TrackPreferenceHelper.FindTrack(tracks, null)).IsEqualTo(-1);
    }

    [Test]
    public async Task FindPreferredLanguage_ShouldIgnoreRegion()
    {
        SubtitleTrack[] tracks = CreateTracks(("it", "Italiano"), ("en-US", "English"));

        int index = TrackPreferenceHelper.FindPreferredLanguage(tracks, "en");

        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task FindPreferredLanguage_ShouldFollowTheOrderOfTheList()
    {
        SubtitleTrack[] tracks = CreateTracks(("it", "Italiano"), ("fr", "Français"));

        await Assert.That(TrackPreferenceHelper.FindPreferredLanguage(tracks, "en, fr")).IsEqualTo(1);
        await Assert.That(TrackPreferenceHelper.FindPreferredLanguage(tracks, "en")).IsEqualTo(-1);
        await Assert.That(TrackPreferenceHelper.FindPreferredLanguage(tracks, string.Empty)).IsEqualTo(-1);
        await Assert.That(TrackPreferenceHelper.FindPreferredLanguage(tracks, null)).IsEqualTo(-1);
    }

    private static SubtitleTrack[] CreateTracks(params (string Language, string Label)[] tracks)
    {
        SubtitleTrack[] result = new SubtitleTrack[tracks.Length];
        for (int i = 0; i < tracks.Length; i++)
        {
            result[i] = new SubtitleTrack(tracks[i].Language) { Label = tracks[i].Label };
        }

        return result;
    }
}
