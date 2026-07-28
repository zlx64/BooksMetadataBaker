using BooksMetadataBaker.Services.Types;

namespace BooksMetadataBaker.Services.Abstract;

public interface IEBookMetadataUpdater
{
    Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(
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
}
