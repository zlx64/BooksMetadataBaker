using PrepKavitaPdf.Models;

namespace PrepKavitaPdf.Services;

public class AggregatedMetadataService(
    AniListService ani,
    GoogleBooksService google,
    ComicVineService comic,
    ILogger<AggregatedMetadataService> logger)
    : IAggregatedMetadataService
{
    public async Task<Dictionary<string,string>> FetchMetadataAsync(string title, BookType type, CancellationToken ct = default)
    {
        logger.LogInformation("Fetching aggregated metadata for Title={Title} Type={Type}", title, type);
        var tasks = new[]
        {
            ani.TryFetchAsync(title, type, ct),
            google.TryFetchAsync(title, type, ct),
            comic.TryFetchAsync(title, type, ct)
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
        logger.LogInformation("Aggregated metadata for Title={Title}: Keys={Keys}", title, string.Join(',', merged.Keys));
        return merged;
    }
}
