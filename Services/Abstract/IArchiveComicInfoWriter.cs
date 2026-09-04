using BooksMetadataBaker.Services.Comic;

namespace BooksMetadataBaker.Services.Abstract;

public interface IArchiveComicInfoWriter
{
    /// <summary>Read the existing ComicInfo.xml from the archive root, or null when absent/malformed.</summary>
    Task<ComicInfoModel?> ReadExistingAsync(string path, CancellationToken ct);

    /// <summary>Count image entries, excluding root-level cover-named files (guide §6).</summary>
    Task<int> CountPagesAsync(string path, CancellationToken ct);

    /// <summary>Write/replace ComicInfo.xml at the archive root.</summary>
    Task<(bool Ok, string? Error)> WriteAsync(string path, string xml, CancellationToken ct);

    /// <summary>
    /// Convert a comic archive (cbz/cbt/cb7/cbr) to a .cbz (ZIP) with ComicInfo.xml embedded.
    /// Used as a fallback when in-place embedding is impossible (no RAR write tool for .cbr) or
    /// fails (7-Zip/TAR hiccup, corrupt archive). On success the original is removed (unless it
    /// was already a .cbz) and the new .cbz path is returned.
    /// </summary>
    Task<(bool Ok, string? NewPath, string? Error)> ConvertToCbzAsync(string path, string xml, CancellationToken ct);
}
