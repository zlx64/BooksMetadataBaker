using PrepKavitaPdf.Models;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Services;

public class AggregatedMetadataService(
    AniListService ani,
    GoogleBooksService google,
    ComicVineService comic,
    ILogger<AggregatedMetadataService> logger,
    IConfiguration config)
    : IAggregatedMetadataService
{
    private readonly string preferredTitleVariant = (config["Tools:PreferredTitleVariant"] ?? "English").Trim().ToLowerInvariant();

    public async Task<Dictionary<string,string>> FetchMetadataAsync(string title, BookType type, string? volumeToken = null, CancellationToken ct = default)
    {
        logger.LogInformation("Fetching aggregated metadata for Title={Title} Type={Type} VolumeToken={Volume} PrefVariant={Pref}", title, type, volumeToken, preferredTitleVariant);

        Dictionary<string,string>? enriched = null;
        if (!string.IsNullOrWhiteSpace(volumeToken))
        {
            var volNormalized = volumeToken.Trim();
            var variants = new List<string>
            {
                title + " " + volNormalized,
                title + " Vol " + volNormalized,
                title + " Volume " + volNormalized
            };
            foreach (var variant in variants)
            {
                var result = await TryAllSourcesAsync(variant, type, ct);
                if (HasVolumeSpecificity(result, volNormalized)) { enriched = result; break; }
            }
        }

        var baseMeta = enriched ?? await TryAllSourcesAsync(title, type, ct);

        NormalizeTitles(baseMeta, title);
        EnsureSummary(baseMeta, volumeToken);

        return baseMeta;
    }

    private async Task<Dictionary<string,string>> TryAllSourcesAsync(string searchTitle, BookType type, CancellationToken ct)
    {
        var tasks = new[]
        {
            ani.TryFetchAsync(searchTitle, type, ct),
            google.TryFetchAsync(searchTitle, type, ct),
            //comic.TryFetchAsync(searchTitle, type, ct)
        };
        var results = await Task.WhenAll(tasks);
        var merged = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in results)
        {
            foreach (var kv in dict)
            {
                if (!merged.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    merged[kv.Key] = kv.Value;
            }
        }
        return merged;
    }

    private void NormalizeTitles(Dictionary<string,string> meta, string fallbackTitle)
    {
        var english = meta.TryGetValue("TitleEnglish", out var te) ? te : null;
        var romaji = meta.TryGetValue("TitleRomaji", out var tr) ? tr : null;
        var native = meta.TryGetValue("TitleNative", out var tn) ? tn : null;
        var plain = meta.TryGetValue("Title", out var tPlain) ? tPlain : null;

        string canonical = fallbackTitle;
        if (preferredTitleVariant == "english" && !string.IsNullOrWhiteSpace(english)) canonical = english!;
        else if (preferredTitleVariant == "romaji" && !string.IsNullOrWhiteSpace(romaji)) canonical = romaji!;
        else if (preferredTitleVariant == "native" && !string.IsNullOrWhiteSpace(native)) canonical = native!;
        else
        {
            canonical = !string.IsNullOrWhiteSpace(english) ? english! :
                        !string.IsNullOrWhiteSpace(romaji) ? romaji! :
                        !string.IsNullOrWhiteSpace(native) ? native! :
                        !string.IsNullOrWhiteSpace(plain) ? plain! : fallbackTitle;
        }

        meta["Title"] = canonical;
        meta["TitleEnglish"] = canonical;
        meta["TitleRomaji"] = canonical;
        meta["TitleNative"] = canonical;
    }

    private static bool HasVolumeSpecificity(Dictionary<string,string> meta, string volumeToken)
    {
        var token = volumeToken.Trim();
        var volNum = ParseVolumeNumber(token);
        var titleFields = new[] { "Title", "TitleEnglish", "TitleRomaji", "TitleNative" };
        foreach (var f in titleFields)
        {
            if (meta.TryGetValue(f, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                var extracted = ExtractVolumeNumber(v);
                if (extracted != null && volNum != null && extracted == volNum) return true;
            }
        }
        return false;
    }

    private static void EnsureSummary(Dictionary<string,string> meta, string? volumeToken)
    {
        bool hasDesc = meta.TryGetValue("Description", out var descVal) && !string.IsNullOrWhiteSpace(descVal);
        if (hasDesc) return;
        if (meta.TryGetValue("Snippet", out var snippet) && !string.IsNullOrWhiteSpace(snippet))
        {
            meta["Description"] = snippet.Trim();
            return;
        }
        // Build synthetic summary
        var parts = new List<string>();
        if (meta.TryGetValue("Title", out var t) && !string.IsNullOrWhiteSpace(t)) parts.Add(t.Trim());
        if (!string.IsNullOrWhiteSpace(volumeToken)) parts.Add("Volume " + volumeToken!.Trim());
        if (meta.TryGetValue("Authors", out var a) && !string.IsNullOrWhiteSpace(a)) parts.Add("By " + a);
        if (meta.TryGetValue("Publisher", out var p) && !string.IsNullOrWhiteSpace(p)) parts.Add("Publisher: " + p);
        if (meta.TryGetValue("Format", out var f) && !string.IsNullOrWhiteSpace(f)) parts.Add("Format: " + f);
        if (meta.TryGetValue("Status", out var s) && !string.IsNullOrWhiteSpace(s)) parts.Add("Status: " + s);
        if (meta.TryGetValue("Genres", out var g) && !string.IsNullOrWhiteSpace(g)) parts.Add("Genres: " + g);
        var synthetic = string.Join(". ", parts);
        if (!string.IsNullOrWhiteSpace(synthetic)) meta["Description"] = synthetic;
    }

    private static double? ParseVolumeNumber(string token) => double.TryParse(token, out var d) ? d : null;

    private static double? ExtractVolumeNumber(string text)
    {
        var m = Regex.Match(text, @"(?i)\bvol(?:ume)?\.?\s*(\d+(?:\.\d+)?)");
        if (m.Success && double.TryParse(m.Groups[1].Value, out var d)) return d;
        var m2 = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*$");
        if (m2.Success && double.TryParse(m2.Groups[1].Value, out var d2)) return d2;
        return null;
    }
}
