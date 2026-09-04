using System.Diagnostics;

namespace BooksMetadataBaker.Services.Helpers;

public static class ProcessRunner
{
    public static async Task<(bool Ok, int ExitCode, string Stdout, string Stderr, string? Error)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        ILogger? logger,
        int? timeoutMs = null,
        CancellationToken ct = default,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        Process proc;
        try
        {
            proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex)
        {
            // A launch failure (missing executable, bad path, not runnable) used to
            // surface only as a generic upstream error — log it here with the tool name.
            logger?.LogError(ex, "Failed to launch {Tool} (args: {Args})", Path.GetFileName(fileName), SummarizeArgs(arguments));
            return (false, -1, string.Empty, string.Empty, $"failed to start {fileName}: {ex.Message}");
        }

        using (proc)
        {
            logger?.LogInformation("Running {Tool} (cwd: {Cwd}, args: {Args})",
                Path.GetFileName(fileName), workingDirectory ?? "(default)", SummarizeArgs(arguments));

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            try
            {
                var exited = proc.WaitForExitAsync(ct);
                if (timeoutMs is { } timeout)
                {
                    var completed = await Task.WhenAny(exited, Task.Delay(timeout, CancellationToken.None));
                    if (completed != exited)
                    {
                        KillTree(proc, logger);
                        logger?.LogWarning("{Tool} timed out after {Seconds}s and was killed", Path.GetFileName(fileName), timeout / 1000);
                        return (false, -1, string.Empty, string.Empty, $"{Path.GetFileName(fileName)} timeout {timeout / 1000}s");
                    }
                }
                await exited;
            }
            catch (OperationCanceledException)
            {
                KillTree(proc, logger);
                throw;
            }

            string stdout, stderr;
            try
            {
                stdout = await stdoutTask;
                stderr = await stderrTask;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to read output from {Tool}", Path.GetFileName(fileName));
                stdout = string.Empty;
                stderr = string.Empty;
            }

            if (proc.ExitCode != 0)
                logger?.LogWarning("{Tool} exited with code {ExitCode}: {Detail}",
                    Path.GetFileName(fileName), proc.ExitCode, SummarizeOutput(stderr, stdout));

            return (proc.ExitCode == 0, proc.ExitCode, stdout, stderr, null);
        }
    }

    public static void KillTree(Process proc, ILogger? logger)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
        }
        catch
        {
            logger?.LogWarning("Failed to kill process {ProcessId}", proc.Id);
        }
    }

    private static string SummarizeArgs(IReadOnlyList<string> arguments)
    {
        var s = string.Join(' ', arguments);
        return s.Length > 300 ? s[..300] + "…" : s;
    }

    private static string SummarizeOutput(string stderr, string stdout)
    {
        var s = (stderr + stdout).Trim();
        return s.Length == 0 ? "(no output)" : (s.Length > 500 ? s[..500] + "…" : s);
    }
}
