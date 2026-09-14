namespace BooksMetadataBaker.Services.Helpers;

/// <summary>
/// Upload policy shared by the upload and server-files endpoints: which
/// extensions are accepted and whether the manga/comics pipeline is on.
/// </summary>
public static class UploadExtensions
{
    /// <summary>
    /// Config-driven archive list; PDF/EPUB are always allowed. Raw containers
    /// (zip/rar/7z/tar) are accepted and saved as their Kavita equivalent.
    /// </summary>
    public static string[] BuildAllowedExtensions(IConfiguration config)
    {
        var raw = config["MangaComics:AllowedExtensions"] ?? "cbz,cbr,cb7,cbt,zip,rar,7z,tar";
        var archives = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return [".pdf", ".epub", .. archives];
    }

    public static bool IsMangaComicsEnabled(IConfiguration config) =>
        !bool.TryParse(config["MangaComics:Enabled"], out var enabled) || enabled;
}
