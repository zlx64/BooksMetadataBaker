namespace BooksMetadataBaker.Services.Helpers;

public static class ToolResolver
{
    public static string? Resolve(string? configuredPath, IReadOnlyList<string> fallbackNames)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) &&
            (configuredPath.Contains(Path.DirectorySeparatorChar) ||
             configuredPath.Contains('/') ||
             configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            return File.Exists(configuredPath) ? configuredPath : null;
        }

        foreach (var name in fallbackNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var found = Which(name);
            if (found != null) return found;
        }
        return null;
    }

    public static string? Which(string cmd)
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
