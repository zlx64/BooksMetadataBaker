using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Services;

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
        var request = new MetadataRequest(filePath, metadata, fallbackTitle);

        var direct = await DirectAttemptAsync(request, ct);
        attempts.Add(new PdfMetadataAttemptResult(
            filePath,
            PdfMetadataAttemptStage.Direct,
            direct.Success,
            direct.ErrorMessage,
            false,
            direct.Success));

        if (ct.IsCancellationRequested || direct.Success)
            return attempts;

        if (!gsEnabled)
        {
            logger.LogWarning("Skipping repair attempt for {File}: Ghostscript disabled", filePath);
            return attempts;
        }

        var repair = await RepairAttemptAsync(request, ct);
        attempts.Add(new PdfMetadataAttemptResult(
            filePath,
            PdfMetadataAttemptStage.Repair,
            repair.Success,
            repair.ErrorMessage,
            repair.GhostscriptRan,
            repair.Success));

        return attempts;
    }

    public Task<DirectAttemptResult> DirectAttemptAsync(
        MetadataRequest request,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromResult(new DirectAttemptResult(false, "Cancelled"));

        // Step 1: Attempt metadata cleanup prior to applying new metadata.
        var cleanedOk = TryCleanPdfMetadata(request.FilePath, out var cleanErr);
        if (!cleanedOk)
        {
            logger.LogWarning("Initial metadata cleanup failed for {File}: {Error}", request.FilePath, cleanErr);
            return Task.FromResult(new DirectAttemptResult(false, cleanErr ?? "Cleanup failed"));
        }

        // Step 2: Apply fresh metadata.
        var ok = TryWriteMetadataWithCalibre(
            request.FilePath,
            request.Metadata,
            request.FallbackTitle,
            out var err);

        var msg = ok ? string.Empty : err ?? string.Empty;
        return Task.FromResult(new DirectAttemptResult(ok, msg));
    }

    public Task<RepairAttemptResult> RepairAttemptAsync(
        MetadataRequest request,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromResult(new RepairAttemptResult(false, "Cancelled", false));

        string? errors = null;
        var gsRan = false;
        var (workDir, outputPath) = PrepareTemp("repair");

        try
        {
            if (RunGhostscriptTransform(request.FilePath, outputPath, out var gsErr))
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

            // Attempt cleanup again (on transformed file if available) before applying metadata.
            if (!TryCleanPdfMetadata(outputPath, out var cleanErr))
            {
                errors = Combine(errors, cleanErr);
                logger.LogWarning("Repair path cleanup failed for {File}: {Error}", request.FilePath, cleanErr);
            }

            if (TryWriteMetadataWithCalibre(outputPath, request.Metadata, request.FallbackTitle, out var metaErr))
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
            CleanupTemp(workDir);
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
        bool ghostscriptRan) => WriteSidecar(new SidecarSummary(
            filePath,
            metadata,
            fallbackTitle,
            success,
            errors,
            metadataApplied,
            ghostscriptRan));

    public void WriteKavitaSeriesMetadata(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is null)
                return;

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
                ["LocalizedTitles"] = metadata.Where(d =>
                    new[] { "Title", "TitleRomaji", "TitleEnglish", "TitleNative" }
                        .Contains(d.Key)
                ),
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

    private bool TryCleanPdfMetadata(string path, out string? error)
    {
        error = null;
        try
        {
            var args = BuildEbookMetaCleanArgs(path);
            var psi = new ProcessStartInfo("ebook-meta", args)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                error = "failed to start ebook-meta (cleanup)";
                return false;
            }
            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();
            if (proc.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr + stdout) ? $"ebook-meta cleanup exit {proc.ExitCode}" : stderr + stdout;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogWarning(ex, "Cleanup exception for {File}", path);
            return false;
        }
    }

    private string BuildEbookMetaCleanArgs(string filePath)
    {
        // Provide blank values for common metadata fields to strip existing info.
        string Q(string v) => '"' + v.Replace("\"", "\\\"") + '"';
        var fields = new[]
        {
            "--title", "--authors", "--comments", "--tags", "--series", "--publisher", "--isbn", "--language"
        };
        var parts = new List<string>();
        foreach (var f in fields) parts.Add(f + " " + Q(string.Empty));
        parts.Add(Q(filePath));
        return string.Join(' ', parts);
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
            if (proc == null)
            {
                error = "failed to start ebook-meta";
                return false;
            }

            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();

            if (proc.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr + stdout)
                    ? $"ebook-meta exit {proc.ExitCode}"
                    : stderr + stdout;
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

    private bool RunGhostscriptTransform(string input, string output, out string? err)
    {
        err = null;
        var gsPath = ResolveGhostscript();
        if (gsPath == null)
        {
            err = "ghostscript not found";
            return false;
        }

        var args = BuildGhostscriptArgs(input, output);
        var psi = new ProcessStartInfo(gsPath, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            err = "failed to start ghostscript";
            return false;
        }

        if (!proc.WaitForExit(GhostscriptTimeoutMs))
        {
            TryKill(proc);
            err = $"ghostscript timeout {GhostscriptTimeoutMs / 1000}s";
            return false;
        }

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

    private static string BuildGhostscriptArgs(string input, string output) => string.Join(' ', new[]
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

    private void TryKill(Process p)
    {
        try
        {
            p.Kill();
        }
        catch
        {
            logger.LogWarning("Failed to kill process");
        }
    }

    private static string Escape(string p) => p.Contains(' ') ? '"' + p + '"' : p;

    private static (string WorkDir, string OutputPath) PrepareTemp(string label)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"pdf_meta_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var outPath = Path.Combine(workDir, Guid.NewGuid().ToString("N") + ".pdf");
        return (workDir, outPath);
    }

    private static void CleanupTemp(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    private static string BuildEbookMetaArgs(
        string filePath,
        IDictionary<string, string> meta,
        string fallbackTitle)
    {
        string Q(string v) => '"' + v.Replace("\"", "\\\"") + '"';
        var parts = new List<string>();

        var title = GetFirst(meta, fallbackTitle, "TitleEnglish", "TitleRomaji", "Title", "TitleNative");

        var newTitle = Path.GetFileNameWithoutExtension(filePath);

        parts.Add("--title " + Q(newTitle ?? title));
        parts.Add("--series " + Q(fallbackTitle));

        var idx = ParseVolumeNumber(newTitle);
        if (idx != null)
            parts.Add("--index " + Q(idx.Value.ToString(CultureInfo.InvariantCulture)));

        var rating = GetFirst(meta, string.Empty, "AverageScore", "Rating", "UserRating");
        if (!string.IsNullOrWhiteSpace(rating))
            parts.Add("--rating " + Q(rating));

        var desc = GetFirst(meta, string.Empty, "Description", "Snippet");
        if (!string.IsNullOrWhiteSpace(desc))
            parts.Add("--comments " + Q(desc));

        var publisher = GetFirst(meta, string.Empty, "Publisher");
        if (!string.IsNullOrWhiteSpace(publisher))
            parts.Add("--publisher " + Q(publisher));

        var dateRaw = GetFirst(meta, string.Empty, "PublishedDate", "StartDate", "StartYear");
        var dateIso = NormDate(dateRaw);
        if (!string.IsNullOrWhiteSpace(dateIso))
            parts.Add("--date " + Q(dateIso));

        var authorsRaw = GetFirst(meta, string.Empty, "Authors", "Author", "Writer");
        if (!string.IsNullOrWhiteSpace(authorsRaw))
        {
            var authors = authorsRaw.Split(new[] { ',', ';', '|', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            parts.Add("--authors " + Q(string.Join(" & ", authors)));
        }

        var tagList = GetTags(meta);
        if (tagList.Count > 0)
            parts.Add("--tags " + Q(string.Join(',', tagList)));

        var lang = GetFirst(meta, string.Empty, "Language");
        if (!string.IsNullOrWhiteSpace(lang))
        {
            parts.Add("--language " + Q(
                lang.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? lang));
        }

        parts.Add(Q(filePath));
        return string.Join(' ', parts);
    }

    private static string NormDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        if (DateTime.TryParse(raw, out var dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length >= 8)
            return digits[..4] + "-" + digits.Substring(4, 2) + "-" + digits.Substring(6, 2);
        if (digits.Length >= 6)
            return digits[..4] + "-" + digits.Substring(4, 2) + "-01";
        if (digits.Length >= 4)
            return digits[..4];
        return raw;
    }
    private static double? ParseVolumeNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var m = Regex.Match(title, @"(?:^|[\s._-])(?:vol(?:ume)?|v|issue|ch(?:apter)?|part)\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;

        var trailing = Regex.Match(title, @"(\d+(?:\.\d+)?)\s*$");
        if (trailing.Success && double.TryParse(trailing.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            return d;

        return null;
    }

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

    private void WriteSidecar(SidecarSummary summary)
    {
        if (!sidecarEnabled)
            return;
        try
        {
            var sidecar = summary.FilePath + ".meta.json";
            var obj = new Dictionary<string, object?>
            {
                ["AppliedTitle"] = GetFirst(summary.Metadata, summary.FallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative"),
                ["Success"] = summary.Success,
                ["MetadataApplied"] = summary.MetadataApplied,
                ["GhostscriptRan"] = summary.GhostscriptRan,
                ["Errors"] = summary.Errors,
                ["TimestampUtc"] = DateTime.UtcNow
            };
            foreach (var kv in summary.Metadata)
                obj[kv.Key] = kv.Value;
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecar, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing sidecar metadata for {File}", summary.FilePath);
        }
    }

    private static List<string> CollectAlternateTitles(IDictionary<string, string> meta, string mainTitle)
    {
        var list = new List<string>();
        void Add(string key)
        {
            if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && !v.Equals(mainTitle, StringComparison.OrdinalIgnoreCase) && !list.Contains(v))
                list.Add(v);
        }
        Add("TitleEnglish");
        Add("TitleRomaji");
        Add("TitleNative");
        return list;
    }

    private static List<string> SplitAuthors(IDictionary<string, string> meta, string key)
    {
        if (!meta.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return new List<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static List<string> GetGenres(IDictionary<string, string> meta)
    {
        var genres = new List<string>();
        void AddCsv(string key)
        {
            if (!meta.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return;
            genres.AddRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        AddCsv("Genres");
        AddCsv("Categories");
        return genres.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetTags(IDictionary<string, string> meta)
    {
        var tags = new List<string>();
        void Add(string key)
        {
            if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                tags.Add(v);
        }
        Add("Format");
        Add("Status");
        Add("Source");
        Add("Language");
        if (meta.TryGetValue("Genres", out var g)) tags.AddRange(g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (meta.TryGetValue("Categories", out var c)) tags.AddRange(c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        if (digits.Length >= 4 && int.TryParse(digits[..4], out var y2)) return y2;
        return 0;
    }

    private string? ResolveGhostscript()
    {
        if (!string.IsNullOrWhiteSpace(gsPathCfg) &&
            (gsPathCfg.Contains(Path.DirectorySeparatorChar) ||
             gsPathCfg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
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
