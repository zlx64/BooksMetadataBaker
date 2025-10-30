using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Services;

public class GoogleBooksService(
    HttpClient http,
    IConfiguration config,
    IMemoryCache cache,
    ILogger<GoogleBooksService> logger)
{
    private readonly string apiKey = config["PdfLibrary:GoogleBooks:ApiKey"] ?? string.Empty;

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Book && type is not BookType.LightNovel) return new Dictionary<string,string>();

        var cacheKey = $"GoogleBooks:{type}:{title}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string,string>? cached) && cached is not null)
        {
            logger.LogDebug("GoogleBooks cache hit for {Title} Type={Type}", title, type);
            return cached;
        }

        try
        {
            var url = $"?q={Uri.EscapeDataString(title)}&maxResults=1&printType=books&projection=full&key={apiKey}";
            logger.LogInformation("GoogleBooks request for {Title} Type={Type} Url={Url}", title, type, url);
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var items = doc.RootElement.TryGetProperty("items", out var itemsEl) ? itemsEl : default;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            {
                logger.LogInformation("GoogleBooks no results for {Title}", title);
                var empty = new Dictionary<string,string>();
                cache.Set(cacheKey, empty, TimeSpan.FromMinutes(10));
                return empty;
            }
            var volumeInfo = items[0].GetProperty("volumeInfo");
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
            if (items[0].TryGetProperty("searchInfo", out var searchInfo) && searchInfo.TryGetProperty("textSnippet", out var snippet)) dict["Snippet"] = Clean(snippet.GetString());
            if (volumeInfo.TryGetProperty("infoLink", out var link)) dict["SourceUrl"] = link.GetString() ?? string.Empty;
            dict["Source"] = "GoogleBooks";

            cache.Set(cacheKey, dict, TimeSpan.FromMinutes(10));
            logger.LogInformation("GoogleBooks response mapped for {Title}. Keys={Keys}", title, string.Join(',', dict.Keys));
            return dict;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GoogleBooks fetch failed for {Title} Type={Type}", title, type);
            var empty = new Dictionary<string,string>();
            cache.Set(cacheKey, empty, TimeSpan.FromMinutes(10));
            return empty;
        }
    }

    private static string? Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return v;
        v = Regex.Replace(v, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        v = Regex.Replace(v, @"<[^>]+>", string.Empty); // strip tags
        return System.Net.WebUtility.HtmlDecode(v).Trim();
    }
}
