namespace PrepKavitaPdf.Models;

public sealed record EBookUploadProcessResult(
    string File,
    bool Success,
    string? ErrorMessage,
    int Attempts,
    IDictionary<string, string> AppliedMetadata,
    bool DirectAttemptSuccess,
    bool RepairAttemptSuccess,
    bool ForceStripAttemptSuccess,
    bool GhostscriptRan,
    EBookFormat Format);
