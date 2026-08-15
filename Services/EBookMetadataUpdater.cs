using BooksMetadataBaker.Services.Helpers;
using BooksMetadataBaker.Services.Types;
using static BooksMetadataBaker.Services.Helpers.PdfGhostscript;
using static BooksMetadataBaker.Services.Helpers.MetadataHelpers;
using static BooksMetadataBaker.Services.Helpers.EBookSidecarWriter;

namespace BooksMetadataBaker.Services;

public class EBookMetadataUpdater : IEBookMetadataUpdater
{
    private const int GhostscriptTimeoutMs = 120_000;

    private readonly bool sidecarEnabled;
    private readonly bool gsEnabled;
    private readonly string gsPathCfg;
    private readonly string ebookMetaPathCfg;
    private readonly ILogger<EBookMetadataUpdater> logger;

    public EBookMetadataUpdater(IConfiguration config, ILogger<EBookMetadataUpdater> logger)
    {
        sidecarEnabled = !bool.TryParse(config["Tools:SidecarMetadataEnabled"], out var sc) || sc;
        gsEnabled = !bool.TryParse(config["Tools:GhostscriptEnabled"], out var gse) || gse;
        gsPathCfg = string.IsNullOrWhiteSpace(config["Tools:GhostscriptPath"]) ? "gs" : config["Tools:GhostscriptPath"]!;
        ebookMetaPathCfg = string.IsNullOrWhiteSpace(config["Tools:EbookMetaPath"]) ? "ebook-meta" : config["Tools:EbookMetaPath"]!;
        this.logger = logger;

        logger.LogInformation(
            "EBookMetadataUpdater initialized. SidecarEnabled={SidecarEnabled}, GhostscriptEnabled={GhostscriptEnabled}, GhostscriptPath={GhostscriptPath}, EbookMetaPath={EbookMetaPath}",
            sidecarEnabled,
            gsEnabled,
            gsPathCfg,
            ebookMetaPathCfg);

        if (CalibreMetadataUpdater.ResolveEbookMeta(ebookMetaPathCfg) is null)
            logger.LogWarning(
                "ebook-meta not found (configured path: {Path}). Metadata writing will fail until it is installed or Tools:EbookMetaPath is set.",
                ebookMetaPathCfg);
        if (gsEnabled && ResolveGhostscript(gsPathCfg) is null)
            logger.LogWarning(
                "ghostscript not found (configured path: {Path}). PDF repair will be unavailable until it is installed or Tools:GhostscriptPath is set.",
                gsPathCfg);
    }

    public async Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        var attempts = new List<EBookMetadataAttemptResult>(2);
        var request = new MetadataRequest(filePath, metadata, fallbackTitle);
        var format = DetectFormat(filePath);

        var direct = await DirectAttemptAsync(request, ct);
        attempts.Add(new EBookMetadataAttemptResult(
            filePath,
            EBookMetadataAttemptStage.Direct,
            direct.Success,
            direct.ErrorMessage,
            false,
            direct.Success));

        if (ct.IsCancellationRequested || direct.Success)
            return attempts;

        if (format == EBookFormat.Pdf && gsEnabled)
        {
            var repair = await RepairAttemptAsync(request, ct);
            attempts.Add(new EBookMetadataAttemptResult(
                filePath,
                EBookMetadataAttemptStage.Repair,
                repair.Success,
                repair.ErrorMessage,
                repair.GhostscriptRan,
                repair.Success));
        }
        else if (format == EBookFormat.Pdf && !gsEnabled)
        {
            logger.LogWarning("Skipping PDF repair attempt for {File}: Ghostscript disabled", filePath);
        }

        return attempts;
    }

    public async Task<DirectAttemptResult> DirectAttemptAsync(MetadataRequest request, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new DirectAttemptResult(false, "Cancelled");

        var (cleaned, cleanErr) = await CalibreMetadataUpdater.TryCleanEBookMetadataAsync(request.FilePath, ebookMetaPathCfg, logger, ct);
        if (!cleaned)
        {
            logger.LogWarning("Initial metadata cleanup failed for {File}: {Error}", request.FilePath, cleanErr);
            return new DirectAttemptResult(false, cleanErr ?? "Cleanup failed");
        }

        var (ok, err) = await CalibreMetadataUpdater.TryWriteMetadataWithCalibreAsync(
            request.FilePath,
            ebookMetaPathCfg,
            request.Metadata,
            request.FallbackTitle,
            logger,
            ct);

        return new DirectAttemptResult(ok, ok ? string.Empty : err ?? string.Empty);
    }

    public async Task<RepairAttemptResult> RepairAttemptAsync(MetadataRequest request, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new RepairAttemptResult(false, "Cancelled", false);

        string? errors = null;
        var gsRan = false;
        var extension = Path.GetExtension(request.FilePath);
        var (workDir, outputPath) = MetadataTemp.Prepare("repair", extension);

        try
        {
            var (gsOk, gsErr) = await RunGhostscriptTransformAsync(request.FilePath, outputPath, logger, gsPathCfg, GhostscriptTimeoutMs, ct);
            if (gsOk)
            {
                gsRan = true;
            }
            else
            {
                errors = Combine(errors, gsErr);
                outputPath = request.FilePath;
            }

            if (ct.IsCancellationRequested)
                return new RepairAttemptResult(false, "Cancelled", gsRan);

            var (cleaned, cleanErr) = await CalibreMetadataUpdater.TryCleanEBookMetadataAsync(outputPath, ebookMetaPathCfg, logger, ct);
            if (!cleaned)
            {
                errors = Combine(errors, cleanErr);
                logger.LogWarning("Repair path cleanup failed for {File}: {Error}", request.FilePath, cleanErr);
            }

            var (written, metaErr) = await CalibreMetadataUpdater.TryWriteMetadataWithCalibreAsync(outputPath, ebookMetaPathCfg, request.Metadata, request.FallbackTitle, logger, ct);
            if (written)
            {
                if (outputPath != request.FilePath && File.Exists(outputPath))
                    File.Copy(outputPath, request.FilePath, overwrite: true);
                return new RepairAttemptResult(true, errors, gsRan);
            }

            errors = Combine(errors, metaErr);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors = Combine(errors, ex.Message);
            logger.LogError(ex, "Repair attempt failed for {File}", request.FilePath);
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }

        return new RepairAttemptResult(false, errors, gsRan);
    }

    public void WriteSidecarSummary(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        bool success,
        string? errors,
        bool metadataApplied,
        bool ghostscriptRan)
    {
        if (!sidecarEnabled) return;
        Write(new SidecarSummary(
            filePath,
            metadata,
            fallbackTitle,
            success,
            errors,
            metadataApplied,
            ghostscriptRan), logger);
    }

    private static EBookFormat DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".epub" => EBookFormat.Epub,
            ".pdf" => EBookFormat.Pdf,
            _ => EBookFormat.Pdf
        };
    }
}
