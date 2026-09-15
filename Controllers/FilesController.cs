using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Helpers;

namespace BooksMetadataBaker.Controllers;

/// <summary>
/// Browses and processes files that already live on the server (e.g. a Docker
/// volume mount), so the user is not limited to uploading from their own PC.
/// The browsable root is ServerFiles:RootFolder (falls back to the library
/// root); every path is contained to that root (no traversal).
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController(IUploadProcessingService processor, IConfiguration config, ILogger<FilesController> logger) : ControllerBase
{
    // Same 500 MB per-file limit as the upload endpoint (512 MB headroom there
    // covers multipart overhead; here the file never crosses the wire).
    private const long MaxFileSize = 536870912;

    private readonly bool mangaComicsEnabled = UploadExtensions.IsMangaComicsEnabled(config);
    private readonly string[] allowedExtensions = UploadExtensions.BuildAllowedExtensions(config);
    private string[] SelectableExtensions => mangaComicsEnabled ? allowedExtensions : [".pdf", ".epub"];

    private string AllowedExtensionsText =>
        string.Join(", ", allowedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));

    [HttpGet]
    public IActionResult Browse([FromQuery] string? path)
    {
        if (!TryGetRoot(out var rootFull, out var reason))
            return NotFound(reason);

        var dirFull = ResolveUnderRoot(rootFull, path, out var error);
        if (dirFull is null)
            return BadRequest(error);
        if (!Directory.Exists(dirFull))
            return NotFound("Folder not found");

        var entries = ListDirectory(rootFull, dirFull, SelectableExtensions, mangaComicsEnabled);
        var parent = string.Equals(dirFull, rootFull, StringComparison.Ordinal)
            ? null
            : ToRelative(rootFull, Directory.GetParent(dirFull)!.FullName);

        return Ok(new { path = ToRelative(rootFull, dirFull), parent, entries });
    }

    [HttpPost("process")]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Process([FromBody] ServerFileRequest request, CancellationToken ct)
    {
        if (!TryGetRoot(out var rootFull, out var reason))
            return NotFound(reason);

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title required");

        var targetFull = ResolveUnderRoot(rootFull, request.Path, out var error);
        if (targetFull is null)
            return BadRequest(error);

        if (Directory.Exists(targetFull))
        {
            if (!mangaComicsEnabled)
                return BadRequest("Manga/comics uploads are disabled");
            var images = ImageFolderArchiver.GetImageFiles(targetFull);
            if (images.Count == 0)
                return BadRequest("No image files found in folder");
            var totalSize = images.Sum(image =>
            {
                try
                {
                    return new FileInfo(Path.Combine(targetFull, image.Replace('/', Path.DirectorySeparatorChar))).Length;
                }
                catch
                {
                    return 0;
                }
            });
            if (totalSize > MaxFileSize)
                return BadRequest("Folder is too large (max 500 MB)");

            var (folderResult, folderMetadata, folderCancelled, folderError) = await processor.ProcessServerImageFolderAsync(
                new UploadRequest { Title = request.Title, Type = request.Type }, targetFull, request.MoveOriginals, ct);
            if (folderError != null && string.IsNullOrWhiteSpace(folderResult.File))
            {
                logger.LogWarning("Server image folder rejected: {Error}", folderError);
                return StatusCode(500, "Internal server error");
            }

            return Ok(new { Files = new[] { folderResult }, Metadata = folderMetadata, Cancelled = folderCancelled });
        }

        if (!System.IO.File.Exists(targetFull))
            return NotFound("File not found");

        var extension = Path.GetExtension(targetFull).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Ebook or comic file required ({AllowedExtensionsText})");
        if (!mangaComicsEnabled && extension is not (".pdf" or ".epub"))
            return BadRequest("Manga/comics uploads are disabled");
        if (new FileInfo(targetFull).Length > MaxFileSize)
            return BadRequest("File is too large (max 500 MB)");

        var (result, metadata, cancelled, procError) = await processor.ProcessServerFileAsync(
            new UploadRequest { Title = request.Title, Type = request.Type }, targetFull, request.MoveOriginals, ct);
        if (procError != null && string.IsNullOrWhiteSpace(result.File))
        {
            logger.LogWarning("Server file rejected: {Error}", procError);
            return StatusCode(500, "Internal server error");
        }

        return Ok(new { Files = new[] { result }, Metadata = metadata, Cancelled = cancelled });
    }

    private bool TryGetRoot(out string rootFull, out string reason)
    {
        rootFull = null!;
        reason = null!;
        if (!bool.TryParse(config["ServerFiles:Enabled"], out var enabled) || !enabled)
        {
            reason = "Server file browsing is disabled";
            return false;
        }
        // An empty (not just missing) ServerFiles:RootFolder falls back to the
        // library root — appsettings.json ships with "" by default.
        var raw = config["ServerFiles:RootFolder"];
        if (string.IsNullOrWhiteSpace(raw))
            raw = config["PdfLibrary:RootFolder"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            reason = "Server files folder is not configured";
            return false;
        }
        rootFull = Path.GetFullPath(raw);
        if (!Directory.Exists(rootFull))
        {
            reason = "Server files folder does not exist";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves a user-supplied relative path under rootFolder, rejecting any
    /// result that would escape the folder (path traversal). Returns the full
    /// path, or null with an error message.
    /// </summary>
    public static string? ResolveUnderRoot(string rootFolder, string? relativePath, out string? error)
    {
        error = null;
        if (relativePath is not null && relativePath.Contains('\0'))
        {
            error = "Invalid path";
            return null;
        }
        // Root-anchored / absolute paths are rejected outright and consistently
        // on both platforms. On Unix a leading '/' would otherwise be trimmed
        // below and silently re-rooted inside the folder (e.g. "/etc/passwd" ->
        // "<root>/etc/passwd"); on Windows a drive letter or '\\' already defeats
        // containment. Path.IsPathRooted alone is not enough — it does not treat a
        // leading '/' as rooted on Windows — so also reject an explicit separator.
        var probe = (relativePath ?? string.Empty).Trim();
        if (probe.Length > 0 && (Path.IsPathRooted(probe) || probe[0] == '/' || probe[0] == '\\'))
        {
            error = "Path is outside the server files folder";
            return null;
        }
        var rootFull = Path.GetFullPath(rootFolder);
        var rel = (relativePath ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(rootFull, rel));
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(full, rootFull, comparison) && !full.StartsWith(prefix, comparison))
        {
            error = "Path is outside the server files folder";
            return null;
        }
        return full;
    }

    /// <summary>
    /// Lists a directory: subdirectories first, then files, in case-insensitive
    /// name order. Dotfiles are hidden; each file gets a selectable flag based
    /// on the allowed upload extensions. Directories containing direct images are
    /// selectable as image folders when <paramref name="imageFoldersEnabled"/>.
    /// </summary>
    public static List<ServerFileEntry> ListDirectory(string rootFull, string dirFull, string[] selectableExtensions, bool imageFoldersEnabled = true)
    {
        var entries = new List<ServerFileEntry>();
        foreach (var dir in Directory.EnumerateDirectories(dirFull))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            var hasImages = imageFoldersEnabled && ImageFolderArchiver.HasDirectImageFiles(dir);
            entries.Add(new ServerFileEntry(name, ToRelative(rootFull, dir), true, 0, hasImages, hasImages));
        }
        foreach (var file in Directory.EnumerateFiles(dirFull))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue;
            long size;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch
            {
                continue; // vanished between enumeration and stat
            }
            var ext = Path.GetExtension(file).ToLowerInvariant();
            entries.Add(new ServerFileEntry(
                name, ToRelative(rootFull, file), false, size,
                selectableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)));
        }
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return entries
            .OrderByDescending(e => e.IsDir)
            .ThenBy(e => e.Name, comparison)
            .ToList();
    }

    private static string ToRelative(string rootFull, string full) =>
        string.Equals(rootFull, full, StringComparison.Ordinal)
            ? ""
            : Path.GetRelativePath(rootFull, full).Replace('\\', '/');
}
