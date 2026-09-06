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

    /// <summary>
    /// List entry names in the archive, '/'-separated relative paths; directory entries
    /// (when the container stores them) end with '/'.
    /// </summary>
    Task<(bool Ok, IReadOnlyList<string> Names, string? Error)> ListEntriesAsync(string path, CancellationToken ct);

    /// <summary>
    /// Split a verified multi-volume archive (see <see cref="MultiVolumeSplitter"/>) into one
    /// .cbz per volume folder, each with its own root ComicInfo.xml (<paramref name="xmls"/>
    /// parallel to <paramref name="plan"/>.Parts). Folder-internal structure is preserved
    /// relative to the volume folder. On success the source archive (and its .meta.json
    /// sidecar) is removed and the new .cbz paths are returned; on failure the source is
    /// left intact and any partial outputs are deleted.
    /// </summary>
    Task<(bool Ok, IReadOnlyList<string> NewPaths, string? Error)> SplitToCbzAsync(
        string path,
        MultiVolumeSplitPlan plan,
        IReadOnlyList<string> xmls,
        string targetDir,
        Func<string, string> fileNameForVolume,
        CancellationToken ct);
}
