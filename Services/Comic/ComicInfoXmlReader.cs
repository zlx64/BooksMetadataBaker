using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BooksMetadataBaker.Services.Comic;

/// <summary>
/// Tolerant ComicInfo.xml parser (§1 precedence source 1). Unknown tags are
/// ignored, empty elements count as absent, malformed XML yields null
/// (treated as "no existing metadata").
/// </summary>
public static class ComicInfoXmlReader
{
    public static ComicInfoModel? FromXml(string? xml, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            // A malformed existing ComicInfo.xml is dropped from the merge and later
            // overwritten — log it so the silent data loss is diagnosable.
            logger?.LogWarning(ex, "Existing ComicInfo.xml is malformed and was ignored");
            return null;
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "ComicInfo")
            return null;

        return new ComicInfoModel
        {
            Title = Str(root, "Title"),
            Series = Str(root, "Series"),
            SeriesSort = Str(root, "SeriesSort"),
            LocalizedSeries = Str(root, "LocalizedSeries"),
            Number = Str(root, "Number"),
            Volume = Str(root, "Volume"),
            Count = Str(root, "Count"),
            Summary = Str(root, "Summary"),
            Year = Int(root, "Year"),
            Month = Int(root, "Month"),
            Day = Int(root, "Day"),
            Publisher = Str(root, "Publisher"),
            Imprint = Str(root, "Imprint"),
            Writer = Str(root, "Writer"),
            Penciller = Str(root, "Penciller"),
            Inker = Str(root, "Inker"),
            Colorist = Str(root, "Colorist"),
            Letterer = Str(root, "Letterer"),
            CoverArtist = Str(root, "CoverArtist"),
            Editor = Str(root, "Editor"),
            Translator = Str(root, "Translator"),
            Genre = Str(root, "Genre"),
            Tags = Str(root, "Tags"),
            Web = Str(root, "Web"),
            PageCount = Int(root, "PageCount"),
            LanguageISO = Str(root, "LanguageISO"),
            Format = Str(root, "Format"),
            SeriesGroup = Str(root, "SeriesGroup"),
            AgeRating = Str(root, "AgeRating"),
            GTIN = Str(root, "GTIN"),
            StoryArc = Str(root, "StoryArc"),
            StoryArcNumber = Str(root, "StoryArcNumber"),
            AlternativeSeries = Str(root, "AlternativeSeries"),
            AlternativeCount = Str(root, "AlternativeCount")
        };
    }

    private static string? Str(XElement root, string localName)
    {
        var el = root.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        var v = el?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static int? Int(XElement root, string localName)
    {
        var v = Str(root, localName);
        return v is not null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }
}
