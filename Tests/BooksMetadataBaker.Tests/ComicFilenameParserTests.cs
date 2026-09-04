using BooksMetadataBaker.Services.Comic;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ComicFilenameParserTests
{
    // ------------------------------------------------------------------
    // kavita-manga-comics-agent-guide.md §3 example table — verbatim.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Noblesse - Episode 406 (52 Pages).7z", null, "406")]
    [InlineData("[Hidoi]_Amaenaideyo_MS_vol01_chp02.rar", "1", "2")]
    [InlineData("Okusama wa Shougakusei c003 (v01) [bokuwaNEET]", "1", "3")]
    [InlineData("Beelzebub_53[KSH].zip", null, "53")]
    [InlineData("Killing Bites Vol. 0001 Ch. 0001 - Galactica Scanlations (gb)", "1", "1")]
    [InlineData("Dance in the Vampire Bund v16-17 (Digital) (NiceDragon)", "16-17", null)]
    [InlineData("Tower Of God S01 014 (CBT) (digital).cbz", "1", "14")]
    public void GuideExamples_ParseAsDocumented(string fileName, string? expectedVolume, string? expectedChapter)
    {
        var parsed = ComicFilenameParser.Parse(fileName);

        Assert.Equal(expectedVolume, parsed.Volume);
        Assert.Equal(expectedChapter, parsed.Chapter);
        Assert.False(parsed.IsSpecial);
    }

    [Fact]
    public void GuideExample_SeriesHint_IsExtracted()
    {
        var parsed = ComicFilenameParser.Parse("Okusama wa Shougakusei c003 (v01) [bokuwaNEET]");

        Assert.Equal("Okusama wa Shougakusei", parsed.SeriesHint);
    }

    // ------------------------------------------------------------------
    // Ranges, decimals, half-chapters
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Series Vol. 1-5.cbz", "1-5", null)]
    [InlineData("Series Volume 3-7.cbz", "3-7", null)]
    [InlineData("Series c001-006.cbz", null, "1-6")]
    [InlineData("Series v1-v2.cbz", "1-2", null)]
    public void Ranges_ArePreserved(string fileName, string? expectedVolume, string? expectedChapter)
    {
        var parsed = ComicFilenameParser.Parse(fileName);

        Assert.Equal(expectedVolume, parsed.Volume);
        Assert.Equal(expectedChapter, parsed.Chapter);
    }

    [Theory]
    [InlineData("Series 034.5.cbz", null, "34.5")]
    [InlineData("Series ch034.5.cbz", null, "34.5")]
    public void Decimals_AreNormalized(string fileName, string? expectedVolume, string? expectedChapter)
    {
        var parsed = ComicFilenameParser.Parse(fileName);

        Assert.Equal(expectedVolume, parsed.Volume);
        Assert.Equal(expectedChapter, parsed.Chapter);
    }

    [Theory]
    [InlineData("Series 153b.cbz", null, "153.5")]
    [InlineData("Series ch153b.cbz", null, "153.5")]
    [InlineData("Series c153b.cbz", null, "153.5")]
    public void TrailingB_IsHalfChapter(string fileName, string? expectedVolume, string? expectedChapter)
    {
        var parsed = ComicFilenameParser.Parse(fileName);

        Assert.Equal(expectedVolume, parsed.Volume);
        Assert.Equal(expectedChapter, parsed.Chapter);
    }

    // ------------------------------------------------------------------
    // Locale variants
    // ------------------------------------------------------------------

    [Theory]
    // FR
    [InlineData("Series tome 3.cbz", "3", null)]
    [InlineData("Series t3.cbz", "3", null)]
    // JP
    [InlineData("Series 巻02.cbz", "2", null)]
    [InlineData("Series 話 12.cbz", null, "12")]
    // CN
    [InlineData("Series 卷 5.cbz", "5", null)]
    [InlineData("Series 话 12.cbz", null, "12")]
    // KR
    [InlineData("Series 권 5.cbz", "5", null)]
    [InlineData("Series 화 12.cbz", null, "12")]
    [InlineData("Series 시즌 2.cbz", "2", null)]
    // TH
    [InlineData("Series เล่ม 1.cbz", "1", null)]
    [InlineData("Series ตอนที่ 4.cbz", null, "4")]
    // RU
    [InlineData("Series Том 2.cbz", "2", null)]
    [InlineData("Series Тома 3.cbz", "3", null)]
    [InlineData("Series Глава 7.cbz", null, "7")]
    public void LocaleVariants_AreRecognized(string fileName, string? expectedVolume, string? expectedChapter)
    {
        var parsed = ComicFilenameParser.Parse(fileName);

        Assert.Equal(expectedVolume, parsed.Volume);
        Assert.Equal(expectedChapter, parsed.Chapter);
    }

    // ------------------------------------------------------------------
    // Special handling
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Series Name.cbz")]
    [InlineData("Series Name (2019).cbz")]
    public void NoVolumeNoChapter_IsSpecial(string fileName)
    {
        var parsed = ComicFilenameParser.Parse(fileName);

        Assert.Null(parsed.Volume);
        Assert.Null(parsed.Chapter);
        Assert.True(parsed.IsSpecial);
        Assert.Null(parsed.SpNumber);
    }

    [Fact]
    public void SpMarker_ForcesSpecialAndIsRecorded()
    {
        var parsed = ComicFilenameParser.Parse("Series Name SP01 Special Name.cbz");

        Assert.True(parsed.IsSpecial);
        Assert.Equal("1", parsed.SpNumber);
        Assert.Contains("Series", parsed.SeriesHint);
        Assert.DoesNotContain("SP", parsed.SeriesHint);
    }

    [Fact]
    public void AnnualKeyword_AloneDoesNotOverrideVolume()
    {
        // Keywords in a filename alone do not force Special status (§3) —
        // a file with a volume marker is not a special.
        var parsed = ComicFilenameParser.Parse("Series v02 Annual.cbz");

        Assert.Equal("2", parsed.Volume);
        Assert.False(parsed.IsSpecial);
    }

    // ------------------------------------------------------------------
    // Marker edge cases
    // ------------------------------------------------------------------

    [Fact]
    public void TwoVolumeMarkers_FirstWins()
    {
        var parsed = ComicFilenameParser.Parse("Series v02 vol03.cbz");

        Assert.Equal("2", parsed.Volume);
        Assert.Null(parsed.Chapter);
    }

    [Fact]
    public void SecondVolumeMarker_NumberIsNotMistakenForChapter()
    {
        var parsed = ComicFilenameParser.Parse("Series v01 vol03.cbz");

        Assert.Equal("1", parsed.Volume);
        Assert.Null(parsed.Chapter);
    }

    [Fact]
    public void ChapterMarker_BeforeVolumeMarker_StillParses()
    {
        var parsed = ComicFilenameParser.Parse("Series ch02_vol01.cbz");

        Assert.Equal("1", parsed.Volume);
        Assert.Equal("2", parsed.Chapter);
    }

    [Fact]
    public void ScanlationBrackets_DoNotBreakParsing()
    {
        var parsed = ComicFilenameParser.Parse("[Group] Series Vol. 3 (10 Pages) [Mirror].cbz");

        Assert.Equal("3", parsed.Volume);
        Assert.Null(parsed.Chapter);
        Assert.False(parsed.IsSpecial);
    }

    [Fact]
    public void VolumeInsideParentheses_IsRecognized()
    {
        var parsed = ComicFilenameParser.Parse("Series c03 (v01).cbz");

        Assert.Equal("1", parsed.Volume);
        Assert.Equal("3", parsed.Chapter);
    }

    [Fact]
    public void YearInParentheses_IsNotAChapter()
    {
        var parsed = ComicFilenameParser.Parse("Series (2019).cbz");

        Assert.Null(parsed.Chapter);
        Assert.True(parsed.IsSpecial);
    }

    [Fact]
    public void Extension_IsIgnored()
    {
        var parsed = ComicFilenameParser.Parse("Series Vol. 3.CBZ");

        Assert.Equal("3", parsed.Volume);
    }
}
