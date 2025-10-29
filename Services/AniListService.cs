using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;

namespace PrepKavitaPdf.Services;

public class AniListService(HttpClient http, IMemoryCache cache, ILogger<AniListService> logger)
{
    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        // Only attempt for Manga or LightNovel
        if (type is not BookType.Manga && type is not BookType.LightNovel) return new Dictionary<string,string>();

        var cacheKey = $"AniList:{type}:{title}";
        if (cache.TryGetValue(cacheKey, out Dictionary<string,string>? cached) && cached is not null)
        {
            logger.LogDebug("AniList cache hit for {Title} Type={Type}", title, type);
            return cached;
        }

        var mediaType = "MANGA"; // GraphQL enum MediaType
        var format = type == BookType.LightNovel ? "NOVEL" : null; // GraphQL enum MediaFormat

        var queryObj = new
        {
            query = "query ($search: String, $type: MediaType, $format: MediaFormat) { Media(search: $search, type: $type, format: $format) { id title { romaji english native } description siteUrl format type } }",
            variables = new { search = title, type = mediaType, format = format }
        };
        try
        {
            logger.LogInformation("AniList request for {Title} Type={Type} Format={Format}", title, type, format);
            using var resp = await http.PostAsJsonAsync("", queryObj, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var media = doc.RootElement.GetProperty("data").GetProperty("Media");
            var dict = new Dictionary<string,string>();
            if (media.TryGetProperty("title", out var titleObj))
            {
                if (titleObj.TryGetProperty("english", out var eng) && !string.IsNullOrWhiteSpace(eng.GetString())) dict["TitleEnglish"] = eng.GetString() ?? string.Empty;
                if (titleObj.TryGetProperty("romaji", out var romaji) && !string.IsNullOrWhiteSpace(romaji.GetString())) dict["TitleRomaji"] = romaji.GetString() ?? string.Empty;
                if (titleObj.TryGetProperty("native", out var native) && !string.IsNullOrWhiteSpace(native.GetString())) dict["TitleNative"] = native.GetString() ?? string.Empty;
            }
            if (media.TryGetProperty("description", out var desc)) dict["Description"] = desc.GetString() ?? string.Empty;
            if (media.TryGetProperty("siteUrl", out var site)) dict["SourceUrl"] = site.GetString() ?? string.Empty;
            if (media.TryGetProperty("format", out var fmt)) dict["Format"] = fmt.GetString() ?? string.Empty;
            dict["Source"] = "AniList";

            cache.Set(cacheKey, dict, TimeSpan.FromMinutes(10));
            logger.LogInformation("AniList response mapped for {Title}. Keys={Keys}", title, string.Join(',', dict.Keys));
            return dict;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AniList fetch failed for {Title} Type={Type}", title, type);
            var empty = new Dictionary<string,string>();
            cache.Set(cacheKey, empty, TimeSpan.FromMinutes(10));
            return empty;
        }
    }
}
