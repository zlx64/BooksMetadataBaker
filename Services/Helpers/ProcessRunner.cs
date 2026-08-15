using System.Diagnostics;

namespace BooksMetadataBaker.Services.Helpers;

public static class ProcessRunner
{
    public static async Task<(bool Ok, int ExitCode, string Stdout, string Stderr, string? Error)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        ILogger? logger,
        int? timeoutMs = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi);
        if (proc is null)
            return (false, -1, string.Empty, string.Empty, $"failed to start {fileName}");

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
        catch
        {
            stdout = string.Empty;
            stderr = string.Empty;
        }

        return (proc.ExitCode == 0, proc.ExitCode, stdout, stderr, null);
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
}
