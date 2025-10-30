using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

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
    staff(perPage: 5) { edges { role node { name { full } } } }
  }
}",
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
            if (media.TryGetProperty("description", out var desc)) dict["Description"] = Clean(desc.GetString());
            if (media.TryGetProperty("siteUrl", out var site)) dict["SourceUrl"] = site.GetString() ?? string.Empty;
            if (media.TryGetProperty("format", out var fmt)) dict["Format"] = fmt.GetString() ?? string.Empty;
            if (media.TryGetProperty("status", out var st)) dict["Status"] = st.GetString() ?? string.Empty;
            if (media.TryGetProperty("averageScore", out var score) && score.ValueKind == JsonValueKind.Number) dict["AverageScore"] = score.GetInt32().ToString();
            if (media.TryGetProperty("volumes", out var vols) && vols.ValueKind == JsonValueKind.Number) dict["Volumes"] = vols.GetInt32().ToString();
            if (media.TryGetProperty("chapters", out var ch) && ch.ValueKind == JsonValueKind.Number) dict["Chapters"] = ch.GetInt32().ToString();
            if (media.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array) dict["Genres"] = string.Join(", ", genres.EnumerateArray().Select(g=>g.GetString()).Where(s=>!string.IsNullOrWhiteSpace(s)));
            if (media.TryGetProperty("startDate", out var sd)) dict["StartDate"] = BuildDate(sd);
            if (media.TryGetProperty("endDate", out var ed)) dict["EndDate"] = BuildDate(ed);
            if (media.TryGetProperty("staff", out var staff))
            {
                try
                {
                    var names = staff.GetProperty("edges").EnumerateArray()
                        .Where(e => e.TryGetProperty("node", out _))
                        .Select(e => {
                            var node = e.GetProperty("node");
                            if (node.TryGetProperty("name", out var nameObj) && nameObj.TryGetProperty("full", out var full)) return full.GetString();
                            return null;
                        })
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .ToList();
                    if (names.Count > 0 && !dict.ContainsKey("Authors")) dict["Authors"] = string.Join(", ", names);
                }
                catch { }
            }
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

    private static string? Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return v;
        v = Regex.Replace(v, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        v = Regex.Replace(v, @"<[^>]+>", string.Empty); // strip tags
        return System.Net.WebUtility.HtmlDecode(v).Trim();
    }

    private static string BuildDate(JsonElement el)
    {
        try
        {
            var year = el.TryGetProperty("year", out var y) && y.ValueKind==JsonValueKind.Number ? y.GetInt32().ToString("D4") : "";
            var month = el.TryGetProperty("month", out var m) && m.ValueKind==JsonValueKind.Number ? m.GetInt32().ToString("D2") : "";
            var day = el.TryGetProperty("day", out var d) && d.ValueKind==JsonValueKind.Number ? d.GetInt32().ToString("D2") : "";
            var parts = new[]{year, month, day}.Where(p=>!string.IsNullOrEmpty(p)).ToArray();
            return parts.Length == 0 ? string.Empty : string.Join('-', parts);
        }
        catch { return string.Empty; }
    }
}
