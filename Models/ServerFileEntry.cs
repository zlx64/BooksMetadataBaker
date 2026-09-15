namespace BooksMetadataBaker.Models;

/// <summary>
/// One entry of the server-files directory listing (GET /api/files).
/// <see cref="Path"/> is relative to the server-files root, '/'-separated.
/// </summary>
public sealed record ServerFileEntry(
    string Name,
    string Path,
    bool IsDir,
    long Size,
    bool Selectable,
    bool HasImages = false);
