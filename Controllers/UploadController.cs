using Microsoft.AspNetCore.Mvc;
using PrepKavitaPdf.Models;
using PrepKavitaPdf.Services.Abstract;

namespace PrepKavitaPdf.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadController(IUploadProcessingService processor) : ControllerBase
{
    private const int MaxFileSize = 524288000; // 500MB
    private static readonly string[] AllowedExtensions = { ".pdf", ".epub" };

    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Upload([FromForm] UploadRequest info, IFormFile? file, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.Title)) 
            return BadRequest("Title required");

        if (file is null) 
            return BadRequest("eBook file required (PDF or EPUB)");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return BadRequest("File must be a PDF or EPUB");

        var (result, metadata, cancelled, error) = await processor.ProcessSingleAsync(info, file, ct);
        if (error != null && string.IsNullOrWhiteSpace(result.File))
            return StatusCode(500, error);

        return Ok(new { Files = new[] { result }, Metadata = metadata, Cancelled = cancelled });
    }
}
