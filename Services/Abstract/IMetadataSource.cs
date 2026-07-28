namespace BooksMetadataBaker.Services.Abstract;

public interface IMetadataSource
{
    Task<Dictionary<string, string>> TryFetchAsync(string title, BookType type, CancellationToken ct);
}
