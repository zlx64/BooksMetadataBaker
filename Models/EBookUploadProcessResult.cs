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
    EBookFormat Format);
