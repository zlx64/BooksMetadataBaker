using BooksMetadataBaker.Services.Helpers;

namespace BooksMetadataBaker.Services;

public class KavitaMetadataWriter(ILogger<KavitaMetadataWriter> logger) : IKavitaMetadataWriter
{
    public void Write(string filePath, IDictionary<string, string> metadata, string fallbackTitle)
    {
        try
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

            var obj = new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["LocalizedTitles"] = metadata.Where(d => new[] { "Title", "TitleRomaji", "TitleEnglish", "TitleNative" }.Contains(d.Key)),
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

            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(kavitaPath, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing Kavita series metadata for {File}", filePath);
        }
    }
}
