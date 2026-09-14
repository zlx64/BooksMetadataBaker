namespace BooksMetadataBaker.Models;

/// <summary>
/// Request body for POST /api/files/process — process a file that already
/// lives under the configured server-files root (e.g. a Docker volume mount).
/// </summary>
public class ServerFileRequest
{
    public required string Title { get; set; }
    public required BookType Type { get; set; }

    /// <summary>Path of the file relative to the server-files root.</summary>
    public required string Path { get; set; }

    /// <summary>Move the source into the library instead of copying it.</summary>
    public bool MoveOriginals { get; set; }
}
