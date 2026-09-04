using System.Globalization;

namespace BooksMetadataBaker.Services.Comic;

/// <summary>
/// Parses manga/comic archive filenames per kavita-manga-comics-agent-guide.md §3:
/// volume markers (v/vol/vol./volume + locale variants, ranges, first marker wins),
/// chapter markers (c/ch/ch./chapter/chp/episode + locale variants, ranges, decimals,
/// trailing b = half chapter), bare trailing number fallback, and SP## special forcing.
/// No volume + no chapter parsed means the file is a Special.
/// </summary>
public static class ComicFilenameParser
{
    // Scanlation/bracket groups like [KSH] carry no numbering and are dropped first.
    // Parenthesized groups are kept for marker matching (e.g. "(v01)") and only
    // stripped for the bare trailing number fallback.
    private static readonly Regex BracketGroups = new(@"\s*\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex ParenGroups = new(@"\([^)]*\)", RegexOptions.Compiled);

    // ASCII markers use a negative letter lookbehind instead of \b so markers
    // preceded by an underscore (scanlation style: "_vol01_") still match, while
    // mid-word letters ("dc 3") don't. Non-ASCII (CJK/KR/TH/RU) markers are
    // self-delimiting. Longest alternatives first; optional trailing range (v16-17).
    private static readonly Regex VolumeRegex = new(
        @"(?:(?<![A-Za-z])(?:volume|vol\.?|tome|[vt])|巻|卷|册|권|장|시즌|เล่มที่|เล่ม|Том|Тома)" +
        @"\s*\.?\s*(\d{1,4}(?:\.\d+)?)(?:\s*[-–]\s*(?:volume|vol\.?|tome|[vt])?\s*(\d{1,4}(?:\.\d+)?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Manga-only season marker (e.g. "Tower Of God S01 014.cbz" -> vol 1, ch 14).
    // Only counts when a chapter number follows, to avoid titles containing "S01".
    private static readonly Regex SeasonRegex = new(
        @"(?<![A-Za-z])S(\d{1,2})\b(?=\s+\d{1,4}\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Chapter markers; optional trailing 'b' = half chapter (153b -> 153.5).
    private static readonly Regex ChapterRegex = new(
        @"(?:(?<![A-Za-z])(?:chapter|chp\.?|ch\.?|episode|ep|c)|話|话|화|회|บทที่|ตอนที่|Глава)" +
        @"\s*\.?\s*(\d{1,4}(?:\.\d+)?)(b)?(?:\s*[-–]\s*(?:chapter|chp\.?|ch\.?|episode|ep|c)?\s*(\d{1,4}(?:\.\d+)?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // SP## forces Special status (§3); stripped from the displayed title.
    private static readonly Regex SpMarker = new(@"(?<![A-Za-z])SP\s*(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingNumber = new(@"(\d{1,4}(?:\.\d+)?)(b?)\s*$", RegexOptions.Compiled);

    public static ParsedComicFilename Parse(string fileName)
    {
        var baseName = StripExtension(Path.GetFileName(fileName));
        var name = BracketGroups.Replace(baseName, string.Empty);

        Match? spMatch = SpMarker.Match(name);
        var spNumber = spMatch.Success ? Normalize(spMatch.Groups[1].Value) : null;

        Match? volumeMatch;
        string? volume;
        var volMatch = VolumeRegex.Match(name);
        if (volMatch.Success)
        {
            volumeMatch = volMatch;
            volume = BuildNumber(volMatch.Groups[1].Value, volMatch.Groups[2].Value, halfChapter: false);
        }
        else
        {
            var season = SeasonRegex.Match(name);
            volumeMatch = season.Success ? season : null;
            volume = season.Success ? Normalize(season.Groups[1].Value) : null;
        }

        var chapterMatch = FindChapter(name, volumeMatch);
        var chapter = chapterMatch is null
            ? FindBareTrailingChapter(name)
            : BuildNumber(chapterMatch.Groups[1].Value, chapterMatch.Groups[3].Value, chapterMatch.Groups[2].Success);

        var isSpecial = spNumber is not null || (volume is null && chapter is null);

        return new ParsedComicFilename(
            Volume: volume,
            Chapter: chapter,
            IsSpecial: isSpecial,
            SpNumber: spNumber,
            SeriesHint: BuildSeriesHint(name, volumeMatch, chapterMatch, spMatch));
    }

    private static Match? FindChapter(string name, Match? volumeMatch)
    {
        var matcher = ChapterRegex.Matches(name);
        foreach (Match m in matcher)
        {
            if (volumeMatch is not null && m.Overlaps(volumeMatch))
                continue;
            return m;
        }
        return null;
    }

    // Bare trailing number in an otherwise unmarked filename ("Beelzebub_53[KSH]" -> 53,
    // "Tower Of God S01 014 (CBT) (digital)" -> 14). All volume/season markers are removed
    // first so a second marker's number cannot become a chapter; parenthesized groups are
    // stripped for this check; 4-digit numbers in 1900-2099 are years, not chapters.
    private static string? FindBareTrailingChapter(string name)
    {
        var residual = name;
        var removable = new List<Match>(VolumeRegex.Matches(residual).OfType<Match>());
        removable.AddRange(SeasonRegex.Matches(residual).OfType<Match>());
        foreach (var rm in removable.OrderByDescending(x => x.Index))
            residual = residual.Remove(rm.Index, rm.Length);
        residual = ParenGroups.Replace(residual, string.Empty);

        var m = TrailingNumber.Match(residual);
        if (!m.Success)
            return null;

        var raw = m.Groups[1].Value;
        if (raw.Length == 4 && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && year is >= 1900 and <= 2099)
            return null;

        var num = Normalize(raw);
        if (m.Groups[2].Value == "b" && decimal.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            num = (d + 0.5m).ToString(CultureInfo.InvariantCulture);
        return num;
    }

    /// <summary>
    /// Strips a trailing file extension only when the last dot segment looks like one
    /// (1-4 alphanumeric chars, at least one letter). Names with dots in markers
    /// ("Vol. 0001", "Ch. 0001", "034.5") are kept intact.
    /// </summary>
    private static string StripExtension(string fileName)
    {
        var lastDot = fileName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == fileName.Length - 1)
            return fileName;

        var ext = fileName[(lastDot + 1)..];
        if (ext.Length is >= 1 and <= 4
            && ext.All(char.IsAsciiLetterOrDigit)
            && ext.Any(char.IsAsciiLetter))
            return fileName[..lastDot];

        return fileName;
    }

    private static string? BuildNumber(string first, string second, bool halfChapter)
    {
        var a = Normalize(first);
        if (a is null)
            return null;

        if (halfChapter)
        {
            if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                a = (d + 0.5m).ToString(CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(second))
            return a;

        var b = Normalize(second);
        return b is null ? a : $"{a}-{b}";
    }

    private static string? BuildSeriesHint(string name, Match? volumeMatch, Match? chapterMatch, Match? spMatch)
    {
        var hint = name;
        var matches = new[] { volumeMatch, chapterMatch, spMatch }
            .Where(x => x is not null)
            .Cast<Match>()
            .OrderByDescending(m => m.Index)
            .ToList();
        foreach (var m in matches)
            hint = hint.Remove(m.Index, m.Length);

        hint = ParenGroups.Replace(hint, string.Empty);
        hint = Regex.Replace(hint, @"\s{2,}", " ");
        hint = hint.Trim((char[])"-_–— ".ToCharArray());
        if (hint.Length == 0)
            return null;
        return hint;
    }

    private static string? Normalize(string raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d.ToString(CultureInfo.InvariantCulture);
        return raw;
    }

    private static bool Overlaps(this Match a, Match b) =>
        a.Index < b.Index + b.Length && b.Index < a.Index + a.Length;
}
