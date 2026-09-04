using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services.Comic;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ComicInfoMapperTests
{
    private static ParsedComicFilename Parsed(string? volume = null, string? chapter = null) =>
        new(volume, chapter, volume is null && chapter is null, null, "Series");

    private static Dictionary<string, string> Meta(params (string Key, string Value)[] kv) =>
        kv.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void FallbackTitle_UsedForSeries_WhenNoMetadata()
    {
        var m = ComicInfoMapper.Map(null, Meta(), Parsed(), BookType.Manga, "My Manga");

        Assert.Equal("My Manga", m.Series);
    }

    [Fact]
    public void NoFabrication_OnlySeriesAndChapterTitleWhenMetaEmpty()
    {
        var m = ComicInfoMapper.Map(null, Meta(), Parsed(chapter: "5"), BookType.Manga, "My Manga");

        Assert.Equal("My Manga", m.Series);
        Assert.Equal("Chapter 5", m.Title);
        Assert.Null(m.Summary);
        Assert.Null(m.Writer);
        Assert.Null(m.Year);
        Assert.Null(m.Format);
        Assert.Null(m.AgeRating);
        Assert.Null(m.LocalizedSeries);
        Assert.Null(m.Count);
    }

    [Fact]
    public void ExistingSeries_WinsOverFetchedTitle()
    {
        var existing = new ComicInfoModel { Series = "Old Name" };

        var m = ComicInfoMapper.Map(existing, Meta(("Title", "New Name")), Parsed(), BookType.Manga, "Fallback");

        Assert.Equal("Old Name", m.Series);
    }

    [Fact]
    public void ExistingNumberAndVolume_WinOverFetchedAndParsed()
    {
        var existing = new ComicInfoModel { Number = "7", Volume = "2" };
        var meta = Meta(("IssueNumber", "9"), ("ComicVolume", "3"));

        var m = ComicInfoMapper.Map(existing, meta, Parsed(volume: "4", chapter: "5"), BookType.Comic, "Fallback");

        Assert.Equal("7", m.Number);
        Assert.Equal("2", m.Volume);
    }

    [Fact]
    public void FetchedNumberAndVolume_UsedWhenNoExisting()
    {
        var meta = Meta(("IssueNumber", "9"), ("ComicVolume", "3"));

        var m = ComicInfoMapper.Map(null, meta, Parsed(volume: "4", chapter: "5"), BookType.Comic, "Fallback");

        Assert.Equal("9", m.Number);
        Assert.Equal("3", m.Volume);
    }

    [Fact]
    public void ParsedValues_UsedWhenNoExistingOrFetched()
    {
        var m = ComicInfoMapper.Map(null, Meta(), Parsed(volume: "4", chapter: "5"), BookType.Manga, "Fallback");

        Assert.Equal("4", m.Volume);
        Assert.Equal("5", m.Number);
    }

    [Fact]
    public void ComicIssueName_UsedAsTitle()
    {
        var meta = Meta(("IssueName", "Amazing Spider-Man #300"));

        var m = ComicInfoMapper.Map(null, meta, Parsed(), BookType.Comic, "Fallback");

        Assert.Equal("Amazing Spider-Man #300", m.Title);
    }

    [Fact]
    public void MangaSeriesTitle_NeverUsedAsChapterTitle()
    {
        var meta = Meta(("Title", "One Piece"));

        var m = ComicInfoMapper.Map(null, meta, Parsed(chapter: "5"), BookType.Manga, "Fallback");

        Assert.Equal("One Piece", m.Series);
        Assert.Equal("Chapter 5", m.Title);
    }

    [Fact]
    public void Count_FromVolumesForManga()
    {
        var m = ComicInfoMapper.Map(null, Meta(("Volumes", "12")), Parsed(), BookType.Manga, "Fallback");

        Assert.Equal("12", m.Count);
    }

    [Fact]
    public void Count_FromIssueCountForComic()
    {
        var m = ComicInfoMapper.Map(null, Meta(("IssueCount", "60")), Parsed(), BookType.Comic, "Fallback");

        Assert.Equal("60", m.Count);
    }

    [Fact]
    public void StartDate_SplitsIntoYearMonthDay()
    {
        var m = ComicInfoMapper.Map(null, Meta(("StartDate", "2023-04-15")), Parsed(), BookType.Manga, "Fallback");

        Assert.Equal(2023, m.Year);
        Assert.Equal(4, m.Month);
        Assert.Equal(15, m.Day);
    }

    [Fact]
    public void YearOnlyDate_SetsYearOnly()
    {
        var m = ComicInfoMapper.Map(null, Meta(("StartYear", "2020")), Parsed(), BookType.Manga, "Fallback");

        Assert.Equal(2020, m.Year);
        Assert.Null(m.Month);
        Assert.Null(m.Day);
    }

    [Fact]
    public void ExistingOnlyFields_SurviveMerge()
    {
        var existing = new ComicInfoModel { StoryArc = "Arc One", StoryArcNumber = "2", SeriesGroup = "G" };

        var m = ComicInfoMapper.Map(existing, Meta(("Title", "T")), Parsed(), BookType.Comic, "Fallback");

        Assert.Equal("Arc One", m.StoryArc);
        Assert.Equal("2", m.StoryArcNumber);
        Assert.Equal("G", m.SeriesGroup);
    }

    [Fact]
    public void ComicStaffRoles_MappedToCreditTags()
    {
        var meta = Meta(
            ("StaffWriter", "Writer A"),
            ("StaffPenciller", "Artist B"),
            ("StaffInker", "Inker C"),
            ("StaffColorist", "Color D"),
            ("StaffLetterer", "Letter E"),
            ("StaffCoverArtist", "Cover F"),
            ("StaffEditor", "Editor G"));

        var m = ComicInfoMapper.Map(null, meta, Parsed(), BookType.Comic, "Fallback");

        Assert.Equal("Writer A", m.Writer);
        Assert.Equal("Artist B", m.Penciller);
        Assert.Equal("Inker C", m.Inker);
        Assert.Equal("Color D", m.Colorist);
        Assert.Equal("Letter E", m.Letterer);
        Assert.Equal("Cover F", m.CoverArtist);
        Assert.Equal("Editor G", m.Editor);
    }

    [Fact]
    public void MangaAuthors_MappedToWriter()
    {
        var m = ComicInfoMapper.Map(null, Meta(("Authors", "Author A, Author B")), Parsed(), BookType.Manga, "Fallback");

        Assert.Equal("Author A, Author B", m.Writer);
        Assert.Null(m.Penciller);
    }

    [Fact]
    public void ComicVineRatings_MapToGuideScale()
    {
        Assert.Equal("Teen", MapRating("T+"));
        Assert.Equal("M", MapRating("M"));
        Assert.Equal("Everyone", MapRating("E"));
        Assert.Equal("Mature 17+", MapRating("R"));
        Assert.Equal("Adults Only 18+", MapRating("AO"));

        string? MapRating(string rating) =>
            ComicInfoMapper.Map(null, Meta(("Rating", rating)), Parsed(), BookType.Comic, "Fallback").AgeRating;
    }

    [Fact]
    public void InferredAgeRating_FallsBackToHeuristic()
    {
        var m = ComicInfoMapper.Map(null, Meta(("Genres", "hentai")), Parsed(), BookType.Manga, "Fallback");

        Assert.Equal("M", m.AgeRating);
    }

    [Fact]
    public void NoAgeSignals_NoAgeRating()
    {
        var m = ComicInfoMapper.Map(null, Meta(("Title", "Calm Manga")), Parsed(), BookType.Manga, "Fallback");

        Assert.Null(m.AgeRating);
    }

    [Fact]
    public void OneShot_MapsToRecognizedFormat()
    {
        var m = ComicInfoMapper.Map(null, Meta(("Format", "ONE_SHOT")), Parsed(), BookType.Manga, "Fallback");

        Assert.Equal("One Shot", m.Format);
    }

    [Fact]
    public void NonOneShotMangaFormat_NotEmitted()
    {
        var m = ComicInfoMapper.Map(null, Meta(("Format", "MANGA")), Parsed(), BookType.Manga, "Fallback");

        Assert.Null(m.Format);
    }

    [Fact]
    public void ComicFormat_OnlyRecognizedValuesEmitted()
    {
        Assert.Equal("TPB", ComicInfoMapper.Map(null, Meta(("ComicFormat", "TPB")), Parsed(), BookType.Comic, "F").Format);
        Assert.Null(ComicInfoMapper.Map(null, Meta(("ComicFormat", "Comic")), Parsed(), BookType.Comic, "F").Format);
    }

    [Fact]
    public void PageCount_FromArchiveCount()
    {
        var m = ComicInfoMapper.Map(null, Meta(), Parsed(), BookType.Manga, "Fallback", pageCount: 180);

        Assert.Equal(180, m.PageCount);
    }

    [Fact]
    public void ExistingPageCount_WinsOverArchiveCount()
    {
        var existing = new ComicInfoModel { PageCount = 99 };

        var m = ComicInfoMapper.Map(existing, Meta(), Parsed(), BookType.Manga, "Fallback", pageCount: 180);

        Assert.Equal(99, m.PageCount);
    }

    [Fact]
    public void Isbn_MappedToGTIN()
    {
        var m = ComicInfoMapper.Map(null, Meta(("Isbn", "978-1-23456-789-7")), Parsed(), BookType.Comic, "Fallback");

        Assert.Equal("978-1-23456-789-7", m.GTIN);
    }

    [Fact]
    public void SourceUrl_MappedToWeb()
    {
        var m = ComicInfoMapper.Map(null, Meta(("SourceUrl", "https://example.com/x")), Parsed(), BookType.Comic, "Fallback");

        Assert.Equal("https://example.com/x", m.Web);
    }
}
