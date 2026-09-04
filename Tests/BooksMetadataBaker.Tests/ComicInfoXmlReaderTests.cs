using BooksMetadataBaker.Services.Comic;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ComicInfoXmlReaderTests
{
    [Fact]
    public void RoundTrip_PreservesAllValues()
    {
        var original = new ComicInfoModel
        {
            Title = "Chapter 1",
            Series = "My Manga",
            SeriesSort = "My Manga",
            LocalizedSeries = "My Manga (EN)",
            Number = "1-5",
            Volume = "16-17",
            Count = "12",
            Summary = "Summary text",
            Year = 2023,
            Month = 4,
            Day = 15,
            Publisher = "Publisher",
            Imprint = "Imprint",
            Writer = "W",
            Penciller = "P",
            Inker = "I",
            Colorist = "C",
            Letterer = "L",
            CoverArtist = "CA",
            Editor = "E",
            Translator = "T",
            Genre = "Action, Adventure",
            Tags = "shounen",
            Web = "https://example.com",
            PageCount = 200,
            LanguageISO = "en",
            Format = "TPB",
            SeriesGroup = "Group",
            AgeRating = "Teen",
            GTIN = "9781234567897",
            StoryArc = "Arc",
            StoryArcNumber = "2",
            AlternativeSeries = "Alt",
            AlternativeCount = "3"
        };

        var parsed = ComicInfoXmlReader.FromXml(ComicInfoXmlWriter.ToXml(original));

        Assert.NotNull(parsed);
        Assert.Equal(ComicInfoXmlWriter.ToXml(original), ComicInfoXmlWriter.ToXml(parsed));
    }

    [Fact]
    public void UnknownTags_AreIgnored()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ComicInfo>
              <Series>Known</Series>
              <SomeFutureTag>value</SomeFutureTag>
            </ComicInfo>
            """;

        var parsed = ComicInfoXmlReader.FromXml(xml);

        Assert.NotNull(parsed);
        Assert.Equal("Known", parsed!.Series);
    }

    [Fact]
    public void EmptyElements_AreAbsent()
    {
        const string xml = """
            <ComicInfo>
              <Series></Series>
              <Writer>   </Writer>
              <Number>7</Number>
            </ComicInfo>
            """;

        var parsed = ComicInfoXmlReader.FromXml(xml);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Series);
        Assert.Null(parsed.Writer);
        Assert.Equal("7", parsed.Number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrEmpty_ReturnsNull(string? xml)
    {
        Assert.Null(ComicInfoXmlReader.FromXml(xml));
    }

    [Fact]
    public void MalformedXml_ReturnsNull()
    {
        Assert.Null(ComicInfoXmlReader.FromXml("<ComicInfo><Series>unterminated"));
    }

    [Fact]
    public void NonComicInfoRoot_ReturnsNull()
    {
        Assert.Null(ComicInfoXmlReader.FromXml("<Root><Series>x</Series></Root>"));
    }

    [Fact]
    public void Whitespace_IsTrimmed()
    {
        const string xml = "<ComicInfo><Series>  Padded Series  </Series></ComicInfo>";

        var parsed = ComicInfoXmlReader.FromXml(xml);

        Assert.Equal("Padded Series", parsed!.Series);
    }

    [Fact]
    public void NonNumericIntFields_AreAbsent()
    {
        const string xml = "<ComicInfo><PageCount>unknown</PageCount><Year>2020</Year></ComicInfo>";

        var parsed = ComicInfoXmlReader.FromXml(xml);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.PageCount);
        Assert.Equal(2020, parsed.Year);
    }
}
