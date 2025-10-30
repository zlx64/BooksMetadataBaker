using Microsoft.AspNetCore.Http;
using PrepKavitaPdf.Models;
using System.Threading;
using System.Threading.Tasks;

namespace PrepKavitaPdf.Services;

public interface IUploadProcessingService
{
    Task<(PdfUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct);
}
