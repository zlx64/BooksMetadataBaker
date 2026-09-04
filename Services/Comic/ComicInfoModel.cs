namespace BooksMetadataBaker.Services.Comic;

/// <summary>
/// Typed model for ComicInfo.xml (ComicInfo v2.1 draft, Anansi Project schema)
/// per kavita-manga-comics-agent-guide.md §5. All fields are optional — only
/// tags with known values are emitted (never fabricate).
/// </summary>
public sealed class ComicInfoModel
{
    // String fields keep verbatim values: Number/Volume/Count may be ranges
    // ("1-5") or tokens ("TPB1").
    public string? Title { get; set; }
    public string? Series { get; set; }
    public string? SeriesSort { get; set; }
    public string? LocalizedSeries { get; set; }
    public string? Number { get; set; }
    public string? Volume { get; set; }
    public string? Count { get; set; }
    public string? Summary { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    public string? Publisher { get; set; }
    public string? Imprint { get; set; }
    public string? Writer { get; set; }
    public string? Penciller { get; set; }
    public string? Inker { get; set; }
    public string? Colorist { get; set; }
    public string? Letterer { get; set; }
    public string? CoverArtist { get; set; }
    public string? Editor { get; set; }
    public string? Translator { get; set; }
    public string? Genre { get; set; }
    public string? Tags { get; set; }
    public string? Web { get; set; }
    public int? PageCount { get; set; }
    public string? LanguageISO { get; set; }
    public string? Format { get; set; }
    public string? SeriesGroup { get; set; }
    public string? AgeRating { get; set; }
    public string? GTIN { get; set; }
    public string? StoryArc { get; set; }
    public string? StoryArcNumber { get; set; }
    public string? AlternativeSeries { get; set; }
    public string? AlternativeCount { get; set; }
}
