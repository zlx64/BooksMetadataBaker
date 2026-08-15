namespace BooksMetadataBaker.Services.Abstract;

public interface IKavitaMetadataWriter
{
    Task WriteAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle);
}
