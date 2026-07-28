namespace BooksMetadataBaker.Services.Abstract;

public interface IKavitaMetadataWriter
{
    void Write(string filePath, IDictionary<string, string> metadata, string fallbackTitle);
}
