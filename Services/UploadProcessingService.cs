using Microsoft.AspNetCore.Http;
using PrepKavitaPdf.Models;
using System.Text.RegularExpressions;
using System.Globalization;

namespace PrepKavitaPdf.Services;

public class UploadProcessingService : IUploadProcessingService
{
    private readonly IConfiguration config;
    private readonly IAggregatedMetadataService metadataService;
    private readonly IPdfMetadataUpdater metadataUpdater;
    private readonly ILogger<UploadProcessingService> logger;

    public UploadProcessingService(
        IConfiguration config,
        IAggregatedMetadataService metadataService,
        IPdfMetadataUpdater metadataUpdater,
        ILogger<UploadProcessingService> logger)
    {
        this.config = config;
        this.metadataService = metadataService;
        this.metadataUpdater = metadataUpdater;
        this.logger = logger;
    }

    public async Task<(PdfUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct)
    {
        var root = config["PdfLibrary:RootFolder"];
        if (string.IsNullOrWhiteSpace(root)) return (new PdfUploadProcessResult("", false, "Root folder not configured", 0, new Dictionary<string,string>(), false, false, false, false), new Dictionary<string,string>(), false, "Root folder not configured");

        var typeFolderSection = config.GetSection("PdfLibrary:TypeFolders");
        var typeFolder = info.Type switch
        {
            BookType.Book => typeFolderSection["Book"] ?? "Novel",
            BookType.LightNovel => typeFolderSection["LightNovel"] ?? "Ranobe",
            BookType.Manga => typeFolderSection["Manga"] ?? "Manga",
            BookType.Comic => typeFolderSection["Comic"] ?? "Comic",
            _ => "Other"
        };

        var titleFolder = Path.Combine(root, Sanitize(typeFolder), Sanitize(info.Title));

        try
        {
            Directory.CreateDirectory(titleFolder);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create directory {Dir}", titleFolder);
            return (new PdfUploadProcessResult("", false, $"Create directory failed: {ex.GetType().Name}: {ex.Message}", 0, new Dictionary<string,string>(), false, false, false, false), new Dictionary<string,string>(), false, ex.Message);
        }

        if (!IsWritableDirectory(titleFolder))
        {
            logger.LogError("Directory not writable: {Dir}", titleFolder);
            return (new PdfUploadProcessResult("", false, $"Directory not writable: {titleFolder}", 0, new Dictionary<string,string>(), false, false, false, false), new Dictionary<string,string>(), false, $"Directory not writable: {titleFolder}");
        }

        var savePath = GetUniquePdfPath(titleFolder, info.Title, file.FileName);
        try
        {
            await using var fs = System.IO.File.Create(savePath);
            await file.CopyToAsync(fs, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed writing uploaded file to {Path}", savePath);
            return (new PdfUploadProcessResult(Path.GetFileName(savePath), false, $"Save file failed: {ex.GetType().Name}: {ex.Message}", 0, new Dictionary<string,string>(), false, false, false, false), new Dictionary<string,string>(), false, ex.Message);
        }

        var meta = await metadataService.FetchMetadataAsync(info.Title, info.Type, ct);
        if (ct.IsCancellationRequested)
            return (new PdfUploadProcessResult(Path.GetFileName(savePath), false, "Cancelled", 0, meta, false, false, false, false), meta, true, null);

        PdfUploadProcessResult result;
        try
        {
            var attempts = await metadataUpdater.RunPipelineAsync(savePath, meta, info.Title, ct);
            var success = attempts.Any(a => a.Success);
            var ghostscriptRan = attempts.Any(a => a.GhostscriptRan);
            var directOk = attempts.Any(a => a.Stage == PdfMetadataAttemptStage.Direct && a.Success);
            var repairOk = attempts.Any(a => a.Stage == PdfMetadataAttemptStage.Repair && a.Success);
            var forceOk = attempts.Any(a => a.Stage == PdfMetadataAttemptStage.ForceStrip && a.Success);
            var errorMessage = CombineErrors(attempts);
            metadataUpdater.WriteSidecarSummary(savePath, meta, info.Title, success, errorMessage, success, ghostscriptRan);
            result = new(
                File: Path.GetFileName(savePath),
                Success: success,
                ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage,
                Attempts: attempts.Count,
                AppliedMetadata: meta,
                DirectAttemptSuccess: directOk,
                RepairAttemptSuccess: repairOk,
                ForceStripAttemptSuccess: forceOk,
                GhostscriptRan: ghostscriptRan);
            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                logger.LogWarning("Metadata update failed for {File}: {Errors}", savePath, errorMessage);
        }
        catch (OperationCanceledException)
        {
            result = new(
                File: Path.GetFileName(savePath),
                Success: false,
                ErrorMessage: "Cancelled",
                Attempts: 0,
                AppliedMetadata: meta,
                DirectAttemptSuccess: false,
                RepairAttemptSuccess: false,
                ForceStripAttemptSuccess: false,
                GhostscriptRan: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed updating metadata for {File}", savePath);
            result = new(
                File: Path.GetFileName(savePath),
                Success: false,
                ErrorMessage: ex.Message,
                Attempts: 0,
                AppliedMetadata: meta,
                DirectAttemptSuccess: false,
                RepairAttemptSuccess: false,
                ForceStripAttemptSuccess: false,
                GhostscriptRan: false);
        }

        return (result, meta, ct.IsCancellationRequested, null);
    }

    private static bool IsWritableDirectory(string path)
    {
        try
        {
            var testFile = Path.Combine(path, ".perm_test_" + Guid.NewGuid().ToString("N"));
            System.IO.File.WriteAllText(testFile, "test");
            System.IO.File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniquePdfPath(string folder, string title, string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var match = Regex.Match(baseName, @"\d+(?:\.\d+)?");
        var newBaseName = match.Success ? BuildVolumeName(title, match.Value) : title;
        var sanitized = Sanitize(newBaseName) + ".pdf";
        var path = Path.Combine(folder, sanitized);
        var counter = 1;
        while (System.IO.File.Exists(path))
        {
            path = Path.Combine(folder, Sanitize(newBaseName) + $" ({counter}).pdf");
            counter++;
        }
        return path;
    }

    private static string BuildVolumeName(string title, string numStr)
    {
        if (numStr.Contains('.') && decimal.TryParse(numStr, out var dec)) numStr = dec.ToString(CultureInfo.InvariantCulture);
        else if (int.TryParse(numStr, out var num)) numStr = num.ToString();
        return $"{title} - Volume {numStr}";
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static string CombineErrors(IEnumerable<PdfMetadataAttemptResult> attempts)
    {
        var parts = attempts.Where(a => !a.Success && !string.IsNullOrWhiteSpace(a.ErrorMessage))
                             .Select(a => $"{a.Stage}: {a.ErrorMessage}").ToList();
        return parts.Count == 0 ? string.Empty : string.Join("; ", parts);
    }
}
