using System.Text.Json;
using PrepKavitaPdf.Models;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Services;

public class GoogleBooksService(
    HttpClient http,
    IConfiguration config,
    ILogger<GoogleBooksService> logger)
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
            var url = $"?q={Uri.EscapeDataString(queryExpression)}&maxResults=3&printType=books&projection=full" + (string.IsNullOrWhiteSpace(langRestrict)?"":$"&langRestrict={langRestrict}") + (string.IsNullOrWhiteSpace(apiKey)?"":$"&key={apiKey}");
            logger.LogInformation("GoogleBooks request for {Title} Type={Type} Url={Url}", title, type, url);
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var items = doc.RootElement.TryGetProperty("items", out var itemsEl) ? itemsEl : default;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return new Dictionary<string,string>();

            // Pick first item having description
            var chosen = items[0];
            foreach (var it in items.EnumerateArray())
            {
                if (it.TryGetProperty("volumeInfo", out var vi) && vi.TryGetProperty("description", out var d) && !string.IsNullOrWhiteSpace(d.GetString()))
                { chosen = it; break; }
            }
            var volumeInfo = chosen.GetProperty("volumeInfo");
            var dict = new Dictionary<string,string>();
            if (volumeInfo.TryGetProperty("title", out var ti)) dict["Title"] = ti.GetString() ?? string.Empty;
            if (volumeInfo.TryGetProperty("subtitle", out var sub) && !string.IsNullOrWhiteSpace(sub.GetString())) dict["Subtitle"] = sub.GetString() ?? string.Empty;
            if (volumeInfo.TryGetProperty("authors", out var authors) && authors.ValueKind==JsonValueKind.Array) dict["Authors"] = string.Join(", ", authors.EnumerateArray().Select(a=>a.GetString()));
            if (volumeInfo.TryGetProperty("description", out var desc)) dict["Description"] = Clean(desc.GetString());
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
            if (chosen.TryGetProperty("searchInfo", out var searchInfo) && searchInfo.TryGetProperty("textSnippet", out var snippet)) dict["Snippet"] = Clean(snippet.GetString());
            if (volumeInfo.TryGetProperty("infoLink", out var link)) dict["SourceUrl"] = link.GetString() ?? string.Empty;
            dict["Source"] = "GoogleBooks";
            return dict;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GoogleBooks fetch failed for {Title} Type={Type}", title, type);
            return new Dictionary<string,string>();
        }
    }

    private static string? Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return v;
        v = Regex.Replace(v, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        v = Regex.Replace(v, @"<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(v).Trim();
    }
}
