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
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return [new EBookMetadataAttemptResult(filePath, EBookMetadataAttemptStage.ComicInfo, false, "Cancelled", false, false)];

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

        return [new EBookMetadataAttemptResult(filePath, EBookMetadataAttemptStage.ComicInfo, ok, ok ? null : error, false, ok)];
    }
}
