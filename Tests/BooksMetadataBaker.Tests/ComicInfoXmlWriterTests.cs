using System.Xml.Linq;
using BooksMetadataBaker.Services.Comic;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ComicInfoXmlWriterTests
{
    private static ComicInfoModel FullModel() => new()
    {
        Series = "My Awesome Manga",
        LocalizedSeries = "My Awesome Manga (EN)",
        Number = "1",
        Volume = "1",
        Count = "12",
        Title = "Chapter 1: The Beginning",
        Summary = "A short summary of this chapter.",
        Year = 2023,
        Month = 4,
        Day = 15,
        Publisher = "Some Publisher",
        Writer = "Author Name",
        Penciller = "Artist Name",
        Genre = "Action, Adventure",
        Tags = "shounen, isekai",
        AgeRating = "Teen",
        LanguageISO = "en",
        PageCount = 200,
        Format = "TPB",
        Web = "https://example.com/series",
        GTIN = "9781234567897"
    };

    [Fact]
    public void FullModel_EmitsWellFormedXml_WithExpectedRootAndDeclaration()
    {
        var xml = ComicInfoXmlWriter.ToXml(FullModel());

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        var doc = XDocument.Parse(xml);
        Assert.Equal("ComicInfo", doc.Root!.Name.LocalName);
        Assert.Equal("http://www.w3.org/2001/XMLSchema-instance",
            doc.Root.Attribute(XNamespace.Xmlns + "xsi")?.Value);
    }

    [Fact]
    public void FullModel_EmitsOnlyKnownTags_InFieldMapOrder()
    {
        var xml = ComicInfoXmlWriter.ToXml(FullModel());
        var doc = XDocument.Parse(xml);
        var names = doc.Root!.Elements().Select(e => e.Name.LocalName).ToList();

        var expected = new[]
        {
            "Series", "LocalizedSeries", "Volume", "Number", "Title", "Summary", "Count",
            "Year", "Month", "Day", "Publisher", "Writer", "Penciller", "Genre", "Tags",
            "Web", "PageCount", "LanguageISO", "Format", "AgeRating", "GTIN"
        };
        Assert.Equal(expected, names);
    }

    [Fact]
    public void EmptyModel_EmitsNoChildElements()
    {
        var xml = ComicInfoXmlWriter.ToXml(new ComicInfoModel());
        var doc = XDocument.Parse(xml);

        Assert.Empty(doc.Root!.Elements());
    }

    [Fact]
    public void SpecialCharacters_AreEscaped()
    {
        var model = new ComicInfoModel { Series = "A & B <C> \"D\" 'E'" };

        var xml = ComicInfoXmlWriter.ToXml(model);
        var doc = XDocument.Parse(xml);

        Assert.Equal("A & B <C> \"D\" 'E'", doc.Root!.Elements("Series").First().Value);
        Assert.DoesNotContain("<C>", xml.Replace("&lt;C&gt;", string.Empty));
    }

    [Fact]
    public void RangeAndTokenValues_ArePreservedVerbatim()
    {
        var model = new ComicInfoModel { Number = "1-5", Volume = "16-17", Count = "TPB1" };

        var doc = XDocument.Parse(ComicInfoXmlWriter.ToXml(model));

        Assert.Equal("1-5", doc.Root!.Elements("Number").First().Value);
        Assert.Equal("16-17", doc.Root.Elements("Volume").First().Value);
        Assert.Equal("TPB1", doc.Root.Elements("Count").First().Value);
    }

    [Fact]
    public void DateParts_AreEmittedAsIntegers()
    {
        var model = new ComicInfoModel { Year = 2023, Month = 4, Day = 15 };

        var doc = XDocument.Parse(ComicInfoXmlWriter.ToXml(model));

        Assert.Equal("2023", doc.Root!.Elements("Year").First().Value);
        Assert.Equal("4", doc.Root.Elements("Month").First().Value);
        Assert.Equal("15", doc.Root.Elements("Day").First().Value);
    }

    [Fact]
    public void ZeroDateParts_AreOmitted()
    {
        var model = new ComicInfoModel { Year = 0, Month = 0, Day = 0, PageCount = 0 };

        var doc = XDocument.Parse(ComicInfoXmlWriter.ToXml(model));

        Assert.DoesNotContain(doc.Root!.Elements(), e => e.Name.LocalName is "Year" or "Month" or "Day" or "PageCount");
    }
}
