using System.IO.Compression;
using System.Text;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;

namespace Screenbox.Core.Tests.Helpers;

public class OpenSubtitlesHelperTests
{
    [Test]
    public async Task ComputeMovieHashAsync_ShouldAddFileSizeAndChunkIntegers()
    {
        // Exactly two chunks: the first integer of the file is 1 and the last integer is 2.
        byte[] file = new byte[131072];
        file[0] = 1;
        file[^8] = 2;
        using MemoryStream stream = new(file);

        string? hash = await OpenSubtitlesHelper.ComputeMovieHashAsync(stream);

        // 131072 + 1 + 2 = 0x20003
        await Assert.That(hash).IsEqualTo("0000000000020003");
    }

    [Test]
    public async Task ComputeMovieHashAsync_ShouldIgnoreOverflow()
    {
        byte[] file = new byte[131072];
        BitConverter.GetBytes(ulong.MaxValue).CopyTo(file, 0);
        using MemoryStream stream = new(file);

        string? hash = await OpenSubtitlesHelper.ComputeMovieHashAsync(stream);

        // 131072 + ulong.MaxValue wraps around to 131071 = 0x1FFFF
        await Assert.That(hash).IsEqualTo("000000000001ffff");
    }

    [Test]
    public async Task ComputeMovieHashAsync_ShouldReturnNullForFilesSmallerThanTwoChunks()
    {
        using MemoryStream stream = new(new byte[131071]);

        string? hash = await OpenSubtitlesHelper.ComputeMovieHashAsync(stream);

        await Assert.That(hash).IsNull();
    }

    [Test]
    public async Task BuildTextSearchUri_ShouldLowercaseEscapeAndAddLanguage()
    {
        Uri uri = OpenSubtitlesHelper.BuildTextSearchUri(" The Matrix ", "eng");

        await Assert.That(uri.AbsoluteUri).IsEqualTo("https://rest.opensubtitles.org/search/query-the%20matrix/sublanguageid-eng");
    }

    [Test]
    public async Task BuildTextSearchUri_ShouldOmitLanguageWhenSearchingAllLanguages()
    {
        Uri uri = OpenSubtitlesHelper.BuildTextSearchUri("Inception", string.Empty);

        await Assert.That(uri.AbsoluteUri).IsEqualTo("https://rest.opensubtitles.org/search/query-inception");
    }

    [Test]
    public async Task BuildHashSearchUri_ShouldOrderParametersAlphabetically()
    {
        Uri uri = OpenSubtitlesHelper.BuildHashSearchUri("8E245D9679D31E12", 12909756, "pob");

        await Assert.That(uri.AbsoluteUri).IsEqualTo(
            "https://rest.opensubtitles.org/search/moviebytesize-12909756/moviehash-8e245d9679d31e12/sublanguageid-pob");
    }

    [Test]
    public async Task ParseSearchResults_ShouldMapFieldsAndSkipUnloadableResults()
    {
        const string json = """
            [
              {"IDSubtitleFile":"1954412181","SubFileName":"Movie.2019.eng.srt","LanguageName":"English","SubLanguageID":"eng",
               "SubFormat":"srt","SubEncoding":"CP1252","SubDownloadsCnt":"22710","MovieReleaseName":" Movie.2019.1080p ",
               "MatchedBy":"moviehash","SubHearingImpaired":"1","SubDownloadLink":"https://dl.opensubtitles.org/en/download/filead/1954412181.gz",
               "Score":12.5,"QueryParameters":{"query":"movie"}},
              {"IDSubtitleFile":"2","SubFileName":"image.idx","SubFormat":"idx","SubDownloadLink":"https://dl.opensubtitles.org/2.gz"},
              {"IDSubtitleFile":"3","SubFileName":"nolink.srt","SubFormat":"srt","SubDownloadLink":""}
            ]
            """;

        IReadOnlyList<OnlineSubtitle> subtitles = OpenSubtitlesHelper.ParseSearchResults(json);

        await Assert.That(subtitles.Count).IsEqualTo(1);
        OnlineSubtitle subtitle = subtitles[0];
        await Assert.That(subtitle.Id).IsEqualTo("1954412181");
        await Assert.That(subtitle.FileName).IsEqualTo("Movie.2019.eng.srt");
        await Assert.That(subtitle.LanguageName).IsEqualTo("English");
        await Assert.That(subtitle.LanguageCode).IsEqualTo("eng");
        await Assert.That(subtitle.Format).IsEqualTo("srt");
        await Assert.That(subtitle.Encoding).IsEqualTo("CP1252");
        await Assert.That(subtitle.DownloadCount).IsEqualTo(22710);
        await Assert.That(subtitle.ReleaseName).IsEqualTo("Movie.2019.1080p");
        await Assert.That(subtitle.IsHashMatch).IsTrue();
        await Assert.That(subtitle.IsHearingImpaired).IsTrue();
        await Assert.That(subtitle.DownloadUri.AbsoluteUri).IsEqualTo("https://dl.opensubtitles.org/en/download/filead/1954412181.gz");
    }

