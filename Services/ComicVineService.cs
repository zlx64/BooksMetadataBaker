using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;

namespace PrepKavitaPdf.Services;

public class ComicVineService(
    HttpClient http,
    IConfiguration config,
    IMemoryCache cache,
    ILogger<ComicVineService> logger)
{
    private readonly string apiKey = config["PdfLibrary:ComicVine:ApiKey"] ?? string.Empty;

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Comic) return new Dictionary<string,string>();

        var cacheKey = $"ComicVine:{type}:{title}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string,string>? cached) && cached is not null)
        {
            logger.LogDebug("ComicVine cache hit for {Title} Type={Type}", title, type);
            return cached;
        }

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
                var empty = new Dictionary<string,string>();
                cache.Set(cacheKey, empty, TimeSpan.FromMinutes(10));
                return empty;
            }
            var first = results[0];
            var dict = new Dictionary<string,string>();
            if (first.TryGetProperty("name", out var name)) dict["Title"] = name.GetString() ?? string.Empty;
            if (first.TryGetProperty("description", out var desc)) dict["Description"] = desc.GetString() ?? string.Empty;
            if (first.TryGetProperty("site_detail_url", out var site)) dict["SourceUrl"] = site.GetString() ?? string.Empty;
            dict["Source"] = "ComicVine";

            cache.Set(cacheKey, dict, TimeSpan.FromMinutes(10));
            logger.LogInformation("ComicVine response mapped for {Title}. Keys={Keys}", title, string.Join(',', dict.Keys));
            return dict;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ComicVine fetch failed for {Title} Type={Type}", title, type);
            var empty = new Dictionary<string,string>();
            cache.Set(cacheKey, empty, TimeSpan.FromMinutes(10));
            return empty;
        }
    }
}
