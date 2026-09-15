using System.IO.Compression;

namespace BooksMetadataBaker.Services.Comic;

public static class ImageFolderArchiver
{
    public static bool HasDirectImageFiles(string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.') || !ArchiveComicInfoWriter.IsImageFile(name))
                continue;
            return true;
        }
        return false;
    }

    public static List<string> GetImageFiles(string folder)
    {
        var result = new List<string>();
        if (!Directory.Exists(folder))
            return result;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (IsHiddenPath(rel) || !ArchiveComicInfoWriter.IsImageFile(rel))
                continue;
            result.Add(rel);
        }

        result.Sort(NaturalStringComparer.Instance);
        return result;
    }

    public static async Task<int> CreateCbzFromDirectoryAsync(
        string sourceDir,
        string cbzPath,
        IReadOnlyList<string>? relativeImagePaths = null,
        CancellationToken ct = default)
    {
        var files = (relativeImagePaths is null ? GetImageFiles(sourceDir) : relativeImagePaths)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizeRelativePath)
            .Where(p => p.Length > 0 && !IsHiddenPath(p) && ArchiveComicInfoWriter.IsImageFile(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        files.Sort(NaturalStringComparer.Instance);

        var resolved = new List<(string Name, string Full)>(files.Count);
        foreach (var rel in files)
        {
            if (!TryResolveRelative(sourceDir, rel, out var full))
                throw new InvalidOperationException($"Invalid image path: {rel}");
            resolved.Add((rel, full));
        }

        resolved = resolved.Where(x => File.Exists(x.Full)).ToList();
        if (resolved.Count == 0)
            return 0;

        var destFull = Path.GetFullPath(cbzPath);
        var destDir = Path.GetDirectoryName(destFull)!;
        Directory.CreateDirectory(destDir);
        var tmpPath = Path.Combine(destDir, Path.GetFileName(destFull) + ".tmp-" + Guid.NewGuid().ToString("N"));

        try
        {
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            await using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, full) in resolved)
                {
                    var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                    await using var inStream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                    await using var outStream = await entry.OpenAsync(ct);
                    await inStream.CopyToAsync(outStream, ct);
                }
            }

            File.Move(tmpPath, destFull, overwrite: true);
            return resolved.Count;
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmpPath);
            throw;
        }
        catch
        {
            TryDelete(tmpPath);
            throw;
        }
    }

    public static void DeleteSourceFiles(string sourceDir, IEnumerable<string> relativePaths, string? protectedPath)
    {
        var sourceFull = Path.GetFullPath(sourceDir);
        var protectedFull = protectedPath is null ? null : Path.GetFullPath(protectedPath);

        foreach (var rel in relativePaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rel))
                continue;
            if (!TryResolveRelative(sourceFull, rel.Replace('\\', '/'), out var full))
                continue;

            if (File.Exists(full) && !SamePath(full, protectedFull))
            {
                try
                {
                    File.Delete(full);
                }
                catch
                {
                    // Ignore individual cleanup failures.
                }
            }

            TryRemoveEmptyAncestors(full, sourceFull, protectedFull);
        }
    }

    public static bool TryResolveRelative(string baseDir, string relative, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relative))
            return false;
        if (relative.Contains('\0'))
            return false;

        var normalized = relative.Replace('\\', '/').Trim();
        if (normalized.Length == 0 || Path.IsPathRooted(normalized))
            return false;

        normalized = normalized.TrimStart('/');
        if (normalized.Length == 0)
            return false;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
            return false;

        var baseFull = Path.GetFullPath(baseDir);
        fullPath = Path.GetFullPath(Path.Combine(baseFull, normalized));
        var comparison = PathComparison();
        if (string.Equals(fullPath, baseFull, comparison))
            return false;

        var prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, comparison))
            return false;

        return true;
    }

    private static void TryRemoveEmptyAncestors(string filePath, string sourceFull, string? protectedFull)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir) && !SamePath(dir, sourceFull))
        {
            if (IsProtectedDirectory(dir, protectedFull))
                break;
            if (!Directory.Exists(dir))
                break;

            bool empty;
            try
            {
                empty = !Directory.EnumerateFileSystemEntries(dir).Any();
            }
            catch
            {
                break;
            }
            if (!empty)
                break;

            try
            {
                Directory.Delete(dir);
            }
            catch
            {
                break;
            }

            dir = Path.GetDirectoryName(dir);
        }
    }

    private static bool IsProtectedDirectory(string dir, string? protectedFull)
    {
        if (protectedFull is null)
            return false;
        if (SamePath(dir, protectedFull))
            return true;

        var prefix = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return protectedFull.StartsWith(prefix, PathComparison());
    }

    private static bool IsHiddenPath(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.StartsWith('.') && segment is not ("." or ".."));

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized))
            return normalized;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != ".")
            .ToArray();
        return string.Join('/', segments);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private static bool SamePath(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Path.GetFullPath(a!), Path.GetFullPath(b!), PathComparison());

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;

            var i = 0;
            var j = 0;
            while (i < x.Length && j < y.Length)
            {
                var cx = x[i];
                var cy = y[j];

                if (char.IsDigit(cx) && char.IsDigit(cy))
                {
                    var endI = i;
                    while (endI < x.Length && char.IsDigit(x[endI]))
                        endI++;
                    var endJ = j;
                    while (endJ < y.Length && char.IsDigit(y[endJ]))
                        endJ++;

                    var cmp = CompareNumeric(x[i..endI], y[j..endJ]);
                    if (cmp != 0)
                        return cmp;

                    i = endI;
                    j = endJ;
                    continue;
                }

                var c = char.ToLowerInvariant(cx).CompareTo(char.ToLowerInvariant(cy));
                if (c != 0)
                    return c;

                i++;
                j++;
            }

            return (x.Length - i) - (y.Length - j);
        }

        private static int CompareNumeric(string a, string b)
        {
            var sa = a.TrimStart('0');
            var sb = b.TrimStart('0');
            if (sa.Length != sb.Length)
                return sa.Length - sb.Length;
            return string.CompareOrdinal(sa, sb);
        }
    }
}
