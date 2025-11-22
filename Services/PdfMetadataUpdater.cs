using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace PrepKavitaPdf.Services;

public enum PdfMetadataAttemptStage
{
    Direct,
    Repair
}

public sealed record PdfMetadataAttemptResult(
    string FilePath,
    PdfMetadataAttemptStage Stage,
    bool Success,
    string? ErrorMessage,
    bool GhostscriptRan,
    bool MetadataApplied);

public interface IPdfMetadataUpdater
{
    Task<IReadOnlyList<PdfMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct);

    void WriteSidecarSummary(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        bool success,
        string? errors,
        bool metadataApplied,
        bool ghostscriptRan);

    void WriteKavitaSeriesMetadata(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle);
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
        logger.LogInformation(
            "PdfMetadataUpdater initialized. SidecarEnabled={SidecarEnabled}, GhostscriptEnabled={GhostscriptEnabled}, GhostscriptPathSetting={GhostscriptPath} (ebook-meta primary)",
            sidecarEnabled,
            gsEnabled,
            gsPathCfg);
    }

    public async Task<IReadOnlyList<PdfMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        var attempts = new List<PdfMetadataAttemptResult>(2);

        var (directOk, directErr) = await DirectAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.Direct, directOk, directErr, false, directOk));
        if (ct.IsCancellationRequested || directOk) return attempts;

        if (!gsEnabled)
        {
            logger.LogWarning("Skipping repair attempt for {File}: Ghostscript disabled", filePath);
            return attempts;
        }

        var (repairOk, repairErr, gsRan) = await RepairAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.Repair, repairOk, repairErr, gsRan, repairOk));
        return attempts;
    }

    public Task<(bool ok, string? error)> DirectAttemptAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.FromResult((false, "Cancelled"));
        var ok = TryWriteMetadataWithCalibre(filePath, metadata, fallbackTitle, out var err);
        return Task.FromResult((ok, ok ? null : err));
    }

    public Task<(bool Success, string? Error, bool GhostscriptRan)> RepairAttemptAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.FromResult<(bool, string?, bool)>((false, "Cancelled", false));

        string? errors = null;
        var gsRan = false;
        var (workDir, outputPath) = PrepareTemp("repair");

        try
        {
            if (RunGhostscriptTransform(filePath, outputPath, out var gsErr))
            {
                gsRan = true;
            }
            else
            {
                errors = Combine(errors, gsErr);
                outputPath = filePath; // fallback
            }

            if (ct.IsCancellationRequested)
                return Task.FromResult<(bool, string?, bool)>((false, "Cancelled", gsRan));

            if (TryWriteMetadataWithCalibre(outputPath, metadata, fallbackTitle, out var metaErr))
            {
                if (outputPath != filePath && File.Exists(outputPath))
                    File.Copy(outputPath, filePath, true);
                return Task.FromResult<(bool, string?, bool)>((true, null, gsRan));
            }
            errors = Combine(errors, metaErr);
        }
        catch (Exception ex)
        {
            errors = Combine(errors, ex.Message);
            logger.LogError(ex, "Repair attempt failed for {File}", filePath);
        }
        finally
        {
            CleanupTemp(workDir);
        }

        return Task.FromResult<(bool, string?, bool)>((false, errors, gsRan));
    }

    private bool TryWriteMetadataWithCalibre(
        string path,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        out string? error)
    {
        error = null;
        try
        {
            var args = BuildEbookMetaArgs(path, metadata, fallbackTitle);
            var psi = new ProcessStartInfo("ebook-meta", args)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) { error = "failed to start ebook-meta"; return false; }
            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();
            if (proc.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr + stdout) ? $"ebook-meta exit {proc.ExitCode}" : stderr + stdout;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(ex, "ebook-meta exception for {File}", path);
            return false;
        }
    }

    private static (string WorkDir, string OutputPath) PrepareTemp(string label)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"pdf_meta_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var outPath = Path.Combine(workDir, Guid.NewGuid().ToString("N") + ".pdf");
        return (workDir, outPath);
    }

    private static void CleanupTemp(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    private bool RunGhostscriptTransform(string input, string output, out string? err)
    {
        err = null;
        var gsPath = ResolveGhostscript();
        if (gsPath == null) { err = "ghostscript not found"; return false; }

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

        var psi = new ProcessStartInfo(gsPath, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) { err = "failed to start ghostscript"; return false; }
        if (!proc.WaitForExit(GhostscriptTimeoutMs)) { TryKill(proc); err = $"ghostscript timeout {GhostscriptTimeoutMs/1000}s"; return false; }

        var stderr = proc.StandardError.ReadToEnd();
        var stdout = proc.StandardOutput.ReadToEnd();
        if (proc.ExitCode != 0)
        {
            err = string.IsNullOrWhiteSpace(stderr + stdout) ? $"gs exit {proc.ExitCode}" : stderr + stdout;
            return false;
        }

        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            err = "ghostscript produced empty output";
            return false;
        }
        return true;
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(); } catch { }
    }

    private static string Escape(string p) => p.Contains(' ') ? '"' + p + '"' : p;

    private static string BuildEbookMetaArgs(
        string filePath,
        IDictionary<string, string> meta,
        string fallbackTitle)
    {
        string Q(string v) => '"' + v.Replace("\"", "\\\"") + '"';
        var parts = new List<string>();

        // Title: now always use original request (fallbackTitle) without any volume markers from data sources.
        var baseTitle = fallbackTitle;
        // Strip accidental volume markers if user passed them.
        baseTitle = System.Text.RegularExpressions.Regex.Replace(baseTitle, @"(?i)\bvol(?:ume)?\s*\d+(?:\.\d+)?", "").Trim();
        baseTitle = System.Text.RegularExpressions.Regex.Replace(baseTitle, @"\s+", " ").Trim();
        var title = baseTitle;
        if (meta.TryGetValue("Subtitle", out var subtitle) && !string.IsNullOrWhiteSpace(subtitle) && !title.Contains(subtitle, StringComparison.OrdinalIgnoreCase))
            title = title + ": " + subtitle.Trim();
        parts.Add("--title " + Q(title));

        // Series (keep if present; calibre:series will override dc:title for series context but we still keep pure chapter title above)
        var series = GetFirst(meta, string.Empty, "Series", "SeriesName", "calibre:series");
        if (!string.IsNullOrWhiteSpace(series)) parts.Add("--series " + Q(series));

        var idx = SeriesIndex(meta);
        if (idx != null) parts.Add("--index " + Q(idx.Value.ToString("0.##", CultureInfo.InvariantCulture)));
        var rating = GetFirst(meta, string.Empty, "AverageScore", "Rating", "UserRating");
        if (!string.IsNullOrWhiteSpace(rating)) parts.Add("--rating " + Q(rating));
        var desc = GetFirst(meta, string.Empty, "Description", "Snippet");
        if (!string.IsNullOrWhiteSpace(desc)) parts.Add("--comments " + Q(desc));
        var publisher = GetFirst(meta, string.Empty, "Publisher");
        if (!string.IsNullOrWhiteSpace(publisher)) parts.Add("--publisher " + Q(publisher));
        var dateRaw = GetFirst(meta, string.Empty, "PublishedDate", "StartDate", "StartYear");
        var dateIso = NormDate(dateRaw);
        if (!string.IsNullOrWhiteSpace(dateIso)) parts.Add("--date " + Q(dateIso));
        var authorsRaw = GetFirst(meta, string.Empty, "Authors", "Author", "Writer");
        if (!string.IsNullOrWhiteSpace(authorsRaw))
        {
            var authors = authorsRaw.Split(new[] { ',', ';', '|', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var joined = string.Join(" & ", authors);
            parts.Add("--authors " + Q(joined));
        }
        var tagList = new List<string>();
        void AddTags(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return;
            foreach (var t in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!string.IsNullOrWhiteSpace(t)) tagList.Add(t);
        }
        if (meta.TryGetValue("Genres", out var gVal)) AddTags(gVal);
        if (meta.TryGetValue("Categories", out var cVal)) AddTags(cVal);
        if (meta.TryGetValue("Format", out var fVal) && !string.IsNullOrWhiteSpace(fVal)) tagList.Add(fVal);
        if (meta.TryGetValue("Status", out var sVal) && !string.IsNullOrWhiteSpace(sVal)) tagList.Add(sVal);
        if (meta.TryGetValue("Language", out var lVal) && !string.IsNullOrWhiteSpace(lVal)) tagList.Add(lVal);
        tagList = tagList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tagList.Count > 0) parts.Add("--tags " + Q(string.Join(",", tagList)));
        var lang = GetFirst(meta, string.Empty, "Language");
        if (!string.IsNullOrWhiteSpace(lang))
            parts.Add("--language " + Q(lang.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? lang));
        var isbn = GetFirst(meta, string.Empty, "ISBN13", "ISBN10", "ISBN");
        if (!string.IsNullOrWhiteSpace(isbn)) parts.Add("--isbn " + Q(isbn));
        parts.Add(Q(filePath));
        return string.Join(' ', parts);
    }

    public void WriteSidecarSummary(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        bool success,
        string? errors,
        bool metadataApplied,
        bool ghostscriptRan) => WriteSidecar(filePath, metadata, fallbackTitle, success, errors, metadataApplied, ghostscriptRan);

    private static double? SeriesIndex(IDictionary<string, string> m)
    {
        foreach (var key in new[] { "Volume", "VolumeNumber", "SeriesIndex", "Issue", "IssueNumber", "Chapter", "calibreSI:series_index" })
        {
            if (m.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                if (double.TryParse(v.Replace('#', ' ').Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
                var num = new string(v.Where(c => char.IsDigit(c) || c == '.').ToArray());
                if (double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
            }
        }
        return null;
    }

    private static string NormDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (DateTime.TryParse(raw, out var dt)) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length >= 8) return digits.Substring(0, 4) + "-" + digits.Substring(4, 2) + "-" + digits.Substring(6, 2);
        if (digits.Length >= 6) return digits.Substring(0, 4) + "-" + digits.Substring(4, 2) + "-01";
        if (digits.Length >= 4) return digits.Substring(0, 4);
        return raw;
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
        if (!sidecarEnabled) return;
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing sidecar metadata for {File}", filePath);
        }
    }

    public void WriteKavitaSeriesMetadata(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is null) return;
            var kavitaPath = Path.Combine(dir, "series.json");

            var title = GetFirst(metadata, fallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative");
            var altTitles = new List<string>();
            void AddAlt(string key)
            {
                if (metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && !v.Equals(title, StringComparison.OrdinalIgnoreCase) && !altTitles.Contains(v))
                    altTitles.Add(v);
            }
            AddAlt("TitleEnglish");
            AddAlt("TitleRomaji");
            AddAlt("TitleNative");

            var authors = metadata.TryGetValue("Authors", out var a)
                ? a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : new List<string>();

            var genres = new List<string>();
            if (metadata.TryGetValue("Genres", out var g)) genres.AddRange(g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (metadata.TryGetValue("Categories", out var c)) genres.AddRange(c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            genres = genres.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var tags = new List<string>();
            void AddTag(string key)
            {
                if (metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) tags.Add(v);
            }
            AddTag("Format");
            AddTag("Status");
            AddTag("Source");
            AddTag("Language");

            var year = ExtractYear(metadata);
            var ageRating = InferAgeRating(metadata, genres, tags);
            var format = metadata.TryGetValue("Format", out var fmt) ? fmt : string.Empty;

            var obj = new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["LocalizedTitles"] = new List<string>(),
                ["AlternativeTitles"] = altTitles,
                ["Summary"] = metadata.TryGetValue("Description", out var desc) ? desc : string.Empty,
                ["Publisher"] = metadata.TryGetValue("Publisher", out var pub) ? pub : string.Empty,
                ["ReleaseYear"] = year,
                ["Format"] = format,
                ["Genres"] = genres,
                ["Tags"] = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
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

    private static int InferAgeRating(
        IDictionary<string, string> meta,
        IEnumerable<string> genres,
        IEnumerable<string> tags)
    {
        var tokens = new List<string>();

        void Collect(string? v)
        {
            if (!string.IsNullOrWhiteSpace(v))
                tokens.AddRange(v.Split(new[] { ' ', ',', ';', '.', '/', '\\', '|' }, StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var g in genres) Collect(g);
        foreach (var t in tags) Collect(t);
        if (meta.TryGetValue("Description", out var desc)) Collect(desc);

        var lowered = tokens.Select(x => x.ToLowerInvariant()).ToList();

        string[] adult = { "adult", "hentai", "mature", "18", "erotic", "nsfw", "porn", "smut" };
        if (lowered.Any(l => adult.Contains(l))) return 18;

        string[] teenPlus = { "seinen", "violence", "gore", "horror", "dark" };
        if (lowered.Any(l => teenPlus.Contains(l))) return 16;

        string[] teen = { "shounen", "romance", "ya", "teen" };
        if (lowered.Any(l => teen.Contains(l))) return 13;

        return 0;
    }

    private static int ExtractYear(IDictionary<string, string> meta)
    {
        string? pick = null;
        foreach (var key in new[] { "PublishedDate", "StartDate", "StartYear", "EndDate" })
        {
            if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                pick = v;
                break;
            }
        }
        if (pick == null) return 0;

        foreach (var part in pick.Split('-', ' ', '/', '.'))
        {
            if (part.Length == 4 && int.TryParse(part, out var yr) && yr > 0) return yr;
        }

        var digits = new string(pick.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4 && int.TryParse(digits.Substring(0, 4), out var y2)) return y2;
        return 0;
    }

    private string? ResolveGhostscript()
    {
        if (!string.IsNullOrWhiteSpace(gsPathCfg) &&
            (gsPathCfg.Contains(Path.DirectorySeparatorChar) || gsPathCfg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            return File.Exists(gsPathCfg) ? gsPathCfg : null;
        }

        var names = new[] { gsPathCfg, "gs", "gswin64c.exe", "gswin32c.exe" };
        foreach (var n in names)
        {
            var p = Which(n);
            if (p != null) return p;
        }
        return null;
    }

    private static string? Which(string cmd)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir, cmd);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string GetFirst(
        IDictionary<string, string> dict,
        string fallback,
        params string[] keys)
    {
        foreach (var k in keys)
        {
            if (dict.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        }
        return fallback;
    }

    private static string Combine(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b ?? string.Empty;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return a + "; " + b;
    }
}
