using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PrepKavitaPdf.Models;
using PrepKavitaPdf.Services;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Controllers;

public sealed record PdfUploadProcessResult(
    string File,
    bool Success,
    string? ErrorMessage,
    int Attempts,
    IDictionary<string, string> AppliedMetadata,
    bool DirectAttemptSuccess,
    bool RepairAttemptSuccess,
    bool ForceStripAttemptSuccess,
    bool GhostscriptRan);

[ApiController]
[Route("api/[controller]")]
public class UploadController(
    IConfiguration config,
    IAggregatedMetadataService metadataService,
    IPdfMetadataUpdater metadataUpdater,
    ILogger<UploadController> logger)
    : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(524_288_000)] // 500MB
    public async Task<IActionResult> Upload([FromForm] UploadRequest info, IFormFile? file, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.Title)) return BadRequest("Title required");
        if (file is null) return BadRequest("PDF file required");
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return BadRequest("File must be a PDF");

        var root = config["PdfLibrary:RootFolder"];
        if (string.IsNullOrWhiteSpace(root)) return StatusCode(500, "Root folder not configured");

        // Resolve type-specific folder from configuration (PdfLibrary:TypeFolders), with defaults.
        var typeFolderSection = config.GetSection("PdfLibrary:TypeFolders");
        var typeFolder = info.Type switch
        {
            BookType.Book => typeFolderSection["Book"] ?? "Novel",
            BookType.LightNovel => typeFolderSection["LightNovel"] ?? "Ranobe",
            BookType.Manga => typeFolderSection["Manga"] ?? "Manga",
            BookType.Comic => typeFolderSection["Comic"] ?? "Comic",
            _ => "Other"
        };

        // Final folder path: {root}/{folder_for_type}/{title}
        var titleFolder = Path.Combine(root, Sanitize(typeFolder), Sanitize(info.Title));
        Directory.CreateDirectory(titleFolder);

        // Save single PDF
        var savePath = GetUniquePdfPath(titleFolder, info.Title, file.FileName);
        await using (var fs = System.IO.File.Create(savePath))
        {
            await file.CopyToAsync(fs, ct);
        }

        var meta = await metadataService.FetchMetadataAsync(info.Title, info.Type, ct);
        if (ct.IsCancellationRequested)
            return Ok(new { Files = Array.Empty<PdfUploadProcessResult>(), Metadata = meta, Cancelled = true });

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

        return Ok(new { Files = new[] { result }, Metadata = meta, Cancelled = ct.IsCancellationRequested });
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