    [Test]
    public async Task ParseSearchResults_ShouldReturnEmptyListForNoResults()
    {
        IReadOnlyList<OnlineSubtitle> subtitles = OpenSubtitlesHelper.ParseSearchResults("[]");

        await Assert.That(subtitles.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MergeResults_ShouldListHashMatchesFirstAndRemoveDuplicates()
    {
        OnlineSubtitle hashMatch = CreateSubtitle("1", downloadCount: 5, isHashMatch: true);
        OnlineSubtitle popular = CreateSubtitle("2", downloadCount: 9000);
        OnlineSubtitle lessPopular = CreateSubtitle("3", downloadCount: 100);

        IReadOnlyList<OnlineSubtitle> merged = OpenSubtitlesHelper.MergeResults(new[] { hashMatch }, new[] { lessPopular, popular, hashMatch });

        await Assert.That(merged.Count).IsEqualTo(3);
        await Assert.That(merged[0].Id).IsEqualTo("1");
        await Assert.That(merged[1].Id).IsEqualTo("2");
        await Assert.That(merged[2].Id).IsEqualTo("3");
    }

    [Test]
    public async Task GetSearchText_ShouldRemoveExtensionSeparatorsAndReleaseTags()
    {
        await Assert.That(OpenSubtitlesHelper.GetSearchText("The.Matrix.1999.1080p.BluRay.x264.mkv")).IsEqualTo("The Matrix 1999");
        await Assert.That(OpenSubtitlesHelper.GetSearchText("Breaking.Bad.S05E14.720p.HDTV.x264.mp4")).IsEqualTo("Breaking Bad S05E14");
        await Assert.That(OpenSubtitlesHelper.GetSearchText("movie_name_2020_WEBRip.mp4")).IsEqualTo("movie name 2020");
        await Assert.That(OpenSubtitlesHelper.GetSearchText("Some Movie (2019).mkv")).IsEqualTo("Some Movie (2019)");
        await Assert.That(OpenSubtitlesHelper.GetSearchText("Charlottes.Web.2006")).IsEqualTo("Charlottes Web 2006");
        await Assert.That(OpenSubtitlesHelper.GetSearchText(null)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetLanguageCode_ShouldMapTagsIncludingRegionalVariants()
    {
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("en-US")).IsEqualTo("eng");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("pt-BR")).IsEqualTo("pob");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("pt-PT")).IsEqualTo("por");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("zh-TW")).IsEqualTo("zht");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("zh-Hant-HK")).IsEqualTo("zht");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("zh-CN")).IsEqualTo("chi");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("es-MX")).IsEqualTo("spa");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("nb-NO")).IsEqualTo("nor");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("sr-Latn-RS")).IsEqualTo("scc");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("el-GR")).IsEqualTo("ell");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("fil-PH")).IsEqualTo("tgl");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode("xx-YY")).IsEqualTo("eng");
        await Assert.That(OpenSubtitlesHelper.GetLanguageCode(string.Empty)).IsEqualTo("eng");
    }

    [Test]
    public async Task Languages_ShouldStartWithAllLanguagesAndHaveUniqueCodes()
    {
        IReadOnlyList<OnlineSubtitleLanguage> languages = OpenSubtitlesHelper.Languages;
        int distinctCodes = languages.Select(language => language.Code).Distinct().Count();

        await Assert.That(languages[0].IsAllLanguages).IsTrue();
        await Assert.That(distinctCodes).IsEqualTo(languages.Count);
    }

    [Test]
    public async Task DecodeSubtitleText_ShouldPreferUtf8AndFallBackToReportedEncoding()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("Café");
        byte[] utf8WithBom = [0xEF, 0xBB, 0xBF, .. utf8];
        byte[] windows1252 = [0x43, 0x61, 0x66, 0xE9];

        await Assert.That(OpenSubtitlesHelper.DecodeSubtitleText(utf8, "CP1251")).IsEqualTo("Café");
        await Assert.That(OpenSubtitlesHelper.DecodeSubtitleText(utf8WithBom, null)).IsEqualTo("Café");
        await Assert.That(OpenSubtitlesHelper.DecodeSubtitleText(windows1252, "CP1252")).IsEqualTo("Café");
        await Assert.That(OpenSubtitlesHelper.DecodeSubtitleText(windows1252, "not-an-encoding")).IsEqualTo("Café");
    }

    [Test]
    public async Task DecompressIfGzip_ShouldDecompressGzipAndPassThroughOtherData()
    {
        byte[] original = Encoding.UTF8.GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nHello\r\n");
        using MemoryStream compressedStream = new();
        using (GZipStream gzip = new(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(original, 0, original.Length);
        }

        bool isDecompressed = OpenSubtitlesHelper.DecompressIfGzip(compressedStream.ToArray()).AsSpan().SequenceEqual(original);
        bool isPassedThrough = ReferenceEquals(OpenSubtitlesHelper.DecompressIfGzip(original), original);

        await Assert.That(isDecompressed).IsTrue();
        await Assert.That(isPassedThrough).IsTrue();
    }

    [Test]
    public async Task GetSubtitleFileName_ShouldReplaceInvalidCharactersAndAddLanguage()
    {
        OnlineSubtitle invalidName = CreateSubtitle("1", fileName: "Movie: Part 1?.srt");
        OnlineSubtitle emptyName = CreateSubtitle("2", fileName: string.Empty);
        OnlineSubtitle languageInName = CreateSubtitle("3", fileName: "Movie.eng.srt");

        await Assert.That(OpenSubtitlesHelper.GetSubtitleFileName(invalidName)).IsEqualTo("Movie_ Part 1_.eng.srt");
        await Assert.That(OpenSubtitlesHelper.GetSubtitleFileName(emptyName)).IsEqualTo("subtitle.eng.srt");
        await Assert.That(OpenSubtitlesHelper.GetSubtitleFileName(languageInName)).IsEqualTo("Movie.eng.srt");
    }

    private static OnlineSubtitle CreateSubtitle(string id, int downloadCount = 0, bool isHashMatch = false,
        string fileName = "subtitle.srt")
    {
        return new OnlineSubtitle(id, fileName, "English", "eng", "srt", string.Empty, downloadCount, string.Empty,
            isHashMatch, IsHearingImpaired: false, new Uri("https://dl.opensubtitles.org/" + id + ".gz"));
    }
}
