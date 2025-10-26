using PrepKavitaPdf.Models;

namespace PrepKavitaPdf.Services;

public class AggregatedMetadataService : IAggregatedMetadataService
{
    private readonly AniListService _ani;
    private readonly GoogleBooksService _google;
    private readonly ComicVineService _comic;

    public AggregatedMetadataService(AniListService ani, GoogleBooksService google, ComicVineService comic)
    {
        _ani = ani; _google = google; _comic = comic;
    }

    public async Task<Dictionary<string,string>> FetchMetadataAsync(string title, BookType type, CancellationToken ct = default)
    {
        var tasks = new[]
        {
            _ani.TryFetchAsync(title, type, ct),
            _google.TryFetchAsync(title, type, ct),
            _comic.TryFetchAsync(title, type, ct)
        };
        var results = await Task.WhenAll(tasks);
        // merge preferring first non-empty values
        var merged = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in results)
        {
            foreach (var kv in dict)
            {
                if (!merged.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    merged[kv.Key] = kv.Value;
            }
        }
        return merged;
    }
}
