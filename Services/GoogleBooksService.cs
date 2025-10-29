using System.Net.Http.Json;
using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PrepKavitaPdf.Services;

public class GoogleBooksService
{
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly IMemoryCache cache;
    private readonly ILogger<GoogleBooksService> logger;

    public GoogleBooksService(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<GoogleBooksService> logger)
    {
        this.http = http;
        apiKey = config["PdfLibrary:GoogleBooks:ApiKey"] ?? string.Empty;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Book && type is not BookType.LightNovel) return new();

        var cacheKey = $"GoogleBooks:{type}:{title}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string,string>? cached))
        {
            logger.LogDebug("GoogleBooks cache hit for {Title} Type={Type}", title, type);
            return cached;
        }

        try
        {
            var url = $"?q={Uri.EscapeDataString(title)}&maxResults=1&key={apiKey}";
            logger.LogInformation("GoogleBooks request for {Title} Type={Type} Url={Url}", title, type, url);
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
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
            if (volumeInfo.TryGetProperty("title", out var ti)) dict["Title"] = ti.GetString() ?? "";
            if (volumeInfo.TryGetProperty("authors", out var authors) && authors.ValueKind==JsonValueKind.Array) dict["Authors"] = string.Join(", ", authors.EnumerateArray().Select(a=>a.GetString()));
            if (volumeInfo.TryGetProperty("description", out var desc)) dict["Description"] = desc.GetString() ?? "";
            if (volumeInfo.TryGetProperty("publishedDate", out var pub)) dict["PublishedDate"] = pub.GetString() ?? "";
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
}
