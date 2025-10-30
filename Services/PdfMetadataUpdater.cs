using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using System.Diagnostics;
using System.Text.Json;

namespace PrepKavitaPdf.Services;

public enum PdfMetadataAttemptStage { Direct, Repair, ForceStrip }

public sealed record PdfMetadataAttemptResult(
    string FilePath,
    PdfMetadataAttemptStage Stage,
    bool Success,
    string? ErrorMessage,
    bool GhostscriptRan,
    bool MetadataApplied);

public interface IPdfMetadataUpdater
{
    Task<IReadOnlyList<PdfMetadataAttemptResult>> RunPipelineAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, CancellationToken ct);
    void WriteSidecarSummary(string filePath, IDictionary<string, string> metadata, string fallbackTitle, bool success, string? errors, bool metadataApplied, bool ghostscriptRan);
}

public class PdfMetadataUpdater : IPdfMetadataUpdater
{
    private const int GhostscriptTimeoutMs = 120_000;
    private readonly bool sidecarEnabled;
    private readonly bool gsEnabled;
    private readonly string gsPathCfg;
    private readonly ILogger<PdfMetadataUpdater> logger;

    public PdfMetadataUpdater(IConfiguration config, ILogger<PdfMetadataUpdater> logger)
    {
        sidecarEnabled = !bool.TryParse(config["Tools:SidecarMetadataEnabled"], out var sc) || sc;
        gsEnabled = !bool.TryParse(config["Tools:GhostscriptEnabled"], out var gse) || gse;
        gsPathCfg = string.IsNullOrWhiteSpace(config["Tools:GhostscriptPath"]) ? "gs" : config["Tools:GhostscriptPath"]!;
        this.logger = logger;
        logger.LogInformation("PdfMetadataUpdater initialized. SidecarEnabled={SidecarEnabled}, GhostscriptEnabled={GhostscriptEnabled}, GhostscriptPathSetting={GhostscriptPath}", sidecarEnabled, gsEnabled, gsPathCfg);
    }

    public async Task<IReadOnlyList<PdfMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        logger.LogInformation("Starting metadata update pipeline for {File} with fallback title {FallbackTitle}. MetadataKeys={MetadataKeys}", filePath, fallbackTitle, string.Join(',', metadata.Keys));
        var attempts = new List<PdfMetadataAttemptResult>(3);

