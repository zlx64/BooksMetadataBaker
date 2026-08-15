using System.Globalization;
using BooksMetadataBaker.Services.Helpers;

namespace BooksMetadataBaker.Services;

public static class CalibreMetadataUpdater
{
    public const int EbookMetaTimeoutMs = 120_000;
    private static readonly string[] EbookMetaFallbackNames = ["ebook-meta"];

    public static string? ResolveEbookMeta(string? configuredPath) =>
        ToolResolver.Resolve(configuredPath, EbookMetaFallbackNames);

    public static async Task<(bool Ok, string? Error)> TryCleanEBookMetadataAsync(
        string path,
        string? ebookMetaPath,
        ILogger logger,
        CancellationToken ct)
    {
        var exe = ResolveEbookMeta(ebookMetaPath);
        if (exe is null)
            return (false, "ebook-meta not found");

        var (ok, _, stdout, stderr, runErr) = await ProcessRunner.RunAsync(
            exe, BuildEbookMetaCleanArgs(path), logger, EbookMetaTimeoutMs, ct);

        if (runErr != null)
        {
            logger.LogWarning("ebook-meta cleanup run error for {File}: {Error}", path, runErr);
            return (false, runErr);
        }
        if (!ok)
            return (false, string.IsNullOrWhiteSpace(stderr + stdout)
                ? "ebook-meta cleanup failed"
                : (stderr + stdout).Trim());
        return (true, null);
    }

    public static async Task<(bool Ok, string? Error)> TryWriteMetadataWithCalibreAsync(
        string path,
        string? ebookMetaPath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        ILogger logger,
        CancellationToken ct)
    {
        var exe = ResolveEbookMeta(ebookMetaPath);
        if (exe is null)
            return (false, "ebook-meta not found");

        var (ok, _, stdout, stderr, runErr) = await ProcessRunner.RunAsync(
            exe, BuildEbookMetaArgs(path, metadata, fallbackTitle), logger, EbookMetaTimeoutMs, ct);

        if (runErr != null)
        {
            logger.LogError("ebook-meta run error for {File}: {Error}", path, runErr);
            return (false, runErr);
        }
        if (!ok)
            return (false, string.IsNullOrWhiteSpace(stderr + stdout)
                ? "ebook-meta failed"
                : (stderr + stdout).Trim());
        return (true, null);
    }

    private static List<string> BuildEbookMetaCleanArgs(string filePath)
    {
        var fields = new[] { "--title", "--authors", "--comments", "--tags", "--series", "--publisher", "--isbn", "--language" };
        var parts = new List<string>();
        foreach (var field in fields)
        {
            parts.Add(field);
            parts.Add(string.Empty);
        }
        parts.Add(filePath);
        return parts;
    }

    private static List<string> BuildEbookMetaArgs(string filePath, IDictionary<string, string> meta, string fallbackTitle)
    {
        var parts = new List<string>();
        var newTitle = Path.GetFileNameWithoutExtension(filePath);
        Add("--title", newTitle);
        Add("--series", fallbackTitle);
        var idx = MetadataHelpers.ParseVolumeNumber(newTitle);
        if (idx != null) Add("--index", idx.Value.ToString(CultureInfo.InvariantCulture));
        var rating = MetadataHelpers.GetFirst(meta, string.Empty, "AverageScore", "Rating", "UserRating");
        Add("--rating", rating);
        var desc = MetadataHelpers.GetFirst(meta, string.Empty, "Description", "Snippet");
        Add("--comments", desc);
        var publisher = MetadataHelpers.GetFirst(meta, string.Empty, "Publisher");
        Add("--publisher", publisher);
        var dateRaw = MetadataHelpers.GetFirst(meta, string.Empty, "PublishedDate", "StartDate", "StartYear");
        var dateIso = MetadataHelpers.NormDate(dateRaw);
        Add("--date", dateIso);
        var authorsRaw = MetadataHelpers.GetFirst(meta, string.Empty, "Authors", "Author", "Writer");
        if (!string.IsNullOrWhiteSpace(authorsRaw))
        {
            var authors = authorsRaw.Split([',', ';', '|', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Add("--authors", string.Join(" & ", authors));
        }
        var tagList = MetadataHelpers.GetTags(meta);
        if (tagList.Count > 0) Add("--tags", string.Join(',', tagList));
        var lang = MetadataHelpers.GetFirst(meta, string.Empty, "Language");
        if (!string.IsNullOrWhiteSpace(lang))
            Add("--language", lang.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? lang);
        parts.Add(filePath);
        return parts;

        void Add(string flag, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            parts.Add(flag);
            parts.Add(value);
        }
    }
}
