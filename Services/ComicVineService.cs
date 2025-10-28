using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;

namespace PrepKavitaPdf.Services;

public class ComicVineService
{
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly IMemoryCache cache;

    public ComicVineService(HttpClient http, IConfiguration config, IMemoryCache cache)
    {
        this.http = http;
        apiKey = config["PdfLibrary:ComicVine:ApiKey"] ?? string.Empty;
        this.cache = cache;
    }

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Comic) return new();

        var cacheKey = $"ComicVine:{type}:{title}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string,string>? cached)) return cached;

        try
        {
            var url = $"search/?api_key={apiKey}&format=json&query={Uri.EscapeDataString(title)}&resources=volume";
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength()==0)
            {
                var empty = new Dictionary<string,string>();
                cache.Set(cacheKey, empty, TimeSpan.FromMinutes(10));
                return empty;
            }
            var first = results[0];
            var dict = new Dictionary<string,string>();
            if (first.TryGetProperty("name", out var name)) dict["Title"] = name.GetString() ?? "";
            if (first.TryGetProperty("description", out var desc)) dict["Description"] = desc.GetString() ?? "";
            if (first.TryGetProperty("site_detail_url", out var site)) dict["SourceUrl"] = site.GetString() ?? "";
            dict["Source"] = "ComicVine";

            cache.Set(cacheKey, dict, TimeSpan.FromMinutes(10));
            return dict;
        }
        catch
        {
            var empty = new Dictionary<string,string>();
            cache.Set(cacheKey, empty, TimeSpan.FromMinutes(10));
            return empty;
        }
    }
}
