namespace BooksMetadataBaker.Services.Abstract;

public interface IUploadProcessingService
{
    Task<(EBookUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct);

    /// <summary>
    /// Processes a file that already exists on the server (e.g. a Docker volume
    /// mount) instead of an uploaded one. <paramref name="moveOriginals"/> moves
    /// the source into the library; otherwise it is copied and the source stays.
    /// </summary>
    Task<(EBookUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessServerFileAsync(UploadRequest info, string sourcePath, bool moveOriginals, CancellationToken ct);
}
