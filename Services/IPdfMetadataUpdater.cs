using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PrepKavitaPdf.Services;

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
