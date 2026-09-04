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
        if (!ok && string.Equals(error, ArchiveComicInfoWriter.RarToolUnavailableError, StringComparison.Ordinal))
            error = RarToolMissingError;

        logger.LogInformation(
            "ComicInfo pipeline for {File}: Ok={Ok}, PageCount={PageCount}, Error={Error}",
            filePath, ok, pageCount, error);

        return [new EBookMetadataAttemptResult(filePath, EBookMetadataAttemptStage.ComicInfo, ok, ok ? null : error, false, ok)];
    }
}
