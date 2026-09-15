using System.Formats.Tar;
using System.IO.Compression;
using BooksMetadataBaker.Services.Helpers;

namespace BooksMetadataBaker.Services.Comic;

    /// <summary>
    /// Reads/writes ComicInfo.xml inside comic archives (guide §4.5, D8).
    /// CBZ is handled in-process (entry order and stored/deflated flags preserved);
    /// CBT via extract+rebuild (uniform, per plan); CB7 via 7-Zip; CBR via 7-Zip
    /// (read) and WinRAR (write, optional). When an in-place write is impossible
    /// (no RAR tool) or fails, <see cref="ConvertToCbzAsync"/> repackages the
    /// archive as a .cbz with the metadata embedded so every supported type gets
    /// a ComicInfo.xml.
    /// </summary>
public class ArchiveComicInfoWriter : IArchiveComicInfoWriter
{
    private const int SevenZipTimeoutMs = 120_000;

    /// <summary>
    /// Marker error for the CBR-without-rar case: the archive is intact and the
    /// upload can still succeed overall (plan §4.7) — only the embedding was
    /// skipped. ComicMetadataUpdater maps this to its user-facing message.
    /// </summary>
    public const string RarToolUnavailableError = "RAR tool not available — ComicInfo.xml not embedded";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif", ".tiff", ".tif", ".heic", ".heif"
    };

    // Guide §6: root-level entries named exactly cover/!cover/folder are not pages.
    private static readonly HashSet<string> CoverBasenameExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cover", "!cover", "folder"
    };

    private readonly string sevenZipPathCfg;
    private readonly string rarPathCfg;
    private readonly ILogger<ArchiveComicInfoWriter> logger;

    public ArchiveComicInfoWriter(IConfiguration config, ILogger<ArchiveComicInfoWriter> logger)
    {
        sevenZipPathCfg = string.IsNullOrWhiteSpace(config["Tools:SevenZipPath"]) ? "7z" : config["Tools:SevenZipPath"]!;
        rarPathCfg = config["Tools:RarPath"] ?? string.Empty;
        this.logger = logger;

        if (ResolveSevenZip() is null)
            logger.LogInformation(
                "7-Zip not found (configured path: {Path}). CB7/CBR support will be unavailable until it is installed or Tools:SevenZipPath is set.",
                sevenZipPathCfg);
        if (string.IsNullOrWhiteSpace(rarPathCfg))
            logger.LogInformation(
                "Tools:RarPath not configured. CBR ComicInfo.xml embedding will be skipped (files are still saved and organized).");
    }

    public Task<ComicInfoModel?> ReadExistingAsync(string path, CancellationToken ct)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cbz" => ReadZipAsync(path),
            ".cbt" => ReadTarAsync(path, ct),
            ".cb7" or ".cbr" => ReadSevenZipAsync(path, ct),
            _ => Task.FromResult<ComicInfoModel?>(null)
        };
    }

    public Task<int> CountPagesAsync(string path, CancellationToken ct)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cbz" => Task.FromResult(CountZipPages(path)),
            ".cbt" => CountTarPagesAsync(path, ct),
            ".cb7" or ".cbr" => CountSevenZipPagesAsync(path, ct),
            _ => Task.FromResult(0)
        };
    }

    public Task<(bool Ok, string? Error)> WriteAsync(string path, string xml, CancellationToken ct)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cbz" => WriteZipAsync(path, xml, ct),
            ".cbt" => WriteTarAsync(path, xml, ct),
            ".cb7" => WriteSevenZipAsync(path, xml, ct),
            ".cbr" => WriteRarAsync(path, xml, ct),
            _ => Task.FromResult((false, (string?)"Unsupported archive format"))
        };
    }

    // --- ZIP (CBZ) ---------------------------------------------------------

    private Task<ComicInfoModel?> ReadZipAsync(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => IsRootComicInfo(e.FullName));
            if (entry is null)
                return Task.FromResult<ComicInfoModel?>(null);
                using var s = entry.Open();
                using var reader = new StreamReader(s);
                return Task.FromResult(ComicInfoXmlReader.FromXml(reader.ReadToEnd(), logger));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read ComicInfo.xml from {File}", path);
            return Task.FromResult<ComicInfoModel?>(null);
        }
    }

    private int CountZipPages(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return CountPages(zip.Entries.Select(e => e.FullName));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to count pages in {File}", path);
            return 0;
        }
    }

    private async Task<(bool Ok, string? Error)> WriteZipAsync(string path, string xml, CancellationToken ct)
    {
        var tmpPath = NewTempPath(path);
        try
        {
            var xmlBytes = Encoding.UTF8.GetBytes(xml);
            // Block-scoped so all handles are released before the atomic move.
            await using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
            await using (var srcZip = new ZipArchive(src, ZipArchiveMode.Read, leaveOpen: true))
            await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            await using (var dstZip = new ZipArchive(dst, ZipArchiveMode.Create, leaveOpen: true))
            {
                var replaced = false;
                foreach (var entry in srcZip.Entries)
                {
                    if (IsRootComicInfo(entry.FullName))
                    {
                        if (!replaced)
                        {
                            await WriteZipEntryAsync(dstZip, entry.FullName, xmlBytes, CompressionLevel.Optimal, ct);
                            replaced = true;
                        }
                        continue;
                    }

                    var copy = dstZip.CreateEntry(entry.FullName, ToCompressionLevel(entry));
                    await using var inStream = entry.Open();
                    await using var outStream = copy.Open();
                    await inStream.CopyToAsync(outStream, ct);
                }

                if (!replaced)
                    await WriteZipEntryAsync(dstZip, ComicInfoXmlWriter.FileName, xmlBytes, CompressionLevel.Optimal, ct);
            }

            File.Move(tmpPath, path, overwrite: true);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmpPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tmpPath);
            logger.LogError(ex, "Failed to write ComicInfo.xml into {File}", path);
            return (false, ex.Message);
        }
    }

    // --- TAR (CBT) ---------------------------------------------------------

    private async Task<ComicInfoModel?> ReadTarAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var tar = new TarReader(fs, leaveOpen: true);
            while (true)
            {
                var entry = await tar.GetNextEntryAsync(copyData: false, ct);
                if (entry is null)
                    break;
                var dataStream = entry.DataStream;
                if (!IsRootComicInfo(entry.Name) || dataStream is null)
                    continue;
                await using var s = dataStream;
                using var reader = new StreamReader(s);
                return ComicInfoXmlReader.FromXml(await reader.ReadToEndAsync(ct), logger);
            }
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read ComicInfo.xml from {File}", path);
            return null;
        }
    }

    private async Task<int> CountTarPagesAsync(string path, CancellationToken ct)
    {
        try
        {
            var count = 0;
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var tar = new TarReader(fs, leaveOpen: true);
            while (true)
            {
                var entry = await tar.GetNextEntryAsync(copyData: false, ct);
                if (entry is null)
                    break;
                if (IsPageEntry(entry.Name))
                    count++;
            }
            return count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to count pages in {File}", path);
            return 0;
        }
    }

    private async Task<(bool Ok, string? Error)> WriteTarAsync(string path, string xml, CancellationToken ct)
    {
        // Extract + rebuild (uniform strategy per plan; tar has no in-place update).
        var tmpPath = NewTempPath(path);
        var workDir = NewWorkDir("cbtwrite");
        try
        {
            await TarFile.ExtractToDirectoryAsync(path, workDir, overwriteFiles: true, ct);

            // Remove any existing root-level ComicInfo.xml (case-insensitive), then write the merged one.
            foreach (var f in Directory.EnumerateFiles(workDir))
            {
                if (IsRootComicInfo(Path.GetFileName(f)))
                    File.Delete(f);
            }
            await File.WriteAllTextAsync(Path.Combine(workDir, ComicInfoXmlWriter.FileName), xml, ct);

            // Dispose the stream before the move: File.Move fails on Windows while the
            // target file is held open by our own handle.
            await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
            {
                await TarFile.CreateFromDirectoryAsync(workDir, dst, includeBaseDirectory: false, ct);
            }

            File.Move(tmpPath, path, overwrite: true);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmpPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tmpPath);
            logger.LogError(ex, "Failed to write ComicInfo.xml into {File}", path);
            return (false, ex.Message);
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }
    }

    // --- 7-Zip (CB7) / RAR (CBR) -------------------------------------------

    private async Task<ComicInfoModel?> ReadSevenZipAsync(string path, CancellationToken ct)
    {
        var exe = ResolveSevenZip();
        if (exe is null)
        {
            logger.LogWarning("Cannot read {File}: 7-Zip not available", path);
            return null;
        }

        var workDir = NewWorkDir("7zread");
        try
        {
            var (ok, _, _, stderr, runErr) = await ProcessRunner.RunAsync(
                exe, BuildExtractArgs(path, workDir), logger, SevenZipTimeoutMs, ct);
            if (!ok)
            {
                logger.LogWarning("7-Zip extract failed for {File}: {Error}", path, runErr ?? stderr.Trim());
                return null;
            }
            var xmlPath = Path.Combine(workDir, ComicInfoXmlWriter.FileName);
            if (!File.Exists(xmlPath))
                return null;
            return ComicInfoXmlReader.FromXml(await File.ReadAllTextAsync(xmlPath, ct), logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read ComicInfo.xml from {File}", path);
            return null;
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }
    }

    private async Task<int> CountSevenZipPagesAsync(string path, CancellationToken ct)
    {
        var exe = ResolveSevenZip();
        if (exe is null)
            return 0;

        var workDir = NewWorkDir("7zcount");
        try
        {
            var (ok, _, _, stderr, runErr) = await ProcessRunner.RunAsync(
                exe, BuildExtractArgs(path, workDir), logger, SevenZipTimeoutMs, ct);
            if (!ok)
            {
                logger.LogWarning("7-Zip extract failed for page count of {File}: {Error}", path, runErr ?? stderr.Trim());
                return 0;
            }
            return CountPages(Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(workDir, f)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to count pages in {File}", path);
            return 0;
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }
    }

    private async Task<(bool Ok, string? Error)> WriteSevenZipAsync(string path, string xml, CancellationToken ct)
    {
        var exe = ResolveSevenZip();
        if (exe is null)
            return (false, "7-Zip not available — ComicInfo.xml not embedded");

        var workDir = NewWorkDir("7zwrite");
        var xmlPath = Path.Combine(workDir, ComicInfoXmlWriter.FileName);
        try
        {
            await File.WriteAllTextAsync(xmlPath, xml, ct);

            var (ok, _, stdout, stderr, runErr) = await ProcessRunner.RunAsync(
                exe, BuildAddArgs(path), logger, SevenZipTimeoutMs, ct, workingDirectory: workDir);
            if (ok && runErr is null)
                return (true, null);

            logger.LogWarning(
                "7-Zip in-place update failed for {File}: {Error}. Falling back to extract+rebuild.",
                path, runErr ?? (stderr + stdout).Trim());

            Directory.Delete(workDir, recursive: true);
            Directory.CreateDirectory(workDir);

            var (xOk, _, xOut, xErr, xRunErr) = await ProcessRunner.RunAsync(
                exe, BuildExtractArgs(path, workDir), logger, SevenZipTimeoutMs, ct);
            if (!xOk || xRunErr is not null)
                return (false, $"7-Zip extract failed: {xRunErr ?? (xErr + xOut).Trim()}");

            await File.WriteAllTextAsync(xmlPath, xml, ct);

            var tmpPath = NewTempPath(path);
            var (aOk, _, aOut, aErr, aRunErr) = await ProcessRunner.RunAsync(
                exe, BuildCreateArgs(tmpPath, workDir), logger, SevenZipTimeoutMs, ct);
            if (!aOk || aRunErr is not null)
            {
                TryDelete(tmpPath);
                return (false, $"7-Zip rebuild failed: {aRunErr ?? (aErr + aOut).Trim()}");
            }

            File.Move(tmpPath, path, overwrite: true);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write ComicInfo.xml into {File}", path);
            return (false, ex.Message);
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }
    }

    private async Task<(bool Ok, string? Error)> WriteRarAsync(string path, string xml, CancellationToken ct)
    {
        var exe = ResolveRar();
        if (exe is null)
            return (false, RarToolUnavailableError);

        var workDir = NewWorkDir("rarwrite");
        var xmlPath = Path.Combine(workDir, ComicInfoXmlWriter.FileName);
        try
        {
            await File.WriteAllTextAsync(xmlPath, xml, ct);
            var (ok, _, stdout, stderr, runErr) = await ProcessRunner.RunAsync(
                exe, BuildAddArgs(path), logger, SevenZipTimeoutMs, ct, workingDirectory: workDir);
            if (ok && runErr is null)
                return (true, null);
            var detail = (stderr + stdout).Trim();
            return (false, runErr ?? (detail.Length > 0 ? $"rar failed: {detail}" : "rar failed"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write ComicInfo.xml into {File}", path);
            return (false, ex.Message);
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }
    }

    public async Task<(bool Ok, string? NewPath, string? Error)> ConvertToCbzAsync(string path, string xml, CancellationToken ct)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".cbz" or ".cbt" or ".cb7" or ".cbr"))
            return (false, null, "Unsupported archive format");

        var newPath = Path.ChangeExtension(path, ".cbz");
        var tmpPath = NewTempPath(newPath);
        var workDir = NewWorkDir("tocabz");
        try
        {
            // 1. Extract the source archive into a clean work dir.
            switch (ext)
            {
                case ".cbz":
                    ZipFile.ExtractToDirectory(path, workDir, overwriteFiles: true);
                    break;
                case ".cbt":
                    await TarFile.ExtractToDirectoryAsync(path, workDir, overwriteFiles: true, ct);
                    break;
                default: // .cb7 or .cbr — 7-Zip has both 7z and RAR read support.
                    var exe = ResolveSevenZip();
                    if (exe is null)
                        return (false, null, "7-Zip not available — cannot convert to CBZ");
                    var (xOk, _, xOut, xErr, xRunErr) = await ProcessRunner.RunAsync(
                        exe, BuildExtractArgs(path, workDir), logger, SevenZipTimeoutMs, ct);
                    if (!xOk || xRunErr is not null)
                        return (false, null, $"7-Zip extract failed: {xRunErr ?? (xErr + xOut).Trim()}");
                    break;
            }

            // 2. Drop any pre-existing root ComicInfo.xml, then write the merged one.
            foreach (var f in Directory.EnumerateFiles(workDir))
            {
                if (IsRootComicInfo(Path.GetFileName(f)))
                    File.Delete(f);
            }
            await File.WriteAllTextAsync(Path.Combine(workDir, ComicInfoXmlWriter.FileName), xml, ct);

            // 3. Repackage as a ZIP (.cbz). Page images are already compressed, so store
            //    (no deflate) — fast and the standard for CBZ.
            await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
            {
                ZipFile.CreateFromDirectory(workDir, dst, CompressionLevel.NoCompression, includeBaseDirectory: false);
            }

            // 4. Swap: move the new .cbz into place. If the format changed, remove the
            //    original and its now-stale sidecar (written as <path>.meta.json).
            File.Move(tmpPath, newPath, overwrite: true);
            if (!string.Equals(newPath, path, StringComparison.Ordinal))
            {
                TryDelete(path);
                TryDelete(path + ".meta.json");
            }
            return (true, newPath, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmpPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tmpPath);
            logger.LogError(ex, "Failed to convert {File} to CBZ", path);
            return (false, null, ex.Message);
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }
    }

    // --- Entry listing / multi-volume split ----------------------------------

    public Task<(bool Ok, IReadOnlyList<string> Names, string? Error)> ListEntriesAsync(string path, CancellationToken ct)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cbz" => ListZipEntries(path),
            ".cbt" => ListTarEntriesAsync(path, ct),
            ".cb7" or ".cbr" => ListSevenZipEntriesAsync(path, ct),
            _ => Task.FromResult<(bool, IReadOnlyList<string>, string?)>((false, Array.Empty<string>(), "Unsupported archive format"))
        };
    }

    private Task<(bool Ok, IReadOnlyList<string> Names, string? Error)> ListZipEntries(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            return Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, names, null));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list entries in {File}", path);
            return Task.FromResult<(bool, IReadOnlyList<string>, string?)>((false, Array.Empty<string>(), ex.Message));
        }
    }

    private async Task<(bool Ok, IReadOnlyList<string> Names, string? Error)> ListTarEntriesAsync(string path, CancellationToken ct)
    {
        try
        {
            var names = new List<string>();
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var tar = new TarReader(fs, leaveOpen: true);
            while (true)
            {
                var entry = await tar.GetNextEntryAsync(copyData: false, ct);
                if (entry is null)
                    break;
                names.Add(entry.Name.Replace('\\', '/'));
            }
            return (Ok: true, Names: names, Error: (string?)null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list entries in {File}", path);
            return (Ok: false, Names: Array.Empty<string>(), Error: ex.Message);
        }
    }

    private async Task<(bool Ok, IReadOnlyList<string> Names, string? Error)> ListSevenZipEntriesAsync(string path, CancellationToken ct)
    {
        var exe = ResolveSevenZip();
        if (exe is null)
        {
            logger.LogWarning("Cannot list {File}: 7-Zip not available", path);
            return (Ok: false, Names: Array.Empty<string>(), Error: "7-Zip not available");
        }

        try
        {
            var (ok, _, stdout, stderr, runErr) = await ProcessRunner.RunAsync(
                exe, ["l", "-slt", path], logger, SevenZipTimeoutMs, ct);
            if (!ok || runErr is not null)
                return (Ok: false, Names: Array.Empty<string>(), Error: runErr ?? (stderr + stdout).Trim());

            // -slt emits one block per entry after the first "----------" separator;
            // the block's "Path =" line is the entry name and "Attributes =" carries
            // 'D' for directories (containers that store directory entries).
            var names = new List<string>();
            var inEntries = false;
            string? current = null;
            foreach (var rawLine in stdout.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (!inEntries)
                {
                    if (line == "----------")
                        inEntries = true;
                    continue;
                }
                if (line.StartsWith("Path = ", StringComparison.Ordinal))
                {
                    if (current is not null)
                        names.Add(current);
                    current = line["Path = ".Length..];
                }
                else if (line.StartsWith("Attributes = ", StringComparison.Ordinal) && current is not null)
                {
                    names.Add(line["Attributes = ".Length..].Contains('D') ? current + "/" : current);
                    current = null;
                }
            }
            if (current is not null)
                names.Add(current);

            return (Ok: true, Names: names, Error: (string?)null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list entries in {File}", path);
            return (Ok: false, Names: Array.Empty<string>(), Error: ex.Message);
        }
    }

    public async Task<(bool Ok, IReadOnlyList<string> NewPaths, string? Error)> SplitToCbzAsync(
        string path,
        MultiVolumeSplitPlan plan,
        IReadOnlyList<string> xmls,
        string targetDir,
        Func<string, string> fileNameForVolume,
        CancellationToken ct)
    {
        if (xmls.Count != plan.Parts.Count)
            return (false, Array.Empty<string>(), "Split plan and XML count mismatch");

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".cbz" or ".cbt" or ".cb7" or ".cbr"))
            return (false, Array.Empty<string>(), "Unsupported archive format");

        var workDir = NewWorkDir("split");
        var written = new List<string>(plan.Parts.Count);
        var ok = false;
        try
        {
            // 1. Extract the source once (uniform for all containers).
            switch (ext)
            {
                case ".cbz":
                    ZipFile.ExtractToDirectory(path, workDir, overwriteFiles: true);
                    break;
                case ".cbt":
                    await TarFile.ExtractToDirectoryAsync(path, workDir, overwriteFiles: true, ct);
                    break;
                default: // .cb7 or .cbr — 7-Zip reads both.
                    var exe = ResolveSevenZip();
                    if (exe is null)
                        return (false, Array.Empty<string>(), "7-Zip not available — cannot split archive");
                    var (xOk, _, xOut, xErr, xRunErr) = await ProcessRunner.RunAsync(
                        exe, BuildExtractArgs(path, workDir), logger, SevenZipTimeoutMs, ct);
                    if (!xOk || xRunErr is not null)
                        return (false, Array.Empty<string>(), $"7-Zip extract failed: {xRunErr ?? (xErr + xOut).Trim()}");
                    break;
            }

            Directory.CreateDirectory(targetDir);

            // 2. One .cbz per volume folder, each with its own root ComicInfo.xml.
            for (var i = 0; i < plan.Parts.Count; i++)
            {
                var part = plan.Parts[i];
                var folder = Path.Combine(
                    workDir,
                    part.FolderPrefix.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar));
                if (!Directory.Exists(folder))
                {
                    // Defensive: the plan was computed from the entry list, so this
                    // should not happen (e.g. directory entries the container hid).
                    return (false, Array.Empty<string>(), $"Volume folder not found after extraction: {part.FolderPrefix}");
                }

                var target = Path.Combine(targetDir, fileNameForVolume(part.Volume));
                var tmpPath = NewTempPath(target);
                var xmlBytes = Encoding.UTF8.GetBytes(xmls[i]);

                await using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await using (var zip = new ZipArchive(dst, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        // Preserve the folder-internal structure, '/'-separated (ZIP convention).
                        var rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
                        var entry = zip.CreateEntry(rel, CompressionLevel.NoCompression);
                        await using var inStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                        await using var outStream = await entry.OpenAsync(ct);
                        await inStream.CopyToAsync(outStream, ct);
                    }

                    var xmlEntry = zip.CreateEntry(ComicInfoXmlWriter.FileName, CompressionLevel.Optimal);
                    await using var xs = await xmlEntry.OpenAsync(ct);
                    await xs.WriteAsync(xmlBytes, ct);
                }

                File.Move(tmpPath, target, overwrite: true);
                written.Add(target);
            }

            // 3. All parts written — remove the source (and its sidecar). A part may
            //    legitimately share the source's path (same volume, .cbz source);
            //    File.Move already replaced it, so don't delete it twice.
            if (!written.Any(w => string.Equals(w, path, PathComparison())))
                TryDelete(path);
            TryDelete(path + ".meta.json");

            ok = true;
            return (true, written, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to split {File} into volume archives", path);
            return (false, Array.Empty<string>(), ex.Message);
        }
        finally
        {
            if (!ok)
                foreach (var w in written)
                    TryDelete(w);
            MetadataTemp.Cleanup(workDir);
        }
    }

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // --- Helpers -------------------------------------------------------------

    private string? ResolveSevenZip()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(sevenZipPathCfg))
            candidates.Add(sevenZipPathCfg);
        candidates.AddRange(["7z", "7zz"]);
        return ToolResolver.Resolve(sevenZipPathCfg, candidates);
    }

    private string? ResolveRar()
    {
        if (string.IsNullOrWhiteSpace(rarPathCfg))
            return null;
        return ToolResolver.Resolve(rarPathCfg, [rarPathCfg, "rar"]);
    }

    private static bool IsRootComicInfo(string? name)
    {
        return !string.IsNullOrEmpty(name)
            && !name.Contains('/')
            && !name.Contains('\\')
            && string.Equals(name, ComicInfoXmlWriter.FileName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPageEntry(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (!ImageExtensions.Contains(Path.GetExtension(name)))
            return false;
        var hasDir = name.Contains('/') || name.Contains('\\');
        if (!hasDir && CoverBasenameExclusions.Contains(Path.GetFileNameWithoutExtension(name)))
            return false;
        return true;
    }

    public static bool IsImageFile(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return ImageExtensions.Contains(Path.GetExtension(name));
    }

    private static int CountPages(IEnumerable<string?> names)
    {
        var count = 0;
        foreach (var n in names)
        {
            if (IsPageEntry(n))
                count++;
        }
        return count;
    }

    private static async Task WriteZipEntryAsync(ZipArchive zip, string name, byte[] data, CompressionLevel level, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, level);
        await using var s = await entry.OpenAsync(ct);
        await s.WriteAsync(data, ct);
    }

    // .NET 10's ZipArchiveEntry no longer exposes the compression method; a stored
    // entry always has CompressedLength == Length, so use that to keep stored entries stored.
    private static CompressionLevel ToCompressionLevel(ZipArchiveEntry entry) =>
        entry.CompressedLength == entry.Length ? CompressionLevel.NoCompression : CompressionLevel.Optimal;

    private static string NewWorkDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"temp_meta_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string NewTempPath(string path) =>
        Path.Combine(Path.GetDirectoryName(path)!, path + ".tmp-" + Guid.NewGuid().ToString("N"));

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    // 7-Zip requires the -o directory to end with a separator; without it only the
    // first character is treated as the output path (e.g. -o/tmp/x extracts to /).
    private static List<string> BuildExtractArgs(string archivePath, string workDir) =>
        ["x", $"-o{workDir}{Path.DirectorySeparatorChar}", "-y", "-bso0", "-bsp0", archivePath];

    // 7-Zip/WinRAR store added files with their full path when given an absolute
    // path, so ComicInfo.xml must be added by name from its own directory to land
    // at the archive root.
    private static List<string> BuildAddArgs(string archivePath) =>
        ["a", archivePath, ComicInfoXmlWriter.FileName];

    private static List<string> BuildCreateArgs(string archivePath, string workDir)
    {
        var type = Path.GetExtension(archivePath).TrimStart('.').ToUpperInvariant();
        return ["a", $"-t{type}", archivePath, workDir];
    }
}
