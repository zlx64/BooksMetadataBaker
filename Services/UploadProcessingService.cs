using System.Globalization;
using System.Collections.Concurrent;
using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Helpers;
using BooksMetadataBaker.Services.Types;

namespace BooksMetadataBaker.Services;

public class UploadProcessingService(
    IConfiguration config,
    IAggregatedMetadataService metadataService,
    IEBookMetadataUpdater metadataUpdater,
    IComicMetadataUpdater comicMetadataUpdater,
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
        {
            logger.LogWarning("Upload rejected: ROOT_DIR not configured and type folder '{TypeFolder}' is not absolute", typeFolderRaw);
            return (CreateErrorResult("", "ROOT_DIR not configured and type folder is not absolute", EBookFormat.Pdf, new Dictionary<string,string>()), new Dictionary<string,string>(), false, "ROOT_DIR not configured and type folder is not absolute");
        }

        var baseFolder = isAbsolute
            ? typeFolderRaw
            : Path.Combine(root!, Sanitize(typeFolderRaw));
        var format = DetectFormat(file.FileName);
        var isArchive = format.IsArchive();
        var specialsSubfolder = !bool.TryParse(config["MangaComics:SpecialsSubfolder"], out var ss) || ss;
        var useCurlyBraceYear = bool.TryParse(config["MangaComics:UseCurlyBraceYear"], out var cby) && cby;

        // D11: opt-in conversion of a trailing "(YYYY)" in the folder name to
        // "{YYYY}" (guide §2 — parentheses are stripped by Kavita's parser).
        // Archives only; the user-supplied title used for metadata lookup stays as-is.
        var folderTitle = isArchive && useCurlyBraceYear
            ? Regex.Replace(info.Title, @"\((\d{4})\)\s*$", "{$1}")
            : info.Title;

        var titleFolder = ResolveTitleFolder(baseFolder, folderTitle, out var pathError);
        if (titleFolder is null)
        {
            logger.LogError("Rejected title that escapes library folder: {Title}", folderTitle);
            return (CreateErrorResult("", pathError ?? "Invalid title", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, pathError ?? "Invalid title");
        }

        ParsedComicFilename? parsed = null;
        string savePath;
        try
        {
            savePath = isArchive
                ? GetArchiveSavePath(titleFolder, folderTitle, format.ToExtension(), parsed = ComicFilenameParser.Parse(file.FileName), specialsSubfolder)
                : GetUniqueEBookPath(titleFolder, info.Title, file.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compute save path for {Name} (title={Title}, type={Type})", file.FileName, info.Title, info.Type);
            return (CreateErrorResult("", $"Save path resolution failed: {ex.GetType().Name}: {ex.Message}", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, ex.Message);
        }
        var saveDir = Path.GetDirectoryName(savePath)!;
        var fileLock = GetFileLock(Path.GetFullPath(savePath));
        try
        {
            await fileLock.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return (CreateCancelledResult(Path.GetFileName(savePath), format, new Dictionary<string,string>()), new Dictionary<string,string>(), true, null);
        }
        try
        {
            try
            {
                Directory.CreateDirectory(saveDir);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create directory {Dir}", saveDir);
                return (CreateErrorResult("", $"Create directory failed: {ex.GetType().Name}: {ex.Message}", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, ex.Message);
            }

            if (!IsWritableDirectory(saveDir))
            {
                logger.LogError("Directory not writable: {Dir}", saveDir);
                return (CreateErrorResult("", $"Directory not writable: {saveDir}", format, new Dictionary<string,string>()), new Dictionary<string,string>(), false, $"Directory not writable: {saveDir}");
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
                if (isArchive)
                {
                    // Comic/manga pipeline (plan §4.7): ComicInfo.xml embedding only —
                    // no Calibre/Ghostscript. CBR without a RAR tool is a partial
                    // success: the file was saved and organized, embedding skipped.
                    var attempts = await comicMetadataUpdater.RunPipelineAsync(savePath, meta, info.Title, parsed!, info.Type, ct);
                    // The pipeline may have converted the archive to a .cbz (e.g. no RAR tool
                    // for .cbr, or an in-place write failure); use the (possibly new) path for
                    // all downstream bookkeeping.
                    var finalPath = attempts.FirstOrDefault(a => a.Stage == EBookMetadataAttemptStage.ComicInfo)?.FilePath ?? savePath;
                    // If the pipeline repackaged the archive as a .cbz, reflect the new format.
                    // Any other (unchanged) path keeps its original format.
                    var finalFormat = finalPath.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase)
                        ? EBookFormat.Cbz
                        : format;
                    var comicOk = attempts.Any(a => a is { Stage: EBookMetadataAttemptStage.ComicInfo, Success: true });
                    var rarPartial = attempts.Any(a => a is
                    {
                        Stage: EBookMetadataAttemptStage.ComicInfo,
                        Success: false,
                        ErrorMessage: ComicMetadataUpdater.RarToolMissingError
                    });
                    var success = comicOk || rarPartial;
                    var errorMessage = CombineErrors(attempts);
                    metadataUpdater.WriteSidecarSummary(finalPath, meta, info.Title, success, errorMessage, comicOk, false);
                    if (success) await kavitaWriter.WriteAsync(finalPath, meta, info.Title);
                    result = new(
                        File: Path.GetFileName(finalPath),
                        Success: success,
                        ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage,
                        Attempts: attempts.Count,
                        AppliedMetadata: meta,
                        DirectAttemptSuccess: false,
                        RepairAttemptSuccess: false,
                        GhostscriptRan: false,
                        Format: finalFormat,
                        ComicInfoWritten: comicOk,
                        PageCount: GetMetaPageCount(meta));
                    if (rarPartial)
                        logger.LogWarning("ComicInfo.xml not embedded for {File}: {Error}", finalPath, errorMessage);
                }
                else
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
                        Format: format,
                        ComicInfoWritten: false,
                        PageCount: 0);
                    if (!success && !string.IsNullOrWhiteSpace(errorMessage)) 
                        logger.LogWarning("Metadata update failed for {File}: {Errors}", savePath, errorMessage);
                }
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

    // Raw archive containers map to their Kavita format (P9): a .zip of pages is
    // a .cbz, a .7z is a .cb7, a .rar is a .cbr, a .tar is a .cbt — same container
    // technology, so the file is saved under the Kavita extension and the existing
    // in-place ComicInfo embedding applies without recompression.
    private static EBookFormat DetectFormat(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".epub" => EBookFormat.Epub,
            ".pdf" => EBookFormat.Pdf,
            ".cbz" or ".zip" => EBookFormat.Cbz,
            ".cbr" or ".rar" => EBookFormat.Cbr,
            ".cb7" or ".7z" => EBookFormat.Cb7,
            ".cbt" or ".tar" => EBookFormat.Cbt,
            _ => EBookFormat.Pdf
        };
    }

    private static EBookUploadProcessResult CreateErrorResult(string fileName, string errorMessage, EBookFormat format, IDictionary<string, string> meta) =>
        new(fileName, false, errorMessage, 0, meta, false, false, false, format, false, 0);

    private static EBookUploadProcessResult CreateCancelledResult(string fileName, EBookFormat format, IDictionary<string, string> meta) =>
        new(fileName, false, "Cancelled", 0, meta, false, false, false, format, false, 0);

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

    /// <summary>
    /// Archive save path (plan §4.7, D5/D10): volume → `<Title> - Volume N.ext`;
    /// chapter (no volume) → `<Title> - Chapter N.ext`; special → `<Title> SP##.ext`
    /// inside `<Title>/Specials/` (or the title folder when the specials subfolder
    /// is disabled). SP## = highest existing `<Title> SP\d+` file (title folder and
    /// Specials/) plus one; the first special gets SP01. Creates the target
    /// directory when needed. <paramref name="extension"/> is the *output*
    /// (Kavita) extension — raw containers like .zip/.7z/.rar are saved under
    /// their converted .cbz/.cb7/.cbr extension (P9).
    /// </summary>
    public static string GetArchiveSavePath(string folder, string title, string extension, ParsedComicFilename parsed, bool specialsSubfolder)
    {
        if (parsed.Volume is not null)
            return Path.Combine(folder, Sanitize(BuildVolumeName(title, parsed.Volume)) + extension);
        if (parsed.Chapter is not null)
            return Path.Combine(folder, Sanitize(BuildChapterName(title, parsed.Chapter)) + extension);

        var specialsDir = specialsSubfolder ? Path.Combine(folder, "Specials") : folder;
        Directory.CreateDirectory(specialsDir);
        var sp = NextSpecialNumber(folder, specialsDir, title);
        return Path.Combine(specialsDir, Sanitize($"{title} SP{sp:D2}") + extension);
    }

    private static int NextSpecialNumber(string folder, string specialsDir, string title)
    {
        var pattern = $"^{Regex.Escape(Sanitize(title))}\\s+SP(\\d+)$";
        var max = 0;
        foreach (var dir in new[] { folder, specialsDir }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var match = Regex.Match(Path.GetFileNameWithoutExtension(file), pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var n) && n > max)
                    max = n;
            }
        }
        return max + 1;
    }

    private static string BuildVolumeName(string title, string numStr) =>
        $"{title} - Volume {NormalizeNumber(numStr)}";

    private static string BuildChapterName(string title, string numStr) =>
        $"{title} - Chapter {NormalizeNumber(numStr)}";

    private static string NormalizeNumber(string numStr)
    {
        if (numStr.Contains('.') && decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
            return dec.ToString(CultureInfo.InvariantCulture);
        if (int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            return num.ToString(CultureInfo.InvariantCulture);
        return numStr;
    }

    // Fetched PageCount (e.g. ComicVine page_count) for the result; 0 when absent.
    private static int GetMetaPageCount(IDictionary<string, string> meta)
    {
        return meta.TryGetValue("PageCount", out var v) && int.TryParse(v, out var n) && n > 0 ? n : 0;
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
        // Platform-native invalid chars (keeps Linux titles like "Dune: Part Two"
        // intact). Path traversal is still blocked by ResolveTitleFolder's
        // containment check.
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
