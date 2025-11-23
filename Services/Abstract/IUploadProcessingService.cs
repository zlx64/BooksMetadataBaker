using PrepKavitaPdf.Models;

namespace PrepKavitaPdf.Services.Abstract;

public interface IUploadProcessingService
{
    Task<(PdfUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct);
}
