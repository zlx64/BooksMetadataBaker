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
    Task<(bool Success, string? Error)> DirectAttemptAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, CancellationToken ct);
    Task<(bool Success, string? Error, bool GhostscriptRan)> RepairAttemptAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, CancellationToken ct);
    Task<(bool Success, string? Error, bool GhostscriptRan)> ForceStripAttemptAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, CancellationToken ct);
    Task<IReadOnlyList<PdfMetadataAttemptResult>> RunPipelineAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, CancellationToken ct);
    void WriteSidecarSummary(string filePath, IDictionary<string, string> metadata, string fallbackTitle, bool success, string? errors, bool metadataApplied, bool ghostscriptRan);
}

public class PdfMetadataUpdater : IPdfMetadataUpdater
{
    private const int GhostscriptTimeoutMs = 120_000;
    private readonly bool sidecarEnabled;
    private readonly bool gsEnabled;
    private readonly string gsPathCfg;

    public PdfMetadataUpdater(IConfiguration config)
    {
        sidecarEnabled = bool.TryParse(config["Tools:SidecarMetadataEnabled"], out var sc) ? sc : true;
        gsEnabled = bool.TryParse(config["Tools:GhostscriptEnabled"], out var gse) ? gse : true;
        gsPathCfg = string.IsNullOrWhiteSpace(config["Tools:GhostscriptPath"]) ? "gs" : config["Tools:GhostscriptPath"]!;
    }

    public async Task<IReadOnlyList<PdfMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        var attempts = new List<PdfMetadataAttemptResult>(3);

        // Direct
        var (dirOk, dirErr) = await DirectAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.Direct, dirOk, dirErr, false, dirOk));
        if (ct.IsCancellationRequested || dirOk) return attempts;

        // Repair
        var (repOk, repErr, repGs) = await RepairAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.Repair, repOk, repErr, repGs, repOk));
        if (ct.IsCancellationRequested || repOk) return attempts;

        // Force strip
        var (fsOk, fsErr, fsGs) = await ForceStripAttemptAsync(filePath, metadata, fallbackTitle, ct);
        attempts.Add(new(filePath, PdfMetadataAttemptStage.ForceStrip, fsOk, fsErr, fsGs, fsOk));
        return attempts;
    }

    public Task<(bool Success, string? Error)> DirectAttemptAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.FromResult((false, "Cancelled"));
        var ok = TryWriteMetadataInPlace(filePath, metadata, fallbackTitle, out var err);
        return Task.FromResult((ok, ok ? null : err));
    }

    public Task<(bool Success, string? Error, bool GhostscriptRan)> RepairAttemptAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !gsEnabled)
            return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, ct.IsCancellationRequested ? "Cancelled" : "Ghostscript disabled", false));

        string? errors = null;
        bool gsRan = false;
        var (workDir, repaired) = PrepareTemp("repair");
        string orig = filePath;
        try
        {
            if (RunGhostscriptTransform(orig, repaired, out var gsErr))
                gsRan = true;
            else
            {
                errors = Combine(errors, gsErr);
                repaired = orig;
            }

            if (ct.IsCancellationRequested)
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, "Cancelled", gsRan));

            if (TryWriteMetadataInPlace(repaired, metadata, fallbackTitle, out var metaErr))
            {
                if (repaired != orig && File.Exists(repaired)) File.Copy(repaired, orig, true);
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((true, null, gsRan));
            }
            errors = Combine(errors, metaErr);
        }
        catch (Exception ex)
        {
            errors = Combine(errors, ex.Message);
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
            return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, ct.IsCancellationRequested ? "Cancelled" : "Ghostscript disabled", false));

        string? errors = null;
        bool gsRan = false;
        var (workDir, stripped) = PrepareTemp("forcestrip");
        string orig = filePath;
        try
        {
            if (RunGhostscriptTransform(orig, stripped, out var gsErr))
                gsRan = true;
            else
            {
                errors = Combine(errors, gsErr);
                stripped = orig;
            }

            if (ct.IsCancellationRequested)
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((false, "Cancelled", gsRan));

            if (TryWriteMetadataInPlace(stripped, metadata, fallbackTitle, out var metaErr))
            {
                if (stripped != orig && File.Exists(stripped)) File.Copy(stripped, orig, true);
                return Task.FromResult<(bool Success, string? Error, bool GhostscriptRan)>((true, null, gsRan));
            }
            errors = Combine(errors, metaErr);
        }
        catch (Exception ex)
        {
            errors = Combine(errors, ex.Message);
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
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
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
            StripInfo(doc);
            ApplyMetadata(doc, metadata, fallbackTitle);
            doc.Save(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
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
        if (!proc.WaitForExit(GhostscriptTimeoutMs)) { TryKill(proc); err = $"ghostscript timeout {GhostscriptTimeoutMs / 1000}s"; return false; }

        var stderr = proc.StandardError.ReadToEnd();
        var stdout = proc.StandardOutput.ReadToEnd();
        if (proc.ExitCode != 0)
        {
            err = string.IsNullOrWhiteSpace(stderr + stdout) ? $"gs exit {proc.ExitCode}" : stderr + stdout;
            return false;
        }
        if (!File.Exists(output) || new FileInfo(output).Length == 0) { err = "ghostscript produced empty output"; return false; }
        return true;
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(); } catch { }
    }

    private void StripInfo(PdfDocument doc)
    {
        try
        {
            var keys = doc.Info.Elements.Keys.ToList();
            foreach (var k in keys) doc.Info.Elements.Remove(k);
        }
        catch { }
    }

    private void ApplyMetadata(
        PdfDocument doc,
        IDictionary<string, string> metadata,
        string fallbackTitle)
    {
        doc.Info.Title = GetFirst(metadata, fallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative") ?? fallbackTitle;
        doc.Info.Author = metadata.TryGetValue("Authors", out var a) ? a : string.Empty;
        doc.Info.Subject = metadata.TryGetValue("Description", out var d) ? Truncate(d, 200) : string.Empty;

        var kws = new List<string>();
        if (metadata.TryGetValue("Source", out var s) && !string.IsNullOrWhiteSpace(s)) kws.Add(s);
        if (metadata.TryGetValue("Format", out var f) && !string.IsNullOrWhiteSpace(f)) kws.Add(f);
        if (metadata.TryGetValue("PublishedDate", out var pd) && !string.IsNullOrWhiteSpace(pd)) kws.Add(pd);
        if (metadata.TryGetValue("SourceUrl", out var url) && !string.IsNullOrWhiteSpace(url)) kws.Add(url);

        doc.Info.Keywords = kws.Count > 0 ? string.Join(", ", kws) : string.Empty;
        doc.Info.Creator = "PrepKavitaPdf";
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
                ["AppliedTitle"] = GetFirst(metadata, fallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative") ?? fallbackTitle,
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
        catch { }
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

    private static void SafeDelete(string p)
    {
        try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) File.Delete(p); } catch { }
    }

    private static void TryDeleteDir(string d)
    {
        try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
    }

    private static string Combine(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b ?? string.Empty;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return a + "; " + b;
    }
}
