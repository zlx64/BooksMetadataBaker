using System.Net.Http.Json;
using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;

namespace PrepKavitaPdf.Services;

public class AniListService
{
    private readonly HttpClient http;
    private readonly IMemoryCache cache;

    public AniListService(HttpClient http, IMemoryCache cache)
    {
        this.http = http;
        this.cache = cache;
    }

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        // Only attempt for Manga or LightNovel
        if (type is not BookType.Manga && type is not BookType.LightNovel) return new();

        var cacheKey = $"AniList:{type}:{title}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string,string>? cached)) return cached;

        // AniList only distinguishes ANIME and MANGA. Light novels are MANGA with format NOVEL.
        var mediaType = "MANGA"; // GraphQL enum MediaType
        string? format = type == BookType.LightNovel ? "NOVEL" : null; // GraphQL enum MediaFormat

        var queryObj = new
        {
            query = "query ($search: String, $type: MediaType, $format: MediaFormat) { Media(search: $search, type: $type, format: $format) { id title { romaji english native } description siteUrl format type } }",
            variables = new { search = title, type = mediaType, format = format }
        };
        try
        {
            using var resp = await http.PostAsJsonAsync("", queryObj, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var media = doc.RootElement.GetProperty("data").GetProperty("Media");
            var dict = new Dictionary<string,string>();
            if (media.TryGetProperty("title", out var titleObj))
            {
                if (titleObj.TryGetProperty("english", out var eng) && !string.IsNullOrWhiteSpace(eng.GetString())) dict["TitleEnglish"] = eng.GetString() ?? "";
                if (titleObj.TryGetProperty("romaji", out var romaji) && !string.IsNullOrWhiteSpace(romaji.GetString())) dict["TitleRomaji"] = romaji.GetString() ?? "";
                if (titleObj.TryGetProperty("native", out var native) && !string.IsNullOrWhiteSpace(native.GetString())) dict["TitleNative"] = native.GetString() ?? "";
            }
            if (media.TryGetProperty("description", out var desc)) dict["Description"] = desc.GetString() ?? "";
            if (media.TryGetProperty("siteUrl", out var site)) dict["SourceUrl"] = site.GetString() ?? "";
            if (media.TryGetProperty("format", out var fmt)) dict["Format"] = fmt.GetString() ?? "";
            dict["Source"] = "AniList";

            // Cache for 10 minutes (including empty result to avoid repeated calls)
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
