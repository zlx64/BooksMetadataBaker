using System.Collections.Concurrent;
using BooksMetadataBaker.Services.Helpers;

namespace BooksMetadataBaker.Services;

public class KavitaMetadataWriter(ILogger<KavitaMetadataWriter> logger) : IKavitaMetadataWriter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task WriteAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir is null) return;

        var kavitaPath = Path.Combine(dir, "series.json");
        var title = MetadataHelpers.GetFirst(metadata, fallbackTitle, "Title", "TitleRomaji", "TitleEnglish", "TitleNative");
        var altTitles = MetadataHelpers.CollectAlternateTitles(metadata, title);
        var authors = MetadataHelpers.SplitAuthors(metadata, "Authors");
        var genres = MetadataHelpers.GetGenres(metadata);
        var tags = MetadataHelpers.GetTags(metadata);
        var year = MetadataHelpers.ExtractYear(metadata);
        var ageRating = MetadataHelpers.InferAgeRating(metadata, genres, tags);
        var format = metadata.TryGetValue("Format", out var fmt) ? fmt : string.Empty;

        var updates = new Dictionary<string, object?>
        {
            ["Title"] = title,
            ["LocalizedTitles"] = metadata
                .Where(d => new[] { "Title", "TitleRomaji", "TitleEnglish", "TitleNative" }.Contains(d.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ["AlternativeTitles"] = altTitles,
            ["Summary"] = metadata.TryGetValue("Description", out var desc) ? desc : string.Empty,
            ["Publisher"] = metadata.TryGetValue("Publisher", out var pub) ? pub : string.Empty,
            ["ReleaseYear"] = year,
            ["Format"] = format,
            ["Genres"] = genres,
            ["Tags"] = tags,
            ["Language"] = metadata.TryGetValue("Language", out var lang) ? lang : string.Empty,
            ["AgeRating"] = ageRating,
            ["Authors"] = authors,
            ["Artists"] = new List<string>(),
            ["Translators"] = new List<string>(),
            ["Editors"] = new List<string>(),
            ["Characters"] = new List<string>(),
            ["Imprint"] = string.Empty,
            ["Source"] = metadata.TryGetValue("Source", out var src) ? src : string.Empty,
            ["SourceUrl"] = metadata.TryGetValue("SourceUrl", out var su) ? su : null
        };

        var lockObj = DirectoryLocks.GetOrAdd(dir, _ => new SemaphoreSlim(1, 1));
        await lockObj.WaitAsync();
        try
        {
            Dictionary<string, object?>? existing = null;
            if (File.Exists(kavitaPath))
            {
                try
                {
                    existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(await File.ReadAllTextAsync(kavitaPath));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Existing series.json is not valid JSON; overwriting: {Path}", kavitaPath);
                }
            }

            var merged = existing ?? new Dictionary<string, object?>();
            foreach (var kv in updates)
                merged[kv.Key] = kv.Value;

            var json = JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = kavitaPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, kavitaPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing Kavita series metadata for {File}", filePath);
        }
        finally
        {
            lockObj.Release();
        }
    }
}
