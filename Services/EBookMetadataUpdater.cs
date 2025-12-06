using System.Text.Json;
using PrepKavitaPdf.Services.Abstract;
using PrepKavitaPdf.Services.Helpers;
using PrepKavitaPdf.Services.Types;
using PrepKavitaPdf.Models;
using static PrepKavitaPdf.Services.Helpers.PdfGhostscript;
using static PrepKavitaPdf.Services.CalibreMetadataUpdater;
using static PrepKavitaPdf.Services.Helpers.MetadataHelpers;
using static PrepKavitaPdf.Services.Helpers.EBookSidecarWriter;

namespace PrepKavitaPdf.Services;

public class EBookMetadataUpdater : IEBookMetadataUpdater
{
    private const int GhostscriptTimeoutMs = 120_000;

    private readonly bool sidecarEnabled;
    private readonly bool gsEnabled;
    private readonly string gsPathCfg;
    private readonly ILogger<EBookMetadataUpdater> logger;

    public EBookMetadataUpdater(IConfiguration config, ILogger<EBookMetadataUpdater> logger)
    {
        sidecarEnabled = !bool.TryParse(config["Tools:SidecarMetadataEnabled"], out var sc) || sc;
        gsEnabled = !bool.TryParse(config["Tools:GhostscriptEnabled"], out var gse) || gse;
        gsPathCfg = string.IsNullOrWhiteSpace(config["Tools:GhostscriptPath"]) ? "gs" : config["Tools:GhostscriptPath"]!;
        this.logger = logger;

        logger.LogInformation(
            "EBookMetadataUpdater initialized. SidecarEnabled={SidecarEnabled}, GhostscriptEnabled={GhostscriptEnabled}, GhostscriptPathSetting={GhostscriptPath} (ebook-meta primary)",
            sidecarEnabled,
            gsEnabled,
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

        // Ghostscript repair is PDF-specific
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

    public Task<DirectAttemptResult> DirectAttemptAsync(MetadataRequest request, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromResult(new DirectAttemptResult(false, "Cancelled"));

        if (!TryCleanEBookMetadata(request.FilePath, logger, out var cleanErr))
        {
            logger.LogWarning("Initial metadata cleanup failed for {File}: {Error}", request.FilePath, cleanErr);
            return Task.FromResult(new DirectAttemptResult(false, cleanErr ?? "Cleanup failed"));
        }

        var ok = TryWriteMetadataWithCalibre(
            request.FilePath,
            request.Metadata,
            request.FallbackTitle,
            logger,
            out var err);

        return Task.FromResult(new DirectAttemptResult(ok, ok ? string.Empty : err ?? string.Empty));
    }

    public Task<RepairAttemptResult> RepairAttemptAsync(MetadataRequest request, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromResult(new RepairAttemptResult(false, "Cancelled", false));

        string? errors = null;
        var gsRan = false;
        var extension = Path.GetExtension(request.FilePath);
        var (workDir, outputPath) = MetadataTemp.Prepare("repair", extension);

        try
        {
            if (RunGhostscriptTransform(request.FilePath, outputPath, logger, gsPathCfg, GhostscriptTimeoutMs, out var gsErr))
            {
                gsRan = true;
            }
            else
            {
                errors = Combine(errors, gsErr);
                outputPath = request.FilePath; // fallback
            }

            if (ct.IsCancellationRequested)
                return Task.FromResult(new RepairAttemptResult(false, "Cancelled", gsRan));

            if (!TryCleanEBookMetadata(outputPath, logger, out var cleanErr))
            {
                errors = Combine(errors, cleanErr);
                logger.LogWarning("Repair path cleanup failed for {File}: {Error}", request.FilePath, cleanErr);
            }

            if (TryWriteMetadataWithCalibre(outputPath, request.Metadata, request.FallbackTitle, logger, out var metaErr))
            {
                if (outputPath != request.FilePath && File.Exists(outputPath))
                    File.Copy(outputPath, request.FilePath, overwrite: true);
                return Task.FromResult(new RepairAttemptResult(true, errors, gsRan));
            }

            errors = Combine(errors, metaErr);
        }
        catch (Exception ex)
        {
            errors = Combine(errors, ex.Message);
            logger.LogError(ex, "Repair attempt failed for {File}", request.FilePath);
        }
        finally
        {
            MetadataTemp.Cleanup(workDir);
        }

        return Task.FromResult(new RepairAttemptResult(false, errors, gsRan));
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

    public void WriteKavitaSeriesMetadata(string filePath, IDictionary<string, string> metadata, string fallbackTitle)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is null) return;

            var kavitaPath = Path.Combine(dir, "series.json");
            var title = GetFirst(metadata, fallbackTitle, "Title", "TitleRomaji", "TitleEnglish", "TitleNative");
            var altTitles = CollectAlternateTitles(metadata, title);
            var authors = SplitAuthors(metadata, "Authors");
            var genres = GetGenres(metadata);
            var tags = GetTags(metadata);
            var year = ExtractYear(metadata);
            var ageRating = InferAgeRating(metadata, genres, tags);
            var format = metadata.TryGetValue("Format", out var fmt) ? fmt : string.Empty;

            var obj = new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["LocalizedTitles"] = metadata.Where(d => new[] { "Title", "TitleRomaji", "TitleEnglish", "TitleNative" }.Contains(d.Key)),
                ["AlternativeTitles"] = altTitles,
                ["Summary"] = metadata.TryGetValue("Description", out var desc) ? desc : string.Empty,
                ["Publisher"] = metadata.TryGetValue("Publisher", out var pub) ? pub : string.Empty,
                ["ReleaseYear"] = year,
                ["Format"] = format,
                ["Genres"] = genres,
                ["Tags"] = tags,
                ["Language"] = metadata.TryGetValue("Language", out var lang) ? lang : string.Empty,
                ["AgeRating"] = ageRating,
                ["Authors"] = authors,
                ["Artists"] = new List<string>(),
                ["Translators"] = new List<string>(),
                ["Editors"] = new List<string>(),
                ["Characters"] = new List<string>(),
                ["Imprint"] = string.Empty,
                ["Source"] = metadata.TryGetValue("Source", out var src) ? src : string.Empty,
                ["SourceUrl"] = metadata.TryGetValue("SourceUrl", out var su) ? su : null
            };

            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(kavitaPath, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing Kavita series metadata for {File}", filePath);
        }
    }

    private static EBookFormat DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".epub" => EBookFormat.Epub,
            ".pdf" => EBookFormat.Pdf,
            _ => EBookFormat.Pdf // default
        };
    }
}
