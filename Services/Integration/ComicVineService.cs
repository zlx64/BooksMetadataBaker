namespace BooksMetadataBaker.Services.Integration;

public class ComicVineService(
    HttpClient http,
    IConfiguration config,
    ILogger<ComicVineService> logger)
    : IMetadataSource
{
    private readonly string apiKey = config["PdfLibrary:ComicVine:ApiKey"] ?? string.Empty;

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Comic) return new Dictionary<string,string>();

        try
        {
            var url = $"search/?api_key={apiKey}&format=json&query={Uri.EscapeDataString(title)}&resources=volume";
            logger.LogInformation("ComicVine request for {Title} Type={Type} Url={Url}", title, type, url);
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength()==0)
            {
                logger.LogInformation("ComicVine no results for {Title}", title);
                return new Dictionary<string,string>();
            }
            var first = results[0];
            var dict = new Dictionary<string,string>();
            if (first.TryGetProperty("name", out var name)) dict["Title"] = name.GetString() ?? string.Empty;

            // description, falling back to the short `deck` synopsis.
            var desc = string.Empty;
            if (first.TryGetProperty("description", out var descEl))
                desc = HtmlCleaner.StripHtml(descEl.GetString());
            if (string.IsNullOrWhiteSpace(desc) && first.TryGetProperty("deck", out var deck))
                desc = HtmlCleaner.StripHtml(deck.GetString());
            if (!string.IsNullOrWhiteSpace(desc))
                dict["Description"] = desc;

            if (first.TryGetProperty("site_detail_url", out var site)) dict["SourceUrl"] = site.GetString() ?? string.Empty;
            if (TryGetScalar(first, "start_year", out var startYear)) dict["StartYear"] = startYear;
            var issueCount = TryGetScalar(first, "count_of_issues", out var coi) ? coi : null;
            if (issueCount is not null) dict["IssueCount"] = issueCount;
            if (first.TryGetProperty("publisher", out var publisher) && publisher.ValueKind==JsonValueKind.Object && publisher.TryGetProperty("name", out var pubName)) dict["Publisher"] = pubName.GetString() ?? string.Empty;
            if (first.TryGetProperty("api_detail_url", out var apiUrl)) dict["ApiDetailUrl"] = apiUrl.GetString() ?? string.Empty;

            // Issue-level fields are only trustworthy when the volume is a single
            // issue (one-shot / collected single); otherwise first_issue is just
            // the series' first issue, not the uploaded one.
            if (first.TryGetProperty("first_issue", out var fi) && fi.ValueKind == JsonValueKind.Object && IsSingleIssue(first, issueCount))
            {
                if (fi.TryGetProperty("issue_number", out var inum)) dict["IssueNumber"] = inum.GetString() ?? string.Empty;
                if (fi.TryGetProperty("name", out var iname)) dict["IssueName"] = iname.GetString() ?? string.Empty;
            }

            MapPersonCredits(first, dict);

            // Fields that some ComicVine responses carry; every read is guarded so
            // absence is harmless (filename-derived values still apply).
            if (TryGetScalar(first, "start_date", out var sd)) dict["StartDate"] = sd;
            if (TryGetScalar(first, "page_count", out var pc)) dict["PageCount"] = pc;
            if (TryGetScalar(first, "rating", out var rating)) dict["Rating"] = rating;
            if (TryGetScalar(first, "format", out var fmt)) dict["ComicFormat"] = fmt;
            if (first.TryGetProperty("language", out var lang) && NormalizeLanguage(lang.GetString()) is { } iso) dict["LanguageISO"] = iso;
            if (TryGetScalar(first, "isbn", out var isbn)) dict["Isbn"] = isbn;
            if (first.TryGetProperty("imprint", out var imprint) && imprint.ValueKind==JsonValueKind.Object && imprint.TryGetProperty("name", out var imName)) dict["Imprint"] = imName.GetString() ?? string.Empty;

            dict["Source"] = "ComicVine";

            logger.LogInformation("ComicVine response mapped for {Title}. Keys={Keys}", title, string.Join(',', dict.Keys));
            return dict;
        }
        catch (Exception ex)
        {
            int? status = ex is HttpRequestException { StatusCode: { } sc } ? (int)sc : null;
            logger.LogWarning(ex, "ComicVine fetch failed for {Title} Type={Type} Status={Status}", title, type, status?.ToString() ?? "n/a");
            return new Dictionary<string,string>();
        }
    }

    private static bool IsSingleIssue(JsonElement volume, string? issueCount)
    {
        if (issueCount is not null)
            return int.TryParse(issueCount, out var n) && n == 1;
        return volume.TryGetProperty("first_issue", out var fi)
            && volume.TryGetProperty("last_issue", out var li)
            && fi.ValueKind == JsonValueKind.Object && li.ValueKind == JsonValueKind.Object
            && fi.TryGetProperty("id", out var fid) && li.TryGetProperty("id", out var lid)
            && fid.ValueKind == JsonValueKind.Number && lid.ValueKind == JsonValueKind.Number
            && fid.GetInt64() == lid.GetInt64();
    }

    private static void MapPersonCredits(JsonElement volume, Dictionary<string, string> dict)
    {
        if (!volume.TryGetProperty("person_credits", out var credits) || credits.ValueKind != JsonValueKind.Array)
            return;
        foreach (var credit in credits.EnumerateArray())
        {
            if (credit.ValueKind != JsonValueKind.Object) continue;
            if (!credit.TryGetProperty("name", out var nameEl) || string.IsNullOrWhiteSpace(nameEl.GetString())) continue;
            if (!credit.TryGetProperty("role", out var roleEl)) continue;
            var name = nameEl.GetString()!;
            foreach (var role in roleEl.GetString()?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            {
                var key = RoleToStaffKey(role);
                if (key is not null && !dict.ContainsKey(key))
                    dict[key] = name;
            }
        }
    }

    private static string? RoleToStaffKey(string role) => role.Trim().ToLowerInvariant() switch
    {
        "writer" or "co-writer" => "StaffWriter",
        "penciller" or "penciller-inker" or "artist" => "StaffPenciller",
        "inker" => "StaffInker",
        "colorist" or "colors" => "StaffColorist",
        "letterer" => "StaffLetterer",
        "cover" or "cover artist" => "StaffCoverArtist",
        "editor" => "StaffEditor",
        _ => null
    };

    // §5 wants ISO 639-1; never fabricate a code from an unrecognized value.
    private static string? NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        if (v.Length is 2 or 3) return v;
        return v switch
        {
            "english" => "en",
            "japanese" => "ja",
            "spanish" => "es",
            "french" => "fr",
            "german" => "de",
            "italian" => "it",
            "portuguese" => "pt",
            "korean" => "ko",
            "chinese" => "zh",
            _ => null
        };
    }

    // ComicVine returns some scalars (start_year, count_of_issues) as strings and
    // others as numbers depending on the endpoint; accept both.
    private static bool TryGetScalar(JsonElement el, string prop, out string value)
    {
        value = string.Empty;
        if (!el.TryGetProperty(prop, out var p)) return false;
        value = p.ValueKind switch
        {
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.String => p.GetString() ?? string.Empty,
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }
}
