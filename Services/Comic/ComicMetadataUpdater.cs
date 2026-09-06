using BooksMetadataBaker.Services.Abstract;
using BooksMetadataBaker.Services.Types;

namespace BooksMetadataBaker.Services.Comic;

/// <summary>
/// Comic/manga pipeline (plan §4.6): reads the existing in-archive
/// ComicInfo.xml, merges fetched metadata with filename-derived values, and
/// writes the merged ComicInfo.xml back into the archive. Single one-shot
/// attempt — no repair pass, no Ghostscript, no ebook-meta.
/// </summary>
public class ComicMetadataUpdater(
    IArchiveComicInfoWriter archiveWriter,
    ILogger<ComicMetadataUpdater> logger)
    : IComicMetadataUpdater
{
    /// <summary>
    /// User-facing note for the CBR-without-rar partial result: the file was
    /// saved and organized, only the metadata embedding was skipped.
    /// UploadProcessingService treats this specific error as a partial success (§4.7).
    /// </summary>
    public const string RarToolMissingError = "ComicInfo.xml not embedded (no RAR tool); file saved and organized";

    public async Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        ParsedComicFilename parsed,
        BookType type,
        CancellationToken ct,
        Func<string, string>? splitFileNameForVolume = null)
    {
        if (ct.IsCancellationRequested)
            return [new EBookMetadataAttemptResult(filePath, EBookMetadataAttemptStage.ComicInfo, false, "Cancelled", false, false, [])];

        ComicInfoModel? existing = null;
        try
        {
            existing = await archiveWriter.ReadExistingAsync(filePath, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read existing ComicInfo.xml from {File}; continuing without it", filePath);
        }

        // Multi-volume split: when the archive is a verifiable set of volume folders,
        // break it into one archive per volume, each with its own ComicInfo.xml. Any
        // doubt (unreadable entries, unparseable/duplicate volumes, loose files) falls
        // through to the single-archive path below — the file is left as-is.
        if (splitFileNameForVolume is not null)
        {
            var split = await TrySplitMultiVolumeAsync(
                filePath, existing, metadata, parsed, type, fallbackTitle, splitFileNameForVolume, ct);
            if (split is not null)
                return split;
        }

        int? pageCount = null;
        var count = await archiveWriter.CountPagesAsync(filePath, ct);
        if (count > 0)
            pageCount = count;

        var model = ComicInfoMapper.Map(existing, metadata, parsed, type, fallbackTitle, pageCount);
        var xml = ComicInfoXmlWriter.ToXml(model);

        var (ok, error) = await archiveWriter.WriteAsync(filePath, xml, ct);

        // In-place embedding failed (no RAR tool for .cbr, a 7-Zip/TAR hiccup, a corrupt
        // archive, etc.). Fall back to converting the archive to a .cbz with the
        // ComicInfo.xml embedded so every supported type gets its metadata. The file path
        // changes to .cbz when the format changes.
        if (!ok)
        {
            var (convOk, newPath, convErr) = await archiveWriter.ConvertToCbzAsync(filePath, xml, ct);
            if (convOk && newPath is not null)
            {
                logger.LogInformation("Converted {Old} to {New}; ComicInfo.xml embedded", filePath, newPath);
                filePath = newPath;
                ok = true;
                error = null;
            }
            else
            {
                logger.LogWarning("Archive→CBZ fallback failed for {File}: {Error}; leaving file as-is", filePath, convErr);
                if (string.Equals(error, ArchiveComicInfoWriter.RarToolUnavailableError, StringComparison.Ordinal))
                    error = RarToolMissingError;
            }
        }

        logger.LogInformation(
            "ComicInfo pipeline for {File}: Ok={Ok}, PageCount={PageCount}, Error={Error}",
            filePath, ok, pageCount, error);

        return [new EBookMetadataAttemptResult(filePath, EBookMetadataAttemptStage.ComicInfo, ok, ok ? null : error, false, ok, [])];
    }

    // Returns the split attempt on success, or null to fall back to the single-archive
    // path (layout not verifiable, entry listing failed, or the split itself failed —
    // in which case the source archive is intact and gets the normal treatment).
    private async Task<IReadOnlyList<EBookMetadataAttemptResult>?> TrySplitMultiVolumeAsync(
        string filePath,
        ComicInfoModel? existing,
        IDictionary<string, string> metadata,
        ParsedComicFilename parsed,
        BookType type,
        string fallbackTitle,
        Func<string, string> splitFileNameForVolume,
        CancellationToken ct)
    {
        (bool Ok, IReadOnlyList<string> Names, string? Error) entries;
        try
        {
            entries = await archiveWriter.ListEntriesAsync(filePath, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not list entries in {File}; skipping multi-volume split", filePath);
            return null;
        }
        if (!entries.Ok)
            return null;

        var plan = MultiVolumeSplitter.TryPlan(entries.Names);
        if (plan is null)
            return null;

        // Shared metadata is merged once; each volume overrides Volume and PageCount
        // with the folder-verified values (the folder wins over filename/existing).
        var baseModel = ComicInfoMapper.Map(existing, metadata, parsed, type, fallbackTitle, null);
        var xmls = new List<string>(plan.Parts.Count);
        foreach (var part in plan.Parts)
        {
            var m = baseModel.Clone();
            m.Volume = part.Volume;
            m.PageCount = part.PageCount > 0 ? part.PageCount : null;
            xmls.Add(ComicInfoXmlWriter.ToXml(m));
        }

        var targetDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var (ok, newPaths, error) = await archiveWriter.SplitToCbzAsync(
            filePath, plan, xmls, targetDir, splitFileNameForVolume, ct);
        if (!ok)
        {
            logger.LogWarning(
                "Multi-volume split failed for {File}: {Error}; leaving archive as a single file",
                filePath, error);
            return null;
        }

        logger.LogInformation(
            "Split {File} into {Count} volume archives: {Files}",
            filePath, newPaths.Count, string.Join(", ", newPaths));

        return [new EBookMetadataAttemptResult(
            newPaths[0],
            EBookMetadataAttemptStage.ComicInfo,
            true,
            null,
            false,
            true,
            newPaths.Count > 1 ? newPaths.Skip(1).ToList() : new List<string>())];
    }
}
