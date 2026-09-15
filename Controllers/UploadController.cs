using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Helpers;

namespace BooksMetadataBaker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadController(IUploadProcessingService processor, IConfiguration config, ILogger<UploadController> logger) : ControllerBase
{
    // 512MB: headroom over the 500MB client limit so a max-size file plus the
    // multipart/form overhead still fits under the per-request body limit.
    private const int MaxFileSize = 536870912;

    private readonly bool mangaComicsEnabled = UploadExtensions.IsMangaComicsEnabled(config);

    private readonly string[] allowedExtensions = UploadExtensions.BuildAllowedExtensions(config);

    private string AllowedExtensionsText =>
        string.Join(", ", allowedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));

    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Upload([FromForm] UploadRequest info, IFormFile? file, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.Title)) 
            return BadRequest("Title required");

        if (file is null) 
            return BadRequest($"Ebook or comic file required ({AllowedExtensionsText})");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Ebook or comic file required ({AllowedExtensionsText})");

        // Everything that is not PDF/EPUB is a comic archive; reject it when the
        // feature is disabled (plan §4.8).
        if (!mangaComicsEnabled && extension is not (".pdf" or ".epub"))
            return BadRequest("Manga/comics uploads are disabled");

        var (result, metadata, cancelled, error) = await processor.ProcessSingleAsync(info, file, ct);
        if (error != null && string.IsNullOrWhiteSpace(result.File))
        {
            logger.LogWarning("Upload rejected: {Error}", error);
            return StatusCode(500, "Internal server error");
        }

        return Ok(new { Files = new[] { result }, Metadata = metadata, Cancelled = cancelled });
    }

    [HttpPost("image-folder")]
    [RequestSizeLimit(MaxFileSize)]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> UploadImageFolder(
        [FromForm] UploadRequest info,
        [FromForm] string? folderName,
        [FromForm] string? paths,
        IFormFileCollection? files,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.Title))
            return BadRequest("Title required");

        if (!mangaComicsEnabled)
            return BadRequest("Manga/comics uploads are disabled");

        if (files is null || files.Count == 0)
            return BadRequest("Image folder required");

        if (!files.Any(file => ArchiveComicInfoWriter.IsImageFile(file.FileName)))
            return BadRequest("No image files found in folder");

        if (files.Sum(file => file.Length) > MaxFileSize)
            return BadRequest("Folder is too large (max 500 MB)");

        var (result, metadata, cancelled, error) = await processor.ProcessImageFolderUploadAsync(info, files, folderName, paths, ct);
        if (error != null && string.IsNullOrWhiteSpace(result.File))
        {
            logger.LogWarning("Image folder upload rejected: {Error}", error);
            return StatusCode(500, "Internal server error");
        }

        return Ok(new { Files = new[] { result }, Metadata = metadata, Cancelled = cancelled });
    }
}
