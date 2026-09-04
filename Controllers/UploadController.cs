namespace BooksMetadataBaker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadController(IUploadProcessingService processor, IConfiguration config, ILogger<UploadController> logger) : ControllerBase
{
    // 512MB: headroom over the 500MB client limit so a max-size file plus the
    // multipart/form overhead still fits under the per-request body limit.
    private const int MaxFileSize = 536870912;

    private readonly bool mangaComicsEnabled =
        !bool.TryParse(config["MangaComics:Enabled"], out var enabled) || enabled;

    private readonly string[] allowedExtensions = BuildAllowedExtensions(config);

    private static string[] BuildAllowedExtensions(IConfiguration config)
    {
        // Config-driven archive list (plan §4.9); PDF/EPUB are always allowed.
        // Raw containers (zip/rar/7z/tar) are accepted and saved as their Kavita
        // equivalent (cbz/cbr/cb7/cbt) — see UploadProcessingService.DetectFormat.
        var raw = config["MangaComics:AllowedExtensions"] ?? "cbz,cbr,cb7,cbt,zip,rar,7z,tar";
        var archives = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return [".pdf", ".epub", .. archives];
    }

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
            return BadRequest($"eBook or comic file required ({AllowedExtensionsText})");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"eBook or comic file required ({AllowedExtensionsText})");

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
}
