using System.Globalization;

namespace BooksMetadataBaker.Services.Comic;

/// <summary>One volume to extract: its number (normalized), the '/'-separated folder
/// prefix that owns its entries (trailing '/'), and the page count under it.</summary>
public sealed record MultiVolumePart(string Volume, string FolderPrefix, int PageCount);

/// <summary>A verified multi-volume layout: every entry belongs to exactly one
/// volume folder, each folder name parses to a distinct single volume number,
/// and every folder holds at least one page.</summary>
public sealed record MultiVolumeSplitPlan(IReadOnlyList<MultiVolumePart> Parts);

/// <summary>
/// Decides whether an archive's entry layout is a verifiable multi-volume set
/// and, if so, which folder belongs to which volume. Two layouts qualify:
/// (A) several top-level folders, one per volume, or (B) a single root folder
/// containing several volume subfolders. Anything not verifiable (loose files,
/// unparseable or duplicate volume numbers, ranges, chapter-named folders,
/// empty folders) returns null and the archive is left as one.
/// </summary>
public static class MultiVolumeSplitter
{
    public static MultiVolumeSplitPlan? TryPlan(IReadOnlyList<string>? entryNames)
    {
        if (entryNames is null || entryNames.Count == 0)
            return null;

        // Normalize separators and drop directory entries (trailing '/').
        var files = new List<string>(entryNames.Count);
        foreach (var raw in entryNames)
        {
            var n = (raw ?? string.Empty).Replace('\\', '/');
            while (n.StartsWith("./", StringComparison.Ordinal))
                n = n[2..];
            n = n.TrimStart('/');
            if (n.Length == 0 || n.EndsWith('/'))
                continue;
            files.Add(n);
        }
        if (files.Count == 0)
            return null;

        var topLevel = files
            .Select(f => f.Split('/')[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (topLevel.Count >= 2)
        {
            // Case A: top-level folders are the volume folders.
            // Only a root-level ComicInfo.xml may sit outside them.
            if (files.Any(f => !f.Contains('/') && !IsRootComicInfo(f)))
                return null;
            // A root-level file (ComicInfo.xml) appears in topLevel too; only
            // segments that actually own files are candidate volume folders.
            var folders = topLevel
                .Where(t => files.Any(f => f.StartsWith(t + "/", StringComparison.Ordinal)))
                .ToList();
            if (folders.Count < 2)
                return null;
            return BuildPlan(files, folders.Select(t => t + "/").ToList(), folders);
        }

        if (topLevel.Count == 1)
        {
            // Case B: a single root folder whose subfolders are the volumes.
            var root = topLevel[0];
            var rootPrefix = root + "/";

            // Loose files directly under the root (besides ComicInfo.xml) make the
            // layout unverifiable — we cannot tell which volume they belong to.
            if (files.Any(f => f.StartsWith(rootPrefix, StringComparison.Ordinal)
                               && !f[rootPrefix.Length..].Contains('/')
                               && !IsRootComicInfo(f[rootPrefix.Length..])))
                return null;

            var subs = files
                .Where(f => f.StartsWith(rootPrefix, StringComparison.Ordinal) && f[rootPrefix.Length..].Contains('/'))
                .Select(f => f[rootPrefix.Length..].Split('/')[0])
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (subs.Count < 2)
                return null;

            var subPrefixes = subs.Select(s => rootPrefix + s + "/").ToList();
            if (files.Any(f => f.StartsWith(rootPrefix, StringComparison.Ordinal)
                               && !IsRootComicInfo(f[rootPrefix.Length..])
                               && !subPrefixes.Any(p => f.StartsWith(p, StringComparison.Ordinal))))
                return null;

            return BuildPlan(files, subPrefixes, subs);
        }

        // Files only at the archive root — a single flat volume, nothing to split.
        return null;
    }

    private static MultiVolumeSplitPlan? BuildPlan(
        List<string> files,
        IReadOnlyList<string> prefixes,
        IReadOnlyList<string> folderNames)
    {
        var parts = new List<MultiVolumePart>(folderNames.Count);
        var seenVolumes = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < folderNames.Count; i++)
        {
            var parsed = ComicFilenameParser.Parse(folderNames[i]);

            // The folder must name exactly one volume. Ranges ("vol 01-05"),
            // chapter-only names, and bare numbers are not verifiable as volumes.
            if (parsed.Volume is null || parsed.Volume.Contains('-'))
                return null;
            if (!decimal.TryParse(parsed.Volume, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                || number < 0)
                return null;
            if (!seenVolumes.Add(parsed.Volume))
                return null;

            var prefix = prefixes[i];
            var pages = 0;
            foreach (var f in files)
            {
                if (f.StartsWith(prefix, StringComparison.Ordinal)
                    && ArchiveComicInfoWriter.IsPageEntry(f[prefix.Length..]))
                    pages++;
            }
            if (pages == 0)
                return null;

            parts.Add(new MultiVolumePart(parsed.Volume, prefix, pages));
        }

        parts.Sort(static (a, b) =>
            decimal.Compare(
                decimal.Parse(a.Volume, CultureInfo.InvariantCulture),
                decimal.Parse(b.Volume, CultureInfo.InvariantCulture)));

        return new MultiVolumeSplitPlan(parts);
    }

    private static bool IsRootComicInfo(string name) =>
        string.Equals(name, ComicInfoXmlWriter.FileName, StringComparison.OrdinalIgnoreCase);
}
