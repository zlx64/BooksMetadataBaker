using Microsoft.AspNetCore.Mvc;
using PrepKavitaPdf.Models;
using PrepKavitaPdf.Services;
using System.Collections.Concurrent;
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
public class UploadController : ControllerBase
{
    private readonly IConfiguration config;
    private readonly IAggregatedMetadataService metadataService;
    private readonly IPdfMetadataUpdater metadataUpdater;
    private readonly ILogger<UploadController> logger;

    public UploadController(
        IConfiguration config,
        IAggregatedMetadataService metadataService,
        IPdfMetadataUpdater metadataUpdater,
        ILogger<UploadController> logger)
    {
        this.config = config;
        this.metadataService = metadataService;
        this.metadataUpdater = metadataUpdater;
        this.logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(524_288_000)] // 500MB
    public async Task<IActionResult> Upload([FromForm] UploadRequest info, List<IFormFile> files, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.Title)) return BadRequest("Title required");
        if (files is null || files.Count == 0) return BadRequest("At least one file required");

        var root = config["PdfLibrary:RootFolder"];
        if (string.IsNullOrWhiteSpace(root)) return StatusCode(500, "Root folder not configured");
        Directory.CreateDirectory(root);
        var titleFolder = Path.Combine(root, Sanitize(info.Title));
        Directory.CreateDirectory(titleFolder);

        // Save PDFs
        var savedFiles = new List<string>();
        foreach (var file in files)
        {
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            var savePath = GetUniquePdfPath(titleFolder, info.Title, file.FileName);
            await using var fs = System.IO.File.Create(savePath);
            await file.CopyToAsync(fs, ct);
            savedFiles.Add(savePath);
        }
        if (savedFiles.Count == 0) return BadRequest("No PDF files uploaded");

        var meta = await metadataService.FetchMetadataAsync(info.Title, info.Type, ct);
        if (ct.IsCancellationRequested) return Ok(new { Files = Array.Empty<PdfUploadProcessResult>(), Metadata = meta, Cancelled = true });

        // Concurrency setting
        int concurrency = int.TryParse(config["PdfLibrary:ProcessingConcurrency"], out var c) ? Math.Clamp(c, 1, 32) : 4;

        var results = new ConcurrentBag<PdfUploadProcessResult>();

        await RunConcurrent(savedFiles, concurrency, async path =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var attempts = await metadataUpdater.RunPipelineAsync(path, meta, info.Title, ct);
                var success = attempts.Any(a => a.Success);
                var ghostscriptRan = attempts.Any(a => a.GhostscriptRan);
                var directOk = attempts.Any(a => a.Stage == PdfMetadataAttemptStage.Direct && a.Success);
                var repairOk = attempts.Any(a => a.Stage == PdfMetadataAttemptStage.Repair && a.Success);
                var forceOk = attempts.Any(a => a.Stage == PdfMetadataAttemptStage.ForceStrip && a.Success);
                var errorMessage = CombineErrors(attempts);
                metadataUpdater.WriteSidecarSummary(path, meta, info.Title, success, errorMessage, success, ghostscriptRan);
                results.Add(new PdfUploadProcessResult(
                    File: Path.GetFileName(path),
                    Success: success,
                    ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage,
                    Attempts: attempts.Count,
                    AppliedMetadata: meta,
                    DirectAttemptSuccess: directOk,
                    RepairAttemptSuccess: repairOk,
                    ForceStripAttemptSuccess: forceOk,
                    GhostscriptRan: ghostscriptRan));
                if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                    logger.LogWarning("Metadata update failed for {File}: {Errors}", path, errorMessage);
            }
            catch (OperationCanceledException)
            {
                results.Add(new PdfUploadProcessResult(
                    File: Path.GetFileName(path),
                    Success: false,
                    ErrorMessage: "Cancelled",
                    Attempts: 0,
                    AppliedMetadata: meta,
                    DirectAttemptSuccess: false,
                    RepairAttemptSuccess: false,
                    ForceStripAttemptSuccess: false,
                    GhostscriptRan: false));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed updating metadata for {File}", path);
                results.Add(new PdfUploadProcessResult(
                    File: Path.GetFileName(path),
                    Success: false,
                    ErrorMessage: ex.Message,
                    Attempts: 0,
                    AppliedMetadata: meta,
                    DirectAttemptSuccess: false,
                    RepairAttemptSuccess: false,
                    ForceStripAttemptSuccess: false,
                    GhostscriptRan: false));
            }
        });

        // Preserve original order
        var ordered = savedFiles.Select(f => results.First(r => r.File == Path.GetFileName(f))).ToList();
        return Ok(new { Files = ordered, Metadata = meta, Cancelled = ct.IsCancellationRequested });
    }

    private static async Task RunConcurrent(IEnumerable<string> items, int maxConcurrency, Func<string, Task> action)
    {
        using var sem = new SemaphoreSlim(maxConcurrency);
        var tasks = items.Select(async item =>
        {
            await sem.WaitAsync();
            try { await action(item); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private static string GetUniquePdfPath(string folder, string title, string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var match = Regex.Match(baseName, @"\d+(?:\.\d+)?");
        var newBaseName = match.Success ? BuildVolumeName(title, match.Value) : title;
        var sanitized = Sanitize(newBaseName) + ".pdf";
        var path = Path.Combine(folder, sanitized);
        int counter = 1;
        while (System.IO.File.Exists(path))
        {
            path = Path.Combine(folder, Sanitize(newBaseName) + $" ({counter}).pdf");
            counter++;
        }
        return path;
    }

    private static string BuildVolumeName(string title, string numStr)
    {
        if (numStr.Contains('.') && decimal.TryParse(numStr, out var dec)) numStr = dec.ToString();
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
