namespace BooksMetadataBaker.Services.Types;

public sealed record EBookMetadataAttemptResult(
    string FilePath,
    EBookMetadataAttemptStage Stage,
    bool Success,
    string? ErrorMessage,
    bool GhostscriptRan,
    bool MetadataApplied,
    // Additional output files when one input produced several (multi-volume split);
    // FilePath is the first of them. Empty for the normal one-in-one-out flow.
    IReadOnlyList<string> AdditionalFilePaths);

public readonly record struct MetadataRequest(
    string FilePath,
    IDictionary<string, string> Metadata,
    string FallbackTitle);

public readonly record struct DirectAttemptResult(
    bool Success,
    string ErrorMessage);

public readonly record struct RepairAttemptResult(
    bool Success,
    string? ErrorMessage,
    bool GhostscriptRan);

public readonly record struct SidecarSummary(
    string FilePath,
    IDictionary<string, string> Metadata,
    string FallbackTitle,
    bool Success,
    string? Errors,
    bool MetadataApplied,
    bool GhostscriptRan);
