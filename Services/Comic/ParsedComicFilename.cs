namespace BooksMetadataBaker.Services.Comic;

/// <summary>
/// Result of parsing a manga/comic archive filename per kavita-manga-comics-agent-guide.md §3.
/// </summary>
public sealed record ParsedComicFilename(
    string? Volume,
    string? Chapter,
    bool IsSpecial,
    string? SpNumber,
    string? SeriesHint)
{
    public bool HasVolume => !string.IsNullOrWhiteSpace(Volume);
    public bool HasChapter => !string.IsNullOrWhiteSpace(Chapter);
}
