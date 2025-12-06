using BooksMetadataBaker.Models;

namespace BooksMetadataBaker.Services.Abstract;

public interface IUploadProcessingService
{
    Task<(EBookUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct);
}
