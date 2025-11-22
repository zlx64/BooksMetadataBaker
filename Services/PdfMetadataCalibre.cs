using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PrepKavitaPdf.Services;

public static class PdfMetadataCalibre
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
        string Q(string v) => '"' + v.Replace("\"", "\\\"") + '"';
        var fields = new[] { "--title", "--authors", "--comments", "--tags", "--series", "--publisher", "--isbn", "--language" };
        var parts = new List<string>();
        foreach (var f in fields) parts.Add(f + " " + Q(string.Empty));
        parts.Add(Q(filePath));
        return string.Join(' ', parts);
    }

    private static string BuildEbookMetaArgs(string filePath, IDictionary<string, string> meta, string fallbackTitle)
    {
        string Q(string v) => '"' + v.Replace("\"", "\\\"") + '"';
        var parts = new List<string>();
        var title = PdfMetadataHelpers.GetFirst(meta, fallbackTitle, "TitleEnglish", "TitleRomaji", "Title", "TitleNative");
        var newTitle = Path.GetFileNameWithoutExtension(filePath);
        parts.Add("--title " + Q(newTitle ?? title));
        parts.Add("--series " + Q(fallbackTitle));
        var idx = PdfMetadataHelpers.ParseVolumeNumber(newTitle);
        if (idx != null) parts.Add("--index " + Q(idx.Value.ToString(CultureInfo.InvariantCulture)));
        var rating = PdfMetadataHelpers.GetFirst(meta, string.Empty, "AverageScore", "Rating", "UserRating");
        if (!string.IsNullOrWhiteSpace(rating)) parts.Add("--rating " + Q(rating));
        var desc = PdfMetadataHelpers.GetFirst(meta, string.Empty, "Description", "Snippet");
        if (!string.IsNullOrWhiteSpace(desc)) parts.Add("--comments " + Q(desc));
        var publisher = PdfMetadataHelpers.GetFirst(meta, string.Empty, "Publisher");
        if (!string.IsNullOrWhiteSpace(publisher)) parts.Add("--publisher " + Q(publisher));
        var dateRaw = PdfMetadataHelpers.GetFirst(meta, string.Empty, "PublishedDate", "StartDate", "StartYear");
        var dateIso = PdfMetadataHelpers.NormDate(dateRaw);
        if (!string.IsNullOrWhiteSpace(dateIso)) parts.Add("--date " + Q(dateIso));
        var authorsRaw = PdfMetadataHelpers.GetFirst(meta, string.Empty, "Authors", "Author", "Writer");
        if (!string.IsNullOrWhiteSpace(authorsRaw))
        {
            var authors = authorsRaw.Split(new[] { ',', ';', '|', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            parts.Add("--authors " + Q(string.Join(" & ", authors)));
        }
        var tagList = PdfMetadataHelpers.GetTags(meta);
        if (tagList.Count > 0) parts.Add("--tags " + Q(string.Join(',', tagList)));
        var lang = PdfMetadataHelpers.GetFirst(meta, string.Empty, "Language");
        if (!string.IsNullOrWhiteSpace(lang)) parts.Add("--language " + Q(lang.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? lang));
        parts.Add(Q(filePath));
        return string.Join(' ', parts);
    }
}
