namespace BooksMetadataBaker.Services.Helpers;

public static class PdfGhostscript
{
    private static readonly string[] GhostscriptFallbackNames = ["gs", "gswin64c.exe", "gswin32c.exe"];

    public static string? ResolveGhostscript(string? configuredPath) =>
        ToolResolver.Resolve(configuredPath, GhostscriptFallbackNames);

    public static async Task<(bool Ok, string? Error)> RunGhostscriptTransformAsync(
        string input,
        string output,
        ILogger logger,
        string gsPathCfg,
        int timeoutMs,
        CancellationToken ct)
    {
        var gsPath = ResolveGhostscript(gsPathCfg);
        if (gsPath is null)
            return (false, "ghostscript not found");

        var args = new[]
        {
            "-dNOPAUSE",
            "-dBATCH",
            "-dSAFER",
            "-sDEVICE=pdfwrite",
            "-dCompatibilityLevel=1.7",
            "-dDetectDuplicateImages=true",
            "-dCompressFonts=true",
            "-dPDFSETTINGS=/prepress",
            $"-sOutputFile={output}",
            input
        };

        var (ok, _, stdout, stderr, runErr) = await ProcessRunner.RunAsync(gsPath, args, logger, timeoutMs, ct);
        if (runErr != null)
            return (false, runErr);
        if (!ok)
            return (false, string.IsNullOrWhiteSpace(stderr + stdout) ? "ghostscript failed" : (stderr + stdout).Trim());
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            return (false, "ghostscript produced empty output");
        return (true, null);
    }
}
