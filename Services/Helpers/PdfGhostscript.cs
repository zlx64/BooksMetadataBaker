using System.Diagnostics;

namespace BooksMetadataBaker.Services.Helpers;

public static class PdfGhostscript
{
    public static bool RunGhostscriptTransform(
        string input,
        string output,
        ILogger logger,
        string gsPathCfg,
        int timeoutMs,
        out string? err)
    {
        err = null;
        var gsPath = ResolveGhostscript(gsPathCfg);
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
        if (!proc.WaitForExit(timeoutMs))
        {
            TryKill(proc, logger);
            err = $"ghostscript timeout {timeoutMs / 1000}s";
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
        $"-sOutputFile={MetadataHelpers.Escape(output)}",
        MetadataHelpers.Escape(input)
    });

    private static void TryKill(Process p, ILogger logger)
    {
        try { p.Kill(); }
        catch { logger.LogWarning("Failed to kill process"); }
    }

    private static string? ResolveGhostscript(string gsPathCfg)
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
}
