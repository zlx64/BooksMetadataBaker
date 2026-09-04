namespace BooksMetadataBaker.Services.Integration;

public class AniListService(HttpClient http, ILogger<AniListService> logger)
    : IMetadataSource
{
    // Roles that identify the actual creator(s) of a work, as opposed to
    // localization staff (translators, editors, letterers, typesetters).
    private static readonly string[] CreatorRoleKeywords =
    {
        "story", "artist", "writer", "manga", "original", "light novel", "illustrator"
    };

    public async Task<Dictionary<string, string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        // Only attempt for Manga or LightNovel
        if (type is not BookType.Manga && type is not BookType.LightNovel)
            return new Dictionary<string, string>();

        // AniList uses MediaType=MANGA for manga and light novels; light novels distinguished by format NOVEL
        const string mediaType = "MANGA";
        var format = type == BookType.LightNovel ? "NOVEL" : null;

        // Build search variants (strip volume markers)
        var cleaned = Regex.Replace(title, @"(?i)\bvol(?:ume)?\s*\d+(?:\.\d+)?", "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        var variants = new[] { title, cleaned }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new Dictionary<string, string>();
        foreach (var variant in variants)
        {
            // AniList quirk: explicitly sending "format": null in the variables makes the
            // API return HTTP 404 "Not Found". Omit the variable when it's null so GraphQL
            // defaults it; the query still declares $format and works either way.
            var variables = new Dictionary<string, object?>
            {
                ["search"] = variant,
                ["type"] = mediaType
            };
            if (format != null)
                variables["format"] = format;

            var queryObj = new
            {
                query = @"query ($search: String, $type: MediaType, $format: MediaFormat) {
  Media(search: $search, type: $type, format: $format) {
    id
    title { romaji english native }
    description(asHtml: false)
    siteUrl
    format
    status
    averageScore
    volumes
    chapters
    genres
    startDate { year month day }
    endDate { year month day }
    staff(perPage: 8) { edges { role node { name { full } } } }
  }
}",
                variables
            };
            try
            {
                logger.LogInformation(
                    "AniList request variant='{Variant}' Title={Title} Type={Type} Format={Format}",
                    variant, title, type, format);

                using var resp = await http.PostAsJsonAsync("", queryObj, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    logger.LogWarning("AniList HTTP {Status} for variant='{Variant}' Title={Title}: {Body}",
                        (int)resp.StatusCode, variant, title, Truncate(body, 500));
                    continue;
                }
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    logger.LogWarning("AniList GraphQL errors for variant='{Variant}' Title={Title}: {Errors}",
                        variant, title, Truncate(errors.GetRawText(), 500));
                    continue;
                }
                var media = doc.RootElement.GetProperty("data").GetProperty("Media");
                var mapped = MapMedia(media);
                if (mapped.Count > 0)
                {
                    result = mapped;
                    break; // first successful variant wins
                }
            }
            catch (Exception ex)
            {
                int? status = ex is HttpRequestException { StatusCode: { } sc } ? (int)sc : null;
                logger.LogWarning(ex,
                    "AniList fetch failed variant='{Variant}' Title={Title} Type={Type} Status={Status}",
                    variant, title, type, status?.ToString() ?? "n/a");
            }
        }
        return result;
    }

    private Dictionary<string, string> MapMedia(JsonElement media)
    {
        var dict = new Dictionary<string, string>();

        // No match: data.Media is null. Return empty so the caller can try the
        // next search variant instead of stopping with only {Source: AniList}.
        if (media.ValueKind != JsonValueKind.Object) return dict;

        if (media.TryGetProperty("title", out var titleObj))
        {
            if (titleObj.TryGetProperty("english", out var eng) && !string.IsNullOrWhiteSpace(eng.GetString()))
                dict["TitleEnglish"] = eng.GetString()!;
            if (titleObj.TryGetProperty("romaji", out var romaji) && !string.IsNullOrWhiteSpace(romaji.GetString()))
                dict["TitleRomaji"] = romaji.GetString()!;
            if (titleObj.TryGetProperty("native", out var native) && !string.IsNullOrWhiteSpace(native.GetString()))
                dict["TitleNative"] = native.GetString()!;
        }

        if (media.TryGetProperty("description", out var desc))
            dict["Description"] = HtmlCleaner.StripHtml(desc.GetString());
        if (media.TryGetProperty("siteUrl", out var site))
            dict["SourceUrl"] = site.GetString() ?? string.Empty;
        if (media.TryGetProperty("format", out var fmt))
            dict["Format"] = fmt.GetString() ?? string.Empty;
        if (media.TryGetProperty("status", out var st))
            dict["Status"] = st.GetString() ?? string.Empty;
        if (media.TryGetProperty("averageScore", out var score) && score.ValueKind == JsonValueKind.Number)
            dict["AverageScore"] = score.GetInt32().ToString();
        if (media.TryGetProperty("volumes", out var vols) && vols.ValueKind == JsonValueKind.Number)
            dict["Volumes"] = vols.GetInt32().ToString();
        if (media.TryGetProperty("chapters", out var ch) && ch.ValueKind == JsonValueKind.Number)
            dict["Chapters"] = ch.GetInt32().ToString();
        if (media.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            dict["Genres"] = string.Join(", ", genres.EnumerateArray()
                .Select(g => g.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (media.TryGetProperty("startDate", out var sd))
            dict["StartDate"] = BuildDate(sd);
        if (media.TryGetProperty("endDate", out var ed))
            dict["EndDate"] = BuildDate(ed);

        if (media.TryGetProperty("staff", out var staff))
        {
            try
            {
                var edges = staff.GetProperty("edges").EnumerateArray()
                    .Select(e => new
                    {
                        Role = e.TryGetProperty("role", out var r) ? r.GetString() ?? string.Empty : string.Empty,
                        Name = (e.TryGetProperty("node", out var n) &&
                                 n.TryGetProperty("name", out var nm) &&
                                 nm.TryGetProperty("full", out var f)) ? f.GetString() : null
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .ToList();

                // Prefer the actual creators (story/art/writer/etc.) over localization
                // staff (translators, editors, letterers) so "Authors" stays accurate.
                var creators = edges
                    .Where(e => CreatorRoleKeywords.Any(k =>
                        e.Role.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .Select(e => e.Name!)
                    .Distinct()
                    .ToList();

                var names = creators.Count > 0
                    ? creators
                    : edges.Select(e => e.Name!).Distinct().ToList();

                if (names.Count > 0 && !dict.ContainsKey("Authors"))
                    dict["Authors"] = string.Join(", ", names);
            }
            catch { /* ignore staff mapping issues */ }
        }

        dict["Source"] = "AniList";
        return dict;
    }

    private static string BuildDate(JsonElement el)
    {
        try
        {
            var year = el.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number
                ? y.GetInt32().ToString("D4") : string.Empty;
            var month = el.TryGetProperty("month", out var m) && m.ValueKind == JsonValueKind.Number
                ? m.GetInt32().ToString("D2") : string.Empty;
            var day = el.TryGetProperty("day", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32().ToString("D2") : string.Empty;
            var parts = new[] { year, month, day }.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            return parts.Length == 0 ? string.Empty : string.Join('-', parts);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Truncate(string? s, int max)
    {
        var t = (s ?? string.Empty).Trim();
        return t.Length > max ? t[..max] + "…" : t;
    }
}
