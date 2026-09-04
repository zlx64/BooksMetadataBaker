using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BooksMetadataBaker.Services.Comic;

/// <summary>
/// Emits ComicInfo.xml per the §5 generation contract: exact root element and
/// xsi namespace, utf-8 declaration, only tags with known values, §5 field order.
/// </summary>
public static class ComicInfoXmlWriter
{
    public const string FileName = "ComicInfo.xml";

    public static string ToXml(ComicInfoModel m)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ComicInfo",
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance")));
        var el = doc.Root!;

        Add(el, "Series", m.Series);
        Add(el, "SeriesSort", m.SeriesSort);
        Add(el, "LocalizedSeries", m.LocalizedSeries);
        Add(el, "Volume", m.Volume);
        Add(el, "Number", m.Number);
        Add(el, "Title", m.Title);
        Add(el, "Summary", m.Summary);
        Add(el, "Count", m.Count);
        Add(el, "Year", m.Year);
        Add(el, "Month", m.Month);
        Add(el, "Day", m.Day);
        Add(el, "Publisher", m.Publisher);
        Add(el, "Imprint", m.Imprint);
        Add(el, "Writer", m.Writer);
        Add(el, "Penciller", m.Penciller);
        Add(el, "Inker", m.Inker);
        Add(el, "Colorist", m.Colorist);
        Add(el, "Letterer", m.Letterer);
        Add(el, "CoverArtist", m.CoverArtist);
        Add(el, "Editor", m.Editor);
        Add(el, "Translator", m.Translator);
        Add(el, "Genre", m.Genre);
        Add(el, "Tags", m.Tags);
        Add(el, "Web", m.Web);
        Add(el, "PageCount", m.PageCount);
        Add(el, "LanguageISO", m.LanguageISO);
        Add(el, "Format", m.Format);
        Add(el, "SeriesGroup", m.SeriesGroup);
        Add(el, "AgeRating", m.AgeRating);
        Add(el, "GTIN", m.GTIN);
        Add(el, "StoryArc", m.StoryArc);
        Add(el, "StoryArcNumber", m.StoryArcNumber);
        Add(el, "AlternativeSeries", m.AlternativeSeries);
        Add(el, "AlternativeCount", m.AlternativeCount);

        // XDocument.ToString() omits the XML declaration by design, and a text-sink
        // XmlWriter reports utf-16; write to a byte stream so the utf-8 declaration
        // required by §5 is emitted.
        var utf8 = new UTF8Encoding(false);
        using var ms = new MemoryStream();
        using (var xw = XmlWriter.Create(ms, new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Indent = true,
            Encoding = utf8
        }))
        {
            doc.Save(xw);
        }
        return utf8.GetString(ms.ToArray());
    }

    private static void Add(XElement el, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            el.Add(new XElement(name, value));
    }

    private static void Add(XElement el, string name, int? value)
    {
        if (value is > 0)
            el.Add(new XElement(name, value.Value.ToString(CultureInfo.InvariantCulture)));
    }
}
