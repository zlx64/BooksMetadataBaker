using PrepKavitaPdf.Services.Types;

namespace PrepKavitaPdf.Services.Abstract;

public interface IPdfMetadataUpdater
{
    Task<IReadOnlyList<PdfMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        CancellationToken ct);

    void WriteSidecarSummary(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        bool success,
        string? errors,
        bool metadataApplied,
        bool ghostscriptRan);

    void WriteKavitaSeriesMetadata(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle);
}
