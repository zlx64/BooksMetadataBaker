using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Types;

namespace BooksMetadataBaker.Services.Abstract;

public interface IComicMetadataUpdater
{
    /// <summary>
    /// Runs the ComicInfo pipeline for a comic/manga archive. When
    /// <paramref name="splitFileNameForVolume"/> is supplied and the archive is a
    /// verifiable multi-volume set (see <see cref="MultiVolumeSplitter"/>), it is split
    /// into one archive per volume — named by that function (volume number → file name) —
    /// each with its own ComicInfo.xml. When null, or when the layout is not verifiable,
    /// the archive is written as a single file exactly as before.
    /// </summary>
    Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(
        string filePath,
        IDictionary<string, string> metadata,
        string fallbackTitle,
        ParsedComicFilename parsed,
        BookType type,
        CancellationToken ct,
        Func<string, string>? splitFileNameForVolume = null);
}
