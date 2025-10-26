using Microsoft.AspNetCore.Mvc;
using PrepKavitaPdf.Models;
using PrepKavitaPdf.Services;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IAggregatedMetadataService _metadata;
    private readonly PdfMetadataUpdater _updater;
    private readonly ILogger<UploadController> _logger;

    public UploadController(IConfiguration config, IAggregatedMetadataService metadata, PdfMetadataUpdater updater, ILogger<UploadController> logger)
    {
        _config = config; _metadata = metadata; _updater = updater; _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(524288000)] // 500MB
    public async Task<IActionResult> Upload([FromForm] UploadRequest info, List<IFormFile> files, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(info.Title)) return BadRequest("Title required");
        if (files == null || files.Count == 0) return BadRequest("At least one file required");
        var root = _config["PdfLibrary:RootFolder"];
        if (string.IsNullOrWhiteSpace(root)) return StatusCode(500, "Root folder not configured");
        Directory.CreateDirectory(root);
        var titleFolder = Path.Combine(root, SanitizeFolder(info.Title));
        Directory.CreateDirectory(titleFolder);

        var savedFiles = new List<string>();
        foreach (var file in files)
        {
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;

            var baseName = Path.GetFileNameWithoutExtension(file.FileName);
            var match = Regex.Match(baseName, @"\d+(?:\.\d+)?");
            string newBaseName = match.Success ? BuildVolumeName(info.Title, match.Value) : info.Title;
            var newFileName = SanitizeFolder(newBaseName) + ".pdf";
            var savePath = Path.Combine(titleFolder, newFileName);
            int counter = 1;
            while (System.IO.File.Exists(savePath))
            {
                savePath = Path.Combine(titleFolder, SanitizeFolder(newBaseName) + $" ({counter}).pdf");
                counter++;
            }
            using var fs = System.IO.File.Create(savePath);
            await file.CopyToAsync(fs, ct);
            savedFiles.Add(savePath);
        }
        if (savedFiles.Count == 0) return BadRequest("No PDF files uploaded");

        var meta = await _metadata.FetchMetadataAsync(info.Title, info.Type, ct);
        int batchSize = int.TryParse(_config["PdfLibrary:ProcessingBatchSize"], out var bs) ? Math.Max(1, bs) : 5;
        var updateResults = new List<object>();

        for (int i = 0; i < savedFiles.Count && !ct.IsCancellationRequested; i += batchSize)
        {
            var batch = savedFiles.Skip(i).Take(batchSize).ToList();
            var tasks = batch.Select(async path =>
            {
                try
                {
                    var result = await _updater.UpdateAsync(path, meta, info.Title, ct);
                    if (!result.Success)
                        _logger.LogWarning("Metadata update failed for {file}: {error}", path, result.ErrorMessage);
                    return (object)new { File = Path.GetFileName(path), result.Success, result.ErrorMessage, result.Attempts, AppliedMetadata = result.AppliedMetadata };
                }
                catch (OperationCanceledException)
                {
                    return (object)new { File = Path.GetFileName(path), Success = false, ErrorMessage = "Cancelled", Attempts = 0, AppliedMetadata = meta };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed updating metadata for {file}", path);
                    return (object)new { File = Path.GetFileName(path), Success = false, ErrorMessage = ex.Message, Attempts = 0, AppliedMetadata = meta };
                }
            }).ToList();
            var batchResults = await Task.WhenAll(tasks);
            updateResults.AddRange(batchResults);
        }

        return Ok(new { Files = updateResults, Metadata = meta, Cancelled = ct.IsCancellationRequested });
    }

    private static string BuildVolumeName(string title, string numStr)
    {
        if (numStr.Contains('.') && decimal.TryParse(numStr, out var dec)) numStr = dec.ToString();
        else if (int.TryParse(numStr, out var num)) numStr = num.ToString();
        return $"{title} - Volume {numStr}";
    }

    private static string SanitizeFolder(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
