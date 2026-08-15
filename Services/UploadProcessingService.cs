using System.Globalization;
using System.Collections.Concurrent;
using BooksMetadataBaker.Services.Helpers;
using BooksMetadataBaker.Services.Types;

namespace BooksMetadataBaker.Services;

public class UploadProcessingService(
    IConfiguration config,
    IAggregatedMetadataService metadataService,
    IEBookMetadataUpdater metadataUpdater,
    IKavitaMetadataWriter kavitaWriter,
    ILogger<UploadProcessingService> logger) : IUploadProcessingService
{
    public async Task<(EBookUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct)
    {
        var root = config["PdfLibrary:RootFolder"];
        var typeFolderSection = config.GetSection("PdfLibrary:TypeFolders");
        var typeFolderRaw = info.Type switch
        {
            BookType.Book => typeFolderSection["Book"] ?? "Novel",
            BookType.LightNovel => typeFolderSection["LightNovel"] ?? "Ranobe",
            BookType.Manga => typeFolderSection["Manga"] ?? "Manga",
            BookType.Comic => typeFolderSection["Comic"] ?? "Comic",
            _ => "Other"
        };

        var isAbsolute = Path.IsPathRooted(typeFolderRaw);
        if (string.IsNullOrWhiteSpace(root) && !isAbsolute)
            return (CreateErrorResult("", "ROOT_DIR not configured and type folder is not absolute", EBookFormat.Pdf, new Dictionary<string,string>()), new Dictionary<string,string>(), false, "ROOT_DIR not configured and type folder is not absolute");

        var baseFolder = isAbsolute
            ? typeFolderRaw
            : Path.Combine(root!, Sanitize(typeFolderRaw));
        var format = DetectFormat(file.FileName);

        var titleFolder = ResolveTitleFolder(baseFolder, info.Title, out var pathError);
        if (titleFolder is null)
        {
            logger.LogError("Rejected title that escapes library folder: {Title}", info.Title);
            return (CreateErrorResult("", pathError ?? "Invalid title", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, pathError ?? "Invalid title");
        }

        var savePath = GetUniqueEBookPath(titleFolder, info.Title, file.FileName);
        var fileLock = GetFileLock(Path.GetFullPath(savePath));
        await fileLock.WaitAsync(ct);
        try
        {
            try
            {
                Directory.CreateDirectory(titleFolder);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create directory {Dir}", titleFolder);
                return (CreateErrorResult("", $"Create directory failed: {ex.GetType().Name}: {ex.Message}", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, ex.Message);
            }

            if (!IsWritableDirectory(titleFolder))
            {
                logger.LogError("Directory not writable: {Dir}", titleFolder);
                return (CreateErrorResult("", $"Directory not writable: {titleFolder}", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, $"Directory not writable: {titleFolder}");
            }

            try
            {
                if (File.Exists(savePath))
                {
                    logger.LogInformation("Overwriting existing file {Path}", savePath);
                }
                await using var fs = File.Create(savePath);
                await file.CopyToAsync(fs, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed writing uploaded file to {Path}", savePath);
                return (CreateErrorResult(Path.GetFileName(savePath), $"Save file failed: {ex.GetType().Name}: {ex.Message}", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, ex.Message);
            }

            var volumeToken = MetadataHelpers.ExtractVolumeToken(file.FileName);
            var meta = await metadataService.FetchMetadataAsync(info.Title, info.Type, volumeToken, ct);
            if (ct.IsCancellationRequested)
                return (CreateCancelledResult(Path.GetFileName(savePath), format, meta), meta, true, null);

            EBookUploadProcessResult result;
            try
            {
                var attempts = await metadataUpdater.RunPipelineAsync(savePath, meta, info.Title, ct);
                var success = attempts.Any(a => a.Success);
                var directOk = attempts.Any(a => a is { Stage: EBookMetadataAttemptStage.Direct, Success: true });
                var repairOk = attempts.Any(a => a is { Stage: EBookMetadataAttemptStage.Repair, Success: true });
                var ghostscriptRan = attempts.Any(a => a.GhostscriptRan);
                var errorMessage = CombineErrors(attempts);
                metadataUpdater.WriteSidecarSummary(savePath, meta, info.Title, success, errorMessage, success, ghostscriptRan);
                if (success) await kavitaWriter.WriteAsync(savePath, meta, info.Title);
                result = new(
                    File: Path.GetFileName(savePath),
                    Success: success,
                    ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage,
                    Attempts: attempts.Count,
                    AppliedMetadata: meta,
                    DirectAttemptSuccess: directOk,
                    RepairAttemptSuccess: repairOk,
                    GhostscriptRan: ghostscriptRan,
                    Format: format);
                if (!success && !string.IsNullOrWhiteSpace(errorMessage)) 
                    logger.LogWarning("Metadata update failed for {File}: {Errors}", savePath, errorMessage);
            }
            catch (OperationCanceledException)
            {
                result = CreateCancelledResult(Path.GetFileName(savePath), format, meta);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed updating metadata for {File}", savePath);
                result = CreateErrorResult(Path.GetFileName(savePath), ex.Message, format, meta);
            }

            return (result, meta, ct.IsCancellationRequested, null);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static EBookFormat DetectFormat(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".epub" => EBookFormat.Epub,
            ".pdf" => EBookFormat.Pdf,
            _ => EBookFormat.Pdf
        };
    }

    private static EBookUploadProcessResult CreateErrorResult(string fileName, string errorMessage, EBookFormat format, IDictionary<string, string> meta) =>
        new(fileName, false, errorMessage, 0, meta, false, false, false, format);

    private static EBookUploadProcessResult CreateCancelledResult(string fileName, EBookFormat format, IDictionary<string, string> meta) =>
        new(fileName, false, "Cancelled", 0, meta, false, false, false, format);

    private static bool IsWritableDirectory(string path)
    {
        try
        {
            var testFile = Path.Combine(path, ".perm_test_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniqueEBookPath(string folder, string title, string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var match = Regex.Match(baseName, @"\d+(?:\.\d+)?");
        var newBaseName = match.Success ? BuildVolumeName(title, match.Value) : title;
        var sanitized = Sanitize(newBaseName) + extension;
        var path = Path.Combine(folder, sanitized);
        return path;
    }

    private static string BuildVolumeName(string title, string numStr)
    {
        if (numStr.Contains('.') && decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
            numStr = dec.ToString(CultureInfo.InvariantCulture);
        else if (int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            numStr = num.ToString(CultureInfo.InvariantCulture);
        return $"{title} - Volume {numStr}";
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim GetFileLock(string fullPath) =>
        FileLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Resolves a user-supplied title into a folder inside baseFolder, rejecting
    /// any result that would escape the library folder (path traversal).
    /// </summary>
    public static string? ResolveTitleFolder(string baseFolder, string title, out string? error)
    {
        error = null;
        var baseFull = Path.GetFullPath(baseFolder);
        var titleFolder = Path.GetFullPath(Path.Combine(baseFull, Sanitize(title)));
        var prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!titleFolder.StartsWith(prefix, comparison))
        {
            error = "Invalid title: resolves outside the library folder";
            return null;
        }
        return titleFolder;
    }

    public static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) 
            name = name.Replace(c, '_');
        return name;
    }

    private static string CombineErrors(IEnumerable<EBookMetadataAttemptResult> attempts)
    {
        var parts = attempts.Where(a => !a.Success && !string.IsNullOrWhiteSpace(a.ErrorMessage))
                             .Select(a => $"{a.Stage}: {a.ErrorMessage}").ToList();
        return parts.Count == 0 ? string.Empty : string.Join("; ", parts);
    }
}
