using PrepKavitaPdf.Models;

namespace PrepKavitaPdf.Services;

public interface IAggregatedMetadataService
{
    Task<Dictionary<string,string>> FetchMetadataAsync(string title, BookType type, CancellationToken ct = default);
}
