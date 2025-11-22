using System.Text.Json;
using PrepKavitaPdf.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Services;

public class ComicVineService(
    HttpClient http,
    IConfiguration config,
    ILogger<ComicVineService> logger)
{
    private readonly string apiKey = config["PdfLibrary:ComicVine:ApiKey"] ?? string.Empty;

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Comic) return new Dictionary<string,string>();

        var cacheKey = $"ComicVine:{type}:{title}";
        
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
                return empty;
            }
            var first = results[0];
            var dict = new Dictionary<string,string>();
            if (first.TryGetProperty("name", out var name)) dict["Title"] = name.GetString() ?? string.Empty;
            if (first.TryGetProperty("description", out var desc)) dict["Description"] = Clean(desc.GetString());
            if (first.TryGetProperty("site_detail_url", out var site)) dict["SourceUrl"] = site.GetString() ?? string.Empty;
            if (first.TryGetProperty("start_year", out var startYear) && startYear.ValueKind==JsonValueKind.Number) dict["StartYear"] = startYear.GetInt32().ToString();
            if (first.TryGetProperty("count_of_issues", out var issues) && issues.ValueKind==JsonValueKind.Number) dict["IssueCount"] = issues.GetInt32().ToString();
            if (first.TryGetProperty("publisher", out var publisher) && publisher.ValueKind==JsonValueKind.Object && publisher.TryGetProperty("name", out var pubName)) dict["Publisher"] = pubName.GetString() ?? string.Empty;
            if (first.TryGetProperty("api_detail_url", out var apiUrl)) dict["ApiDetailUrl"] = apiUrl.GetString() ?? string.Empty;
            dict["Source"] = "ComicVine";

            logger.LogInformation("ComicVine response mapped for {Title}. Keys={Keys}", title, string.Join(',', dict.Keys));
            return dict;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ComicVine fetch failed for {Title} Type={Type}", title, type);
            var empty = new Dictionary<string,string>();
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
