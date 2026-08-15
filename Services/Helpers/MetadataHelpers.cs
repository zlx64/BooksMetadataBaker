using System.Globalization;

namespace BooksMetadataBaker.Services.Helpers;

public static class MetadataHelpers
{
    /// <summary>
    /// Extracts a volume/issue number token from a file name, e.g. "One Piece Vol. 10.pdf" -> "10".
    /// Prefers explicit vol/volume keywords, falls back to the first bare number (1-999).
    /// </summary>
    public static string? ExtractVolumeToken(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var volMatch = Regex.Match(baseName, @"(\b|_)(?:v|vol|volume)[ _-]?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (volMatch.Success)
            return volMatch.Groups[2].Value;
        var numMatch = Regex.Match(baseName, @"\b(\d{1,3}(?:\.\d+)?)\b");
        return numMatch.Success ? numMatch.Groups[1].Value : null;
    }

    public static string GetFirst(IDictionary<string, string> dict, string fallback, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (dict.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        }
        return fallback;
    }

    public static string Combine(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b ?? string.Empty;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return a + "; " + b;
    }

    public static List<string> CollectAlternateTitles(IDictionary<string, string> meta, string mainTitle)
    {
        var list = new List<string>();
        Add("TitleEnglish");
        Add("TitleRomaji");
        Add("TitleNative");
        return list;

        void Add(string key)
        {
            if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && !v.Equals(mainTitle, StringComparison.OrdinalIgnoreCase) && !list.Contains(v)) list.Add(v);
        }
    }

    public static List<string> SplitAuthors(IDictionary<string, string> meta, string key)
    {
        if (!meta.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public static List<string> GetGenres(IDictionary<string, string> meta)
    {
        var genres = new List<string>();
        AddCsv("Genres");
        AddCsv("Categories");
        return genres.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        void AddCsv(string key)
        {
            if (!meta.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return;
            genres.AddRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    public static List<string> GetTags(IDictionary<string, string> meta)
    {
        var tags = new List<string>();
        Add("Format");
        Add("Status");
        Add("Source");
        Add("Language");
        if (meta.TryGetValue("Genres", out var g)) tags.AddRange(g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (meta.TryGetValue("Categories", out var c)) tags.AddRange(c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        void Add(string key)
        {
            if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) tags.Add(v);
        }
    }

    public static int InferAgeRating(IDictionary<string, string> meta, IEnumerable<string> genres, IEnumerable<string> tags)
    {
        var tokens = new List<string>();
        foreach (var g in genres) Collect(g);
        foreach (var t in tags) Collect(t);
        if (meta.TryGetValue("Description", out var desc)) Collect(desc);
        var lowered = tokens.Select(x => x.ToLowerInvariant()).ToList();
        string[] adult = ["adult", "hentai", "mature", "18", "erotic", "nsfw", "porn", "smut"];
        if (lowered.Any(l => adult.Contains(l))) return 18;
        string[] teenPlus = ["seinen", "violence", "gore", "horror", "dark"];
        if (lowered.Any(l => teenPlus.Contains(l))) return 16;
        string[] teen = ["shounen", "romance", "ya", "teen"];
        if (lowered.Any(l => teen.Contains(l))) return 13;
        return 0;

        void Collect(string? v)
        {
            if (!string.IsNullOrWhiteSpace(v)) tokens.AddRange(v.Split([' ', ',', ';', '.', '/', '\\', '|'], StringSplitOptions.RemoveEmptyEntries));
        }
    }

    public static int ExtractYear(IDictionary<string, string> meta)
    {
        string? pick = null;
        foreach (var key in new[] { "PublishedDate", "StartDate", "StartYear", "EndDate" })
        {
            if (meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) { pick = v; break; }
        }
        if (pick == null) return 0;
        foreach (var part in pick.Split('-', ' ', '/', '.'))
        {
            if (part.Length == 4 && int.TryParse(part, out var yr) && yr > 0) return yr;
        }
        var digits = new string(pick.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4 && int.TryParse(digits[..4], out var y2)) return y2;
        return 0;
    }

    public static string NormDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (DateTime.TryParse(raw, out var dt)) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length >= 8) return digits[..4] + "-" + digits.Substring(4, 2) + "-" + digits.Substring(6, 2);
        if (digits.Length >= 6) return digits[..4] + "-" + digits.Substring(4, 2) + "-01";
        if (digits.Length >= 4) return digits[..4];
        return raw;
    }

    public static double? ParseVolumeNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = Regex.Match(title, @"(?:^|[\s._-])(?:vol(?:ume)?|v|issue|ch(?:apter)?|part)\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        var trailing = Regex.Match(title, @"(\d+(?:\.\d+)?)\s*$");
        if (trailing.Success && double.TryParse(trailing.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }
}
