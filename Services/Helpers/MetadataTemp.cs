namespace BooksMetadataBaker.Services.Helpers;

public static class MetadataTemp
{
    public static (string WorkDir, string OutputPath) Prepare(string label, string fileExtension)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"temp_meta_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var outPath = Path.Combine(workDir, Guid.NewGuid().ToString("N") + fileExtension);
        return (workDir, outPath);
    }

    public static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