        // Direct
        var (dirOk, dirErr) = await DirectAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.Direct, dirOk, dirErr, false, dirOk));
        logger.LogInformation("Direct attempt for {File} success={Success} error={Error}", filePath, dirOk, dirErr);
        if (ct.IsCancellationRequested || dirOk)
        {
            if (ct.IsCancellationRequested) logger.LogWarning("Pipeline cancelled during/after direct attempt for {File}", filePath);
            return attempts;
        }

        // Repair
        var (repOk, repErr, repGs) = await RepairAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.Repair, repOk, repErr, repGs, repOk));
        logger.LogInformation("Repair attempt for {File} success={Success} gsRan={GhostscriptRan} error={Error}", filePath, repOk, repGs, repErr);
        if (ct.IsCancellationRequested || repOk)
        {
            if (ct.IsCancellationRequested) logger.LogWarning("Pipeline cancelled during/after repair attempt for {File}", filePath);
            return attempts;
        }

        // Force strip
        var (fsOk, fsErr, fsGs) = await ForceStripAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.ForceStrip, fsOk, fsErr, fsGs, fsOk));
        logger.LogInformation("Force-strip attempt for {File} success={Success} gsRan={GhostscriptRan} error={Error}", filePath, fsOk, fsGs, fsErr);
        return attempts;
    }

    public Task<(bool ok, string?)> DirectAttemptAsync(string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.FromResult((false, (string?)"Cancelled"));
        logger.LogDebug("DirectAttempt: writing metadata in-place for {File}", filePath);
        var ok = TryWriteMetadataInPlace(filePath, metadata, fallbackTitle, out var err);
        if (!ok && err != null) logger.LogWarning("DirectAttempt failed for {File}: {Error}", filePath, err);
        return Task.FromResult((ok, ok ? null : err));
    }

    public Task<(bool Success, string? Error, bool GhostscriptRan)> RepairAttemptAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !gsEnabled)
        {
            var reason = ct.IsCancellationRequested ? "Cancelled" : "Ghostscript disabled";
            logger.LogWarning("RepairAttempt skipped for {File}. Reason={Reason}", filePath, reason);
            return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, reason, false));
        }

        string? errors = null;
        var gsRan = false;
        var (workDir, repaired) = PrepareTemp("repair");
        var orig = filePath;
        logger.LogDebug("RepairAttempt: temp workdir {WorkDir} output {Repaired}", workDir, repaired);
        try
        {
            if (RunGhostscriptTransform(orig, repaired, out var gsErr))
            {
                gsRan = true;
                logger.LogInformation("RepairAttempt: Ghostscript transform succeeded for {File}", filePath);
            }
            else
            {
                errors = Combine(errors, gsErr);
                logger.LogWarning("RepairAttempt: Ghostscript failed for {File}. Error={Error}", filePath, gsErr);
                repaired = orig;
            }

            if (ct.IsCancellationRequested)
            {
                logger.LogWarning("RepairAttempt cancelled for {File}", filePath);
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, "Cancelled", gsRan));
            }

            if (TryWriteMetadataInPlace(repaired, metadata, fallbackTitle, out var metaErr))
            {
                if (repaired != orig && File.Exists(repaired)) File.Copy(repaired, orig, true);
                logger.LogInformation("RepairAttempt: metadata applied successfully for {File}", filePath);
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((true, null, gsRan));
            }
            errors = Combine(errors, metaErr);
            logger.LogWarning("RepairAttempt: metadata apply failed for {File}. Error={Error}", filePath, metaErr);
        }
        catch (Exception ex)
        {
            errors = Combine(errors, ex.Message);
            logger.LogError(ex, "RepairAttempt: unexpected error for {File}", filePath);
        }
        finally
        {
            CleanupTemp(workDir);
        }
        return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, errors, gsRan));
    }

    public Task<(bool Success, string? Error, bool GhostscriptRan)> ForceStripAttemptAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !gsEnabled)
        {
            var reason = ct.IsCancellationRequested ? "Cancelled" : "Ghostscript disabled";
            logger.LogWarning("ForceStripAttempt skipped for {File}. Reason={Reason}", filePath, reason);
            return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, reason, false));
        }

        string? errors = null;
        var gsRan = false;
        var (workDir, stripped) = PrepareTemp("forcestrip");
        var orig = filePath;
        logger.LogDebug("ForceStripAttempt: temp workdir {WorkDir} output {Stripped}", workDir, stripped);
        try
        {
            if (RunGhostscriptTransform(orig, stripped, out var gsErr))
            {
                gsRan = true;
                logger.LogInformation("ForceStripAttempt: Ghostscript transform succeeded for {File}", filePath);
            }
            else
            {
                errors = Combine(errors, gsErr);
                logger.LogWarning("ForceStripAttempt: Ghostscript failed for {File}. Error={Error}", filePath, gsErr);
                stripped = orig;
            }

            if (ct.IsCancellationRequested)
            {
                logger.LogWarning("ForceStripAttempt cancelled for {File}", filePath);
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, "Cancelled", gsRan));
            }

            if (TryWriteMetadataInPlace(stripped, metadata, fallbackTitle, out var metaErr))
            {
                if (stripped != orig && File.Exists(stripped)) File.Copy(stripped, orig, true);
                logger.LogInformation("ForceStripAttempt: metadata applied successfully for {File}", filePath);
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((true, null, gsRan));
            }
            errors = Combine(errors, metaErr);
            logger.LogWarning("ForceStripAttempt: metadata apply failed for {File}. Error={Error}", filePath, metaErr);
        }
        catch (Exception ex)
        {
            errors = Combine(errors, ex.Message);
            logger.LogError(ex, "ForceStripAttempt: unexpected error for {File}", filePath);
        }
        finally
        {
            CleanupTemp(workDir);
        }
        return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, errors, gsRan));
    }

    public void WriteSidecarSummary(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        bool success,
        string? errors,
        bool metadataApplied,
        bool ghostscriptRan) => WriteSidecar(filePath, metadata, fallbackTitle, success, errors, metadataApplied, ghostscriptRan);

    private static (string WorkDir, string RepairedPath) PrepareTemp(string label)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"pdf_meta_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var repaired = Path.Combine(workDir, Guid.NewGuid() + ".pdf");
        return (workDir, repaired);
    }

    private static void CleanupTemp(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { }
    }

    private bool TryWriteMetadataInPlace(
        string path,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        out string? error)
    {
        error = null;
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
            logger.LogDebug("Opened PDF for metadata write: {File}", path);
            StripInfo(doc);
            ApplyMetadata(doc, metadata, fallbackTitle);
            doc.Save(path);
            logger.LogInformation("Metadata saved to PDF {File}. TitleApplied={TitleApplied}", path, GetFirst(metadata, fallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative"));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(ex, "Failed writing metadata to {File}", path);
            return false;
        }
    }

    private bool RunGhostscriptTransform(string input, string output, out string? err)
    {
        err = null;
        var gsPath = ResolveGhostscript();
        if (gsPath == null) { err = "ghostscript not found"; logger.LogWarning("Ghostscript executable not found for transform. Input={Input}", input); return false; }

        var args = string.Join(' ', new[]
        {
            "-dNOPAUSE",
            "-dBATCH",
            "-dSAFER",
            "-sDEVICE=pdfwrite",
            "-dCompatibilityLevel=1.7",
            "-dDetectDuplicateImages=true",
            "-dCompressFonts=true",
            "-dPDFSETTINGS=/prepress",
            $"-sOutputFile={Escape(output)}",
            Escape(input)
        });

        logger.LogDebug("Starting Ghostscript for {Input} -> {Output}: {Args}", input, output, args);
        var psi = new ProcessStartInfo(gsPath, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) { err = "failed to start ghostscript"; logger.LogError("Ghostscript process failed to start for {Input}", input); return false; }
        if (!proc.WaitForExit(GhostscriptTimeoutMs)) { TryKill(proc); err = $"ghostscript timeout {GhostscriptTimeoutMs / 1000}s"; logger.LogWarning("Ghostscript timeout for {Input}. TimeoutMs={TimeoutMs}", input, GhostscriptTimeoutMs); return false; }

        var stderr = proc.StandardError.ReadToEnd();
        var stdout = proc.StandardOutput.ReadToEnd();
        if (proc.ExitCode != 0)
        {
            err = string.IsNullOrWhiteSpace(stderr + stdout) ? $"gs exit {proc.ExitCode}" : stderr + stdout;
            logger.LogWarning("Ghostscript non-zero exit code for {Input}. Code={ExitCode} Error={Error}", input, proc.ExitCode, err);
            return false;
        }

        if (File.Exists(output) && new FileInfo(output).Length != 0) { logger.LogDebug("Ghostscript produced output for {Input} size={SizeBytes}", input, new FileInfo(output).Length); return true; }
        err = "ghostscript produced empty output";
        logger.LogWarning("Ghostscript produced empty output for {Input}", input);
        return false;
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(); }
        catch { }
    }

    private static void StripInfo(PdfDocument doc)
    {
        try
        {
            var keys = doc.Info.Elements.Keys.ToList();
            foreach (var k in keys) doc.Info.Elements.Remove(k);
        }
        catch { }
    }

    private static void ApplyMetadata(
        PdfDocument doc,
        IDictionary<string, string> metadata,
        string fallbackTitle)
    {
        // Title (include subtitle if present and not already contained)
        var title = GetFirst(metadata, fallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative");
        if (metadata.TryGetValue("Subtitle", out var subtitle) && !string.IsNullOrWhiteSpace(subtitle) && !title.Contains(subtitle, StringComparison.OrdinalIgnoreCase))
        {
            title = $"{title}: {subtitle}";
        }
        doc.Info.Title = title;

        // Author fallbacks (Authors -> Publisher)
        if (metadata.TryGetValue("Authors", out var authors) && !string.IsNullOrWhiteSpace(authors))
            doc.Info.Author = authors;
        else if (metadata.TryGetValue("Publisher", out var publisher) && !string.IsNullOrWhiteSpace(publisher))
            doc.Info.Author = publisher;
        else
            doc.Info.Author = string.Empty;

        // Subject: prefer Description then Snippet
        if (metadata.TryGetValue("Description", out var desc) && !string.IsNullOrWhiteSpace(desc))
            doc.Info.Subject = Truncate(desc, 400);
        else if (metadata.TryGetValue("Snippet", out var snippet) && !string.IsNullOrWhiteSpace(snippet))
            doc.Info.Subject = Truncate(snippet, 400);
        else
            doc.Info.Subject = string.Empty;

        // Keywords compilation from expanded metadata
        doc.Info.Keywords = BuildKeywords(metadata);

        // Creator & Producer
        doc.Info.Creator = "PrepKavitaPdf";
        doc.Info.Elements.SetString("/Producer", "PrepKavitaPdf");
    }

    private static string BuildKeywords(IDictionary<string,string> meta)
    {
        var list = new List<string>();
        void Add(string? v) { if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim()); }
        void AddKV(string key)
        {
            if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) Add($"{key}:{v}");
        }

        // Core classification
        AddKV("Source");
        AddKV("Format");
        AddKV("Status");
        AddKV("Publisher");
        AddKV("Language");
        AddKV("AverageScore");
        AddKV("Volumes");
        AddKV("Chapters");
        AddKV("PageCount");
        AddKV("IssueCount");
        AddKV("ISBN13");
        AddKV("ISBN10");
        AddKV("StartDate");
        AddKV("EndDate");
        AddKV("PublishedDate");
        AddKV("StartYear");

        // Genre/category arrays
        if (meta.TryGetValue("Genres", out var genres)) foreach (var g in genres.Split(',', StringSplitOptions.RemoveEmptyEntries)) Add(g);
        if (meta.TryGetValue("Categories", out var cats)) foreach (var c in cats.Split(',', StringSplitOptions.RemoveEmptyEntries)) Add(c);

        // Source URL last (raw)
        AddKV("SourceUrl");
        AddKV("ApiDetailUrl");

        // De-duplicate & limit size
        var distinct = list.Select(s => s.Length > 120 ? s.Substring(0,120) : s).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var joined = string.Join(", ", distinct);
        return joined.Length <= 512 ? joined : joined.Substring(0, 512);
    }

    private void WriteSidecar(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        bool success,
        string? errors,
        bool metaApplied,
        bool gsRan)
    {
        if (!sidecarEnabled) { logger.LogDebug("Sidecar writing disabled for {File}", filePath); return; }
        try
        {
            var sidecar = filePath + ".meta.json";
            var obj = new Dictionary<string, object?>
            {
                ["AppliedTitle"] = GetFirst(metadata, fallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative"),
                ["Success"] = success,
                ["MetadataApplied"] = metaApplied,
                ["GhostscriptRan"] = gsRan,
                ["Errors"] = errors,
                ["TimestampUtc"] = DateTime.UtcNow
            };
            foreach (var kv in metadata) obj[kv.Key] = kv.Value;
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecar, json);
            logger.LogInformation("Wrote sidecar metadata file {Sidecar} for {File} success={Success}", sidecar, filePath, success);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing sidecar metadata for {File}", filePath);
        }
    }

    private string? ResolveGhostscript()
    {
        if (!string.IsNullOrWhiteSpace(gsPathCfg) && (gsPathCfg.Contains(Path.DirectorySeparatorChar) || gsPathCfg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            return File.Exists(gsPathCfg) ? gsPathCfg : null;
        var names = new[] { gsPathCfg, "gs", "gswin64c.exe", "gswin32c.exe" };
        foreach (var n in names)
        {
            var p = Which(n);
            if (p != null) return p;
        }
        return null;
    }

    private string? Which(string cmd)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir, cmd);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string GetFirst(IDictionary<string, string> dict, string fallback, params string[] keys)
    {
        foreach (var k in keys)
            if (dict.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return fallback;
    }

    private static string Truncate(string v, int m) => string.IsNullOrEmpty(v) ? v : (v.Length <= m ? v : v.Substring(0, m));
    private static string Escape(string p) => p.Contains(' ') ? "\"" + p + "\"" : p;

    private static string Combine(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b ?? string.Empty;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return a + "; " + b;
    }
}
