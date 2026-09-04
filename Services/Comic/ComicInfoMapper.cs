using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services.Helpers;

namespace BooksMetadataBaker.Services.Comic;

/// <summary>
/// Merges metadata into a ComicInfoModel per the guide's precedence model (§1):
/// existing in-archive ComicInfo.xml (non-empty fields) &gt; fetched metadata
/// &gt; filename-derived values &gt; fallback title. Only tags with known values
/// are populated (§5: never fabricate).
/// </summary>
public static class ComicInfoMapper
{
    // §5 — recognized Format values that force Special status.
    private static readonly HashSet<string> SpecialFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "Special", "Reference", "Director's Cut", "Box Set", "Box-Set", "Annual", "Anthology",
        "Epilogue", "One Shot", "One-Shot", "Prologue", "TPB", "Trade Paper Back", "Omnibus",
        "Compendium", "Absolute", "Graphic Novel", "GN", "FCBD", "Giant Size"
    };

    // ComicVine rating -> §5 ordered scale.
    private static readonly Dictionary<string, string> ComicVineRatings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["E"] = "Everyone",
        ["T+"] = "Teen",
        ["M"] = "M",
        ["R"] = "Mature 17+",
        ["AO"] = "Adults Only 18+"
    };

    public static ComicInfoModel Map(
        ComicInfoModel? existing,
        IDictionary<string, string> metadata,
        ParsedComicFilename parsed,
        BookType type,
        string fallbackTitle,
        int? pageCount = null)
    {
        var m = existing ?? new ComicInfoModel();
        var meta = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var isComic = type is BookType.Comic;

        // Series is required for grouping (§5); the fallback title guarantees presence.
        m.Series = Choose(m.Series,
            MetadataHelpers.GetFirst(meta, fallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative"));

        m.Volume = Choose(m.Volume, Get(meta, "ComicVolume"), parsed.Volume);
        m.Number = Choose(m.Number, Get(meta, "IssueNumber"), parsed.Chapter);

        // Chapter/issue title — never a series title.
        m.Title = Choose(m.Title,
            isComic ? Get(meta, "IssueName") : null,
            parsed.Chapter is not null ? $"Chapter {parsed.Chapter}" : null);

        // Total series length (manga: volumes, comic: issues) — not the owned count (§5).
        m.Count = Choose(m.Count, isComic ? Get(meta, "IssueCount") : Get(meta, "Volumes"));

        m.Summary = Choose(m.Summary, Get(meta, "Description"));

        var (y, mo, d) = ParseDate(ChooseRaw(meta, "StartDate", "PublishedDate", "StartYear"));
        m.Year = ChooseInt(m.Year, y);
        m.Month = ChooseInt(m.Month, mo);
        m.Day = ChooseInt(m.Day, d);

        m.Publisher = Choose(m.Publisher, Get(meta, "Publisher"));
        m.Imprint = Choose(m.Imprint, Get(meta, "Imprint"));

        if (isComic)
        {
            m.Writer = Choose(m.Writer, Get(meta, "StaffWriter"));
            m.Penciller = Choose(m.Penciller, Get(meta, "StaffPenciller"));
            m.Inker = Choose(m.Inker, Get(meta, "StaffInker"));
            m.Colorist = Choose(m.Colorist, Get(meta, "StaffColorist"));
            m.Letterer = Choose(m.Letterer, Get(meta, "StaffLetterer"));
            m.CoverArtist = Choose(m.CoverArtist, Get(meta, "StaffCoverArtist"));
            m.Editor = Choose(m.Editor, Get(meta, "StaffEditor"));
        }
        else
        {
            m.Writer = Choose(m.Writer, Get(meta, "StaffWriter"), Get(meta, "Authors"));
        }

        var genres = MetadataHelpers.GetGenres(meta);
        m.Genre = Choose(m.Genre, genres.Count > 0 ? string.Join(", ", genres) : null);

        var tags = MetadataHelpers.GetTags(meta);
        m.Tags = Choose(m.Tags, tags.Count > 0 ? string.Join(", ", tags) : null);

        m.Web = Choose(m.Web, Get(meta, "SourceUrl"));
        // Fetched PageCount (e.g. ComicVine page_count) covers archives whose
        // pages could not be counted (CBR without 7-Zip, unreadable archive).
        m.PageCount = ChooseInt(m.PageCount, pageCount, ParseInt(Get(meta, "PageCount")));
        m.LanguageISO = Choose(m.LanguageISO, Get(meta, "LanguageISO"));

        m.Format = Choose(m.Format, MapFormat(meta, type));
        m.AgeRating = Choose(m.AgeRating, MapAgeRating(meta, isComic, genres, tags));
        m.GTIN = Choose(m.GTIN, Get(meta, "Isbn"));

        return m;
    }

    private static string? MapFormat(IDictionary<string, string> meta, BookType type)
    {
        if (type is BookType.Manga or BookType.LightNovel)
        {
            // AniList format: MANGA, NOVEL, LIGHT_NOVEL, ONE_SHOT.
            var f = Get(meta, "Format");
            return string.Equals(f, "ONE_SHOT", StringComparison.OrdinalIgnoreCase) ? "One Shot" : null;
        }

        // ComicVine format — only values Kavita recognizes (§5).
        var cf = Get(meta, "ComicFormat");
        return cf is not null && SpecialFormats.Contains(cf) ? cf : null;
    }

    private static string? MapAgeRating(IDictionary<string, string> meta, bool isComic, List<string> genres, List<string> tags)
    {
        if (isComic)
        {
            var r = Get(meta, "Rating");
            if (r is not null && ComicVineRatings.TryGetValue(r, out var mapped))
                return mapped;
        }

        // Fallback: the app's existing genre/description heuristic (0/13/16/18).
        return MetadataHelpers.InferAgeRating(meta, genres, tags) switch
        {
            18 => "M",
            16 => "Mature 17+",
            13 => "Teen",
            _ => null
        };
    }

    private static (int? Year, int? Month, int? Day) ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null, null);

        var parts = MetadataHelpers.NormDate(raw).Split('-');
        return (
            parts.Length > 0 && int.TryParse(parts[0], out var y) && y > 0 ? y : null,
            parts.Length > 1 && int.TryParse(parts[1], out var mo) && mo is > 0 and < 13 ? mo : null,
            parts.Length > 2 && int.TryParse(parts[2], out var d) && d is > 0 and < 32 ? d : null);
    }

    private static string? Get(IDictionary<string, string> meta, string key)
    {
        return meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    }

    private static int? ParseInt(string? s) =>
        int.TryParse(s, out var v) && v > 0 ? v : null;

    private static string? ChooseRaw(IDictionary<string, string> meta, params string[] keys)
    {
        foreach (var k in keys)
        {
            var v = Get(meta, k);
            if (v is not null)
                return v;
        }
        return null;
    }

    private static string? Choose(string? existing, params string?[] candidates)
    {
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
                return c;
        }
        return null;
    }

    private static int? ChooseInt(int? existing, params int?[] candidates)
    {
        if (existing is > 0)
            return existing;
        foreach (var c in candidates)
        {
            if (c is > 0)
                return c;
        }
        return null;
    }
}
