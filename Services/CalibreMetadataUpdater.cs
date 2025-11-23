using PrepKavitaPdf.Services.Helpers;
using System.Diagnostics;
using System.Globalization;

namespace PrepKavitaPdf.Services;

public static class CalibreMetadataUpdater
{
    public static bool TryCleanPdfMetadata(string path, ILogger logger, out string? error)
    {
        error = null;
        try
        {
            var args = BuildEbookMetaCleanArgs(path);
            var psi = new ProcessStartInfo("ebook-meta", args)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                error = "failed to start ebook-meta (cleanup)";
                return false;
            }
            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();
            if (proc.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr + stdout) ? $"ebook-meta cleanup exit {proc.ExitCode}" : stderr + stdout;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogWarning(ex, "Cleanup exception for {File}", path);
            return false;
        }
    }

    public static bool TryWriteMetadataWithCalibre(
        string path,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        ILogger logger,
        out string? error)
    {
        error = null;
        try
        {
            var args = BuildEbookMetaArgs(path, metadata, fallbackTitle);
            var psi = new ProcessStartInfo("ebook-meta", args)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                error = "failed to start ebook-meta";
                return false;
            }
            proc.WaitForExit();
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();
            if (proc.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr + stdout) ? $"ebook-meta exit {proc.ExitCode}" : stderr + stdout;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(ex, "ebook-meta exception for {File}", path);
            return false;
        }
    }

    private static string BuildEbookMetaCleanArgs(string filePath)
    {
        var fields = new[] { "--title", "--authors", "--comments", "--tags", "--series", "--publisher", "--isbn", "--language" };
        var parts = new List<string>();
        foreach (var f in fields) parts.Add(f + " " + Q(string.Empty));
        parts.Add(Q(filePath));
        return string.Join(' ', parts);
        static string Q(string v) => '"' + v.Replace("\"", "\\\"") + '"';
    }

    private static string BuildEbookMetaArgs(string filePath, IDictionary<string, string> meta, string fallbackTitle)
    {
        var parts = new List<string>();
        //var title = MetadataHelpers.GetFirst(meta, fallbackTitle, "TitleEnglish", "TitleRomaji", "Title", "TitleNative");
        var newTitle = Path.GetFileNameWithoutExtension(filePath);
        parts.Add("--title " + Clean(newTitle));
        parts.Add("--series " + Clean(fallbackTitle));
        var idx = MetadataHelpers.ParseVolumeNumber(newTitle);
        if (idx != null) parts.Add("--index " + Clean(idx.Value.ToString(CultureInfo.InvariantCulture)));
        var rating = MetadataHelpers.GetFirst(meta, string.Empty, "AverageScore", "Rating", "UserRating");
        if (!string.IsNullOrWhiteSpace(rating)) parts.Add("--rating " + Clean(rating));
        var desc = MetadataHelpers.GetFirst(meta, string.Empty, "Description", "Snippet");
        if (!string.IsNullOrWhiteSpace(desc)) parts.Add("--comments " + Clean(desc));
        var publisher = MetadataHelpers.GetFirst(meta, string.Empty, "Publisher");
        if (!string.IsNullOrWhiteSpace(publisher)) parts.Add("--publisher " + Clean(publisher));
        var dateRaw = MetadataHelpers.GetFirst(meta, string.Empty, "PublishedDate", "StartDate", "StartYear");
        var dateIso = MetadataHelpers.NormDate(dateRaw);
        if (!string.IsNullOrWhiteSpace(dateIso)) parts.Add("--date " + Clean(dateIso));
        var authorsRaw = MetadataHelpers.GetFirst(meta, string.Empty, "Authors", "Author", "Writer");
        if (!string.IsNullOrWhiteSpace(authorsRaw))
        {
            var authors = authorsRaw.Split([',', ';', '|', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            parts.Add("--authors " + Clean(string.Join(" & ", authors)));
        }
        var tagList = MetadataHelpers.GetTags(meta);
        if (tagList.Count > 0) parts.Add("--tags " + Clean(string.Join(',', tagList)));
        var lang = MetadataHelpers.GetFirst(meta, string.Empty, "Language");
        if (!string.IsNullOrWhiteSpace(lang)) parts.Add("--language " + Clean(lang.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? lang));
        parts.Add(Clean(filePath));
        return string.Join(' ', parts);

        static string Clean(string v) => '"' + v.Replace("\"", "\\\"") + '"';
    }
}
