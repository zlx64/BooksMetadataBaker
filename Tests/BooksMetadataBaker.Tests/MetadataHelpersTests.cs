using BooksMetadataBaker.Services.Helpers;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class MetadataHelpersTests
{
    [Theory]
    [InlineData("One Piece Vol. 10.pdf", "10")]
    [InlineData("one_piece_volume_12.epub", "12")]
    [InlineData("Book Vol 3.5.epub", "3.5")]
    [InlineData("Book 5.pdf", "5")]
    [InlineData("Volume 7 of Something.pdf", "7")]
    public void ExtractVolumeToken_ExtractsNumber(string fileName, string expected)
    {
        Assert.Equal(expected, MetadataHelpers.ExtractVolumeToken(fileName));
    }

    [Theory]
    [InlineData("No Number Here.pdf")]
    [InlineData("the-undefined.epub")]
    [InlineData("1000.pdf")]
    [InlineData("book_5.pdf")]
    public void ExtractVolumeToken_ReturnsNullWhenNoVolume(string fileName)
    {
        Assert.Null(MetadataHelpers.ExtractVolumeToken(fileName));
    }

    [Fact]
    public void ExtractVolumeToken_BareNumberDoesNotThrow()
    {
        // Regression: fallback regex had one capture group but code read Groups[2],
        // throwing ArgumentOutOfRangeException for filenames like "Book 5.pdf".
        var token = MetadataHelpers.ExtractVolumeToken("Book 5.pdf");
        Assert.Equal("5", token);
    }

    [Theory]
    [InlineData("2020-05-01", "2020-05-01")]
    [InlineData("20200501", "2020-05-01")]
    [InlineData("202005", "2020-05-01")]
    [InlineData("2020", "2020")]
    [InlineData("May 1, 2020", "2020-05-01")]
    [InlineData("", "")]
    public void NormDate_NormalizesToIso(string raw, string expected)
    {
        Assert.Equal(expected, MetadataHelpers.NormDate(raw));
    }

    [Theory]
    [InlineData("One Piece - Volume 10", 10.0)]
    [InlineData("book 5", 5.0)]
    [InlineData("Vol. 3.5", 3.5)]
    [InlineData("no number", null)]
    public void ParseVolumeNumber_Parses(string title, double? expected)
    {
        Assert.Equal(expected, MetadataHelpers.ParseVolumeNumber(title));
    }

    [Theory]
    [InlineData("hentai", 18)]
    [InlineData("nsfw", 18)]
    [InlineData("seinen", 16)]
    [InlineData("horror", 16)]
    [InlineData("shounen", 13)]
    [InlineData("romance", 13)]
    [InlineData("adventure", 0)]
    public void InferAgeRating_ByGenreToken(string genre, int expected)
    {
        var meta = new Dictionary<string, string>();
        Assert.Equal(expected, MetadataHelpers.InferAgeRating(meta, new[] { genre }, new List<string>()));
    }

    [Theory]
    [InlineData("2020-05-01", 2020)]
    [InlineData("1999", 1999)]
    [InlineData("", 0)]
    public void ExtractYear_Parses(string date, int expected)
    {
        var meta = new Dictionary<string, string> { ["PublishedDate"] = date };
        Assert.Equal(expected, MetadataHelpers.ExtractYear(meta));
    }

    [Fact]
    public void GetFirst_ReturnsFirstNonEmptyKey()
    {
        var meta = new Dictionary<string, string> { ["A"] = "", ["B"] = "value", ["C"] = "other" };
        Assert.Equal("value", MetadataHelpers.GetFirst(meta, "fallback", "A", "B", "C"));
        Assert.Equal("fallback", MetadataHelpers.GetFirst(meta, "fallback", "X", "Y"));
    }

    [Theory]
    [InlineData(null, "b", "b")]
    [InlineData("a", null, "a")]
    [InlineData("a", "b", "a; b")]
    [InlineData("", "", "")]
    public void Combine_JoinsErrors(string? a, string? b, string expected)
    {
        Assert.Equal(expected, MetadataHelpers.Combine(a, b));
    }

    [Fact]
    public void GetTags_IncludesFormatStatusSourceLanguageAndGenres()
    {
        var meta = new Dictionary<string, string>
        {
            ["Format"] = "MANGA",
            ["Status"] = "Ongoing",
            ["Source"] = "AniList",
            ["Language"] = "ja",
            ["Genres"] = "Action, Comedy"
        };
        var tags = MetadataHelpers.GetTags(meta);
        Assert.Contains("MANGA", tags);
        Assert.Contains("Ongoing", tags);
        Assert.Contains("AniList", tags);
        Assert.Contains("ja", tags);
        Assert.Contains("Action", tags);
        Assert.Contains("Comedy", tags);
    }
}
