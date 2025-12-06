using System.Globalization;
using System.Text.RegularExpressions;
using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services.Abstract;
using BooksMetadataBaker.Services.Types;

namespace BooksMetadataBaker.Services;

public class UploadProcessingService(
    IConfiguration config,
    IAggregatedMetadataService metadataService,
    IEBookMetadataUpdater metadataUpdater,
    ILogger<UploadProcessingService> logger) : IUploadProcessingService
{
    public async Task<(EBookUploadProcessResult Result, IDictionary<string,string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct)
    {
        var root = config["PdfLibrary:RootFolder"];
        if (string.IsNullOrWhiteSpace(root)) 
            return (CreateErrorResult("", "Root folder not configured", EBookFormat.Pdf, new Dictionary<string,string>()), new Dictionary<string,string>(), false, "Root folder not configured");

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
        var format = DetectFormat(file.FileName);

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

        var savePath = GetUniqueEBookPath(titleFolder, info.Title, file.FileName);
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

        var baseName = Path.GetFileNameWithoutExtension(file.FileName);
        var volMatch = Regex.Match(baseName, @"(\b|_)(?:v|vol|volume)[ _-]?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (!volMatch.Success)
        {
            var numMatch = Regex.Match(baseName, @"\b(\d{1,3}(?:\.\d+)?)\b");
            volMatch = numMatch;
        }
        var volumeToken = volMatch.Success ? volMatch.Groups[2].Value : null;
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
            if (success) metadataUpdater.WriteKavitaSeriesMetadata(savePath, meta, info.Title);
            result = new(
                File: Path.GetFileName(savePath),
                Success: success,
                ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage,
                Attempts: attempts.Count,
                AppliedMetadata: meta,
                DirectAttemptSuccess: directOk,
                RepairAttemptSuccess: repairOk,
                ForceStripAttemptSuccess: false,
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
        new(fileName, false, errorMessage, 0, meta, false, false, false, false, format);

    private static EBookUploadProcessResult CreateCancelledResult(string fileName, EBookFormat format, IDictionary<string, string> meta) =>
        new(fileName, false, "Cancelled", 0, meta, false, false, false, false, format);

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
        if (numStr.Contains('.') && decimal.TryParse(numStr, out var dec)) 
            numStr = dec.ToString(CultureInfo.InvariantCulture);
        else if (int.TryParse(numStr, out var num)) 
            numStr = num.ToString();
        return $"{title} - Volume {numStr}";
    }

    private static string Sanitize(string name)
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
