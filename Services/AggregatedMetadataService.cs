namespace BooksMetadataBaker.Services;

public class AggregatedMetadataService(
    IEnumerable<IMetadataSource> sources,
    ILogger<AggregatedMetadataService> logger,
    IConfiguration config)
    : IAggregatedMetadataService
{
    private static readonly string DefaultSourceOrder = "AniList,GoogleBooks,ComicVine";

    private readonly List<IMetadataSource> orderedSources = OrderSources(sources, config);

    public async Task<Dictionary<string, string>> FetchMetadataAsync(string title, BookType type, string? volumeToken = null, CancellationToken ct = default)
    {
        logger.LogInformation("Fetching aggregated metadata for Title={Title} Type={Type} VolumeToken={Volume} SourceOrder={Order}",
            title, type, volumeToken, string.Join(",", orderedSources.Select(s => s.GetType().Name)));

        var baseMeta = await TryAllSourcesAsync(title, type, ct);

        NormalizeTitles(baseMeta, title);

        return baseMeta;
    }

    private async Task<Dictionary<string, string>> TryAllSourcesAsync(string searchTitle, BookType type, CancellationToken ct)
    {
        var results = await Task.WhenAll(orderedSources.Select(s => s.TryFetchAsync(searchTitle, type, ct)));

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private static List<IMetadataSource> OrderSources(IEnumerable<IMetadataSource> sources, IConfiguration config)
    {
        var order = (config["Tools:SourceOrder"] ?? DefaultSourceOrder)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return sources
            .OrderBy(s =>
            {
                var name = s.GetType().Name;
                var idx = Array.FindIndex(order, o => name.StartsWith(o, StringComparison.OrdinalIgnoreCase));
                return idx == -1 ? int.MaxValue : idx;
            })
            .ToList();
    }

    private static void NormalizeTitles(Dictionary<string, string> meta, string fallbackTitle)
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
