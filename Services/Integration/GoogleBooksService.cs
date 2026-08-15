namespace BooksMetadataBaker.Services.Integration;

public class GoogleBooksService(
    HttpClient http,
    IConfiguration config,
    ILogger<GoogleBooksService> logger)
    : IMetadataSource
{
    private readonly string apiKey = config["PdfLibrary:GoogleBooks:ApiKey"] ?? string.Empty;

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Book && type is not BookType.LightNovel) return new Dictionary<string,string>();

        // Clean title (remove volume markers) and build enriched query with OR terms to improve relevance.
        var cleaned = Regex.Replace(title, @"(?i)\bvol(?:ume)?\s*\d+(?:\.\d+)?", "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        var terms = new[] { title, cleaned }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var queryExpression = string.Join(" OR ", terms.Select(t => '"' + t + '"'));
        // Restrict language if provided in config (optional)
        var langRestrict = config["PdfLibrary:GoogleBooks:Lang"];

        try
        {
            var url = $"?q={Uri.EscapeDataString(queryExpression)}&maxResults=5&printType=books&projection=full" + (string.IsNullOrWhiteSpace(langRestrict)?"":$"&langRestrict={langRestrict}") + (string.IsNullOrWhiteSpace(apiKey)?"":$"&key={apiKey}");
            logger.LogInformation("GoogleBooks request for {Title} Type={Type} Url={Url} KeyPresent={KeyPresent}", title, type, string.IsNullOrWhiteSpace(apiKey) ? url : url.Replace($"&key={apiKey}", "&key=***"), !string.IsNullOrWhiteSpace(apiKey));

            // Google's backend intermittently returns 503/429; retry transient failures.
            HttpResponseMessage resp;
            for (var attempt = 1; ; attempt++)
            {
                resp = await http.GetAsync(url, ct);
                var status = (int)resp.StatusCode;
                var transient = status is 429 or 500 or 502 or 503;
                if (!transient || attempt >= 3) break;
                resp.Dispose();
                logger.LogWarning("GoogleBooks transient HTTP {Status} for {Title}, retry {Attempt}/2", status, title, attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
            using (resp)
            {
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var items = doc.RootElement.TryGetProperty("items", out var itemsEl) ? itemsEl : default;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return new Dictionary<string,string>();

            // Prefer an item whose title matches the searched title, then one with a description.
            var chosen = PickBestItem(items, cleaned);
            var volumeInfo = chosen.GetProperty("volumeInfo");
            var dict = new Dictionary<string,string>();
            if (volumeInfo.TryGetProperty("title", out var ti)) dict["Title"] = ti.GetString() ?? string.Empty;
            if (volumeInfo.TryGetProperty("subtitle", out var sub) && !string.IsNullOrWhiteSpace(sub.GetString())) dict["Subtitle"] = sub.GetString() ?? string.Empty;
            if (volumeInfo.TryGetProperty("authors", out var authors) && authors.ValueKind==JsonValueKind.Array) dict["Authors"] = string.Join(", ", authors.EnumerateArray().Select(a=>a.GetString()));
            if (volumeInfo.TryGetProperty("description", out var desc)) dict["Description"] = HtmlCleaner.StripHtml(desc.GetString());
            if (volumeInfo.TryGetProperty("publishedDate", out var pub)) dict["PublishedDate"] = pub.GetString() ?? string.Empty;
            if (volumeInfo.TryGetProperty("publisher", out var publisher)) dict["Publisher"] = publisher.GetString() ?? string.Empty;
            if (volumeInfo.TryGetProperty("pageCount", out var pages) && pages.ValueKind==JsonValueKind.Number) dict["PageCount"] = pages.GetInt32().ToString();
            if (volumeInfo.TryGetProperty("categories", out var cats) && cats.ValueKind==JsonValueKind.Array) dict["Categories"] = string.Join(", ", cats.EnumerateArray().Select(c=>c.GetString()).Where(s=>!string.IsNullOrWhiteSpace(s)));
            if (volumeInfo.TryGetProperty("language", out var lang)) dict["Language"] = lang.GetString() ?? string.Empty;
            if (volumeInfo.TryGetProperty("industryIdentifiers", out var ids) && ids.ValueKind==JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                {
                    if (id.TryGetProperty("type", out var t) && id.TryGetProperty("identifier", out var val))
                    {
                        var typeStr = t.GetString();
                        if (!string.IsNullOrWhiteSpace(typeStr))
                        {
                            if (typeStr.Contains("ISBN_13")) dict["ISBN13"] = val.GetString() ?? string.Empty;
                            else if (typeStr.Contains("ISBN_10")) dict["ISBN10"] = val.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            if (chosen.TryGetProperty("searchInfo", out var searchInfo) && searchInfo.TryGetProperty("textSnippet", out var snippet)) dict["Snippet"] = HtmlCleaner.StripHtml(snippet.GetString());
            if (volumeInfo.TryGetProperty("infoLink", out var link)) dict["SourceUrl"] = link.GetString() ?? string.Empty;
            dict["Source"] = "GoogleBooks";
            return dict;
            }
        }
        catch (Exception ex)
        {
            if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } && string.IsNullOrWhiteSpace(apiKey))
                logger.LogWarning(
                    "GoogleBooks keyless quota exhausted (HTTP 429) for {Title}. Set GOOGLE_BOOKS_KEY to use a dedicated quota.", title);
            else
                logger.LogWarning(ex, "GoogleBooks fetch failed for {Title} Type={Type}", title, type);
            return new Dictionary<string,string>();
        }
    }

    /// <summary>
    /// Scores candidates: title starting with the searched title (+4), containing it (+2),
    /// having a description (+1). Falls back to the first item with a description, then items[0].
    /// </summary>
    private static JsonElement PickBestItem(JsonElement items, string searchTitle)
    {
        var needle = Normalize(searchTitle);
        var chosen = items[0];
        var bestScore = -1;
        foreach (var it in items.EnumerateArray())
        {
            if (!it.TryGetProperty("volumeInfo", out var vi)) continue;
            var itemTitle = vi.TryGetProperty("title", out var te) ? te.GetString() ?? string.Empty : string.Empty;
            var hasDesc = vi.TryGetProperty("description", out var d) && !string.IsNullOrWhiteSpace(d.GetString());
            var score = 0;
            var norm = Normalize(itemTitle);
            if (needle.Length > 0)
            {
                if (norm.StartsWith(needle, StringComparison.Ordinal)) score += 4;
                else if (norm.Contains(needle, StringComparison.Ordinal)) score += 2;
            }
            if (hasDesc) score += 1;
            if (score > bestScore)
            {
                bestScore = score;
                chosen = it;
            }
        }
        return chosen;

        static string Normalize(string s) =>
            string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
