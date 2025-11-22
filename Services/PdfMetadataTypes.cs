using System.Collections.Generic;

namespace PrepKavitaPdf.Services;

public sealed record PdfMetadataAttemptResult(
    string FilePath,
    PdfMetadataAttemptStage Stage,
    bool Success,
    string? ErrorMessage,
    bool GhostscriptRan,
    bool MetadataApplied);

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
