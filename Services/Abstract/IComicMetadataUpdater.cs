using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Types;

namespace BooksMetadataBaker.Services.Abstract;

public interface IComicMetadataUpdater
{
    Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        ParsedComicFilename parsed,
        BookType type,
        CancellationToken ct);
}
