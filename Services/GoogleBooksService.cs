using System.Net.Http.Json;
using System.Text.Json;
using PrepKavitaPdf.Models;

namespace PrepKavitaPdf.Services;

public class GoogleBooksService
{
    private readonly HttpClient http;
    private readonly string apiKey;

    public GoogleBooksService(HttpClient http, IConfiguration config)
    {
        this.http = http;
        apiKey = config["PdfLibrary:GoogleBooks:ApiKey"] ?? string.Empty;
    }

    public async Task<Dictionary<string,string>> TryFetchAsync(string title, BookType type, CancellationToken ct)
    {
        if (type is not BookType.Book && type is not BookType.LightNovel) return new();
        try
        {
            var url = $"?q={Uri.EscapeDataString(title)}&maxResults=1&key={apiKey}";
            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var items = doc.RootElement.TryGetProperty("items", out var itemsEl) ? itemsEl : default;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return new();
            var volumeInfo = items[0].GetProperty("volumeInfo");
            var dict = new Dictionary<string,string>();
            if (volumeInfo.TryGetProperty("title", out var ti)) dict["Title"] = ti.GetString() ?? "";
            if (volumeInfo.TryGetProperty("authors", out var authors) && authors.ValueKind==JsonValueKind.Array) dict["Authors"] = string.Join(", ", authors.EnumerateArray().Select(a=>a.GetString()));
            if (volumeInfo.TryGetProperty("description", out var desc)) dict["Description"] = desc.GetString() ?? "";
            if (volumeInfo.TryGetProperty("publishedDate", out var pub)) dict["PublishedDate"] = pub.GetString() ?? "";
            dict["Source"] = "GoogleBooks";
            return dict;
        }
        catch
        {
            return new();
        }
    }
}
