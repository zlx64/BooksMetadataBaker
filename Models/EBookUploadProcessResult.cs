namespace BooksMetadataBaker.Models;

public sealed record EBookUploadProcessResult(
    string File,
    bool Success,
    string? ErrorMessage,
    int Attempts,
    IDictionary<string, string> AppliedMetadata,
    bool DirectAttemptSuccess,
    bool RepairAttemptSuccess,
    bool GhostscriptRan,
    EBookFormat Format,
    bool ComicInfoWritten,
    int PageCount,
    // File names when a multi-volume archive was split into several archives
    // (File is the first of them). Empty when the file was kept as a single archive.
    IReadOnlyList<string> SplitFiles);
