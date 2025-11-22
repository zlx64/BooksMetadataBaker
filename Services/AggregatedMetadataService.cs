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

        // Normalize titles to preferred language/style
        NormalizeTitles(baseMeta, title);

        // Apply volume corrections if requested
        if (!string.IsNullOrWhiteSpace(volumeToken)) ApplyVolumeCorrections(baseMeta, volumeToken!.Trim());

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
            // Fallback priority: English -> Romaji -> Native -> Plain -> fallback
            canonical = !string.IsNullOrWhiteSpace(english) ? english! :
                        !string.IsNullOrWhiteSpace(romaji) ? romaji! :
                        !string.IsNullOrWhiteSpace(native) ? native! :
                        !string.IsNullOrWhiteSpace(plain) ? plain! : fallbackTitle;
        }

        // Force all title variants to canonical to keep consistency across volumes.
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

    private static void ApplyVolumeCorrections(Dictionary<string,string> meta, string volumeToken)
    {
        var targetVol = ParseVolumeNumber(volumeToken);
        if (targetVol == null) return;
        var canonicalBase = meta["Title"]; // already normalized
        var existingExtract = ExtractVolumeNumber(canonicalBase);
        if (existingExtract != targetVol)
        {
            var rebuilt = BuildVolumeTitle(RemoveVolumeMarkers(canonicalBase), targetVol.Value);
            meta["Title"] = rebuilt;
            meta["TitleEnglish"] = rebuilt;
            meta["TitleRomaji"] = rebuilt;
            meta["TitleNative"] = rebuilt;
        }
        meta["SeriesIndex"] = targetVol.Value.ToString();
        meta["VolumeNumber"] = targetVol.Value.ToString();
    }

    private static string RemoveVolumeMarkers(string title)
    {
        var cleaned = Regex.Replace(title, @"(?i)\bvol(?:ume)?\s*\d+(?:\.\d+)?", "").Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static string BuildVolumeTitle(string baseTitle, double vol) => $"{baseTitle} Vol {vol:g}";
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
