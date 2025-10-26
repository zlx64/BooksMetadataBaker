using System.Text.Json;
using PrepKavitaPdf.Models;

namespace PrepKavitaPdf.Services;

public class ComicVineService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public ComicVineService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["PdfLibrary:ComicVine:ApiKey"] ?? string.Empty;
    }

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Comic) return new();
        try
        {
            var url = $"search/?api_key={_apiKey}&format=json&query={Uri.EscapeDataString(title)}&resources=volume";
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength()==0) return new();
            var first = results[0];
            var dict = new Dictionary<string,string>();
            if (first.TryGetProperty("name", out var name)) dict["Title"] = name.GetString() ?? "";
            if (first.TryGetProperty("description", out var desc)) dict["Description"] = desc.GetString() ?? "";
            if (first.TryGetProperty("site_detail_url", out var site)) dict["SourceUrl"] = site.GetString() ?? "";
            dict["Source"] = "ComicVine";
            return dict;
        }
        catch
        {
            return new();
        }
    }
}
