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
        {
            logger.LogWarning("Ghostscript not found (configured: {Path}); PDF repair will be unavailable", gsPathCfg);
            return (false, "ghostscript not found");
        }

        logger.LogInformation("Ghostscript repair: {Input} -> {Output}", input, output);

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

        var (ok, exitCode, stdout, stderr, runErr) = await ProcessRunner.RunAsync(gsPath, args, logger, timeoutMs, ct);
        if (runErr != null)
        {
            logger.LogError("Ghostscript did not complete for {Input}: {Error}", input, runErr);
            return (false, runErr);
        }
        if (!ok)
        {
            logger.LogWarning("Ghostscript exited non-zero ({ExitCode}) for {Input}; repair skipped", exitCode, input);
            return (false, string.IsNullOrWhiteSpace(stderr + stdout) ? "ghostscript failed" : (stderr + stdout).Trim());
        }
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            logger.LogWarning("Ghostscript produced empty output for {Input}", input);
            return (false, "ghostscript produced empty output");
        }
        logger.LogInformation("Ghostscript repair succeeded for {Input}", input);
        return (true, null);
    }
}
