namespace BooksMetadataBaker.Services;

public class AggregatedMetadataService(
    AniListService ani,
    GoogleBooksService google,
    ComicVineService comic,
    ILogger<AggregatedMetadataService> logger,
    IConfiguration config)
    : IAggregatedMetadataService
{
    private readonly string preferredTitleVariant = (config["Tools:PreferredTitleVariant"] ?? "English").Trim().ToLowerInvariant();

    public async Task<Dictionary<string,string>> FetchMetadataAsync(string title, BookType type, string? volumeToken = null, CancellationToken ct = default)
    {
        logger.LogInformation("Fetching aggregated metadata for Title={Title} Type={Type} VolumeToken={Volume} PrefVariant={Pref}", title, type, volumeToken, preferredTitleVariant);
        
        var baseMeta = await TryAllSourcesAsync(title, type, ct);

        NormalizeTitles(baseMeta, title);

        return baseMeta;
    }

    private async Task<Dictionary<string,string>> TryAllSourcesAsync(string searchTitle, BookType type, CancellationToken ct)
    {
        var tasks = new[]
        {
            ani.TryFetchAsync(searchTitle, type, ct),
            google.TryFetchAsync(searchTitle, type, ct),
            comic.TryFetchAsync(searchTitle, type, ct)
        };
        var results = await Task.WhenAll(tasks);
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

    private static void NormalizeTitles(Dictionary<string,string> meta, string fallbackTitle)
    {
        var english = meta.TryGetValue("TitleEnglish", out var te) ? te : null;
        var romaji = meta.TryGetValue("TitleRomaji", out var tr) ? tr : null;
        var native = meta.TryGetValue("TitleNative", out var tn) ? tn : null;
        var plain = meta.TryGetValue("Title", out var tPlain) ? tPlain : null;
        
        meta["Title"] = plain ?? fallbackTitle;
        meta["TitleEnglish"] = english ?? fallbackTitle;
        meta["TitleRomaji"] = romaji ?? fallbackTitle;
        meta["TitleNative"] = native ?? fallbackTitle;
    }
}
