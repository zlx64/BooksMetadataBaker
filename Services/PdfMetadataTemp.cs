namespace PrepKavitaPdf.Services;

public static class PdfMetadataTemp
{
    public static (string WorkDir, string OutputPath) Prepare(string label)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"pdf_meta_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var outPath = Path.Combine(workDir, Guid.NewGuid().ToString("N") + ".pdf");
        return (workDir, outPath);
    }

    public static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
