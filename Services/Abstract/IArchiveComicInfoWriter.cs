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
}
