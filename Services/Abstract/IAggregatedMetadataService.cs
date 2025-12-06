using BooksMetadataBaker.Models;

namespace BooksMetadataBaker.Services.Abstract;

public interface IAggregatedMetadataService
{
    Task<Dictionary<string,string>> FetchMetadataAsync(string title, BookType type, string? volumeToken = null, CancellationToken ct = default);
}
