using PrepKavitaPdf.Services.Types;
using System.Text.Json;

namespace PrepKavitaPdf.Services.Helpers;

public static class PdfSidecarWriter
{
    public static void Write(SidecarSummary summary, ILogger logger)
    {
        try
        {
            var sidecar = summary.FilePath + ".meta.json";
            var obj = new Dictionary<string, object?>
            {
                ["AppliedTitle"] = MetadataHelpers.GetFirst(summary.Metadata, summary.FallbackTitle, "Title", "TitleEnglish", "TitleRomaji", "TitleNative"),
                ["Success"] = summary.Success,
                ["MetadataApplied"] = summary.MetadataApplied,
                ["GhostscriptRan"] = summary.GhostscriptRan,
                ["Errors"] = summary.Errors,
                ["TimestampUtc"] = DateTime.UtcNow
            };
            foreach (var kv in summary.Metadata) obj[kv.Key] = kv.Value;
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecar, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing sidecar metadata for {File}", summary.FilePath);
        }
    }
}
