using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services;
using BooksMetadataBaker.Services.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class AggregatedMetadataServiceTests
{
    private sealed class SourceA : IMetadataSource
    {
        private readonly Dictionary<string, string> data;
        public SourceA(Dictionary<string, string> data) => this.data = data;
        public Task<Dictionary<string, string>> TryFetchAsync(string title, BookType type, CancellationToken ct) =>
            Task.FromResult(new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class SourceB : IMetadataSource
    {
        private readonly Dictionary<string, string> data;
        public SourceB(Dictionary<string, string> data) => this.data = data;
        public Task<Dictionary<string, string>> TryFetchAsync(string title, BookType type, CancellationToken ct) =>
            Task.FromResult(new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase));
    }

    private static AggregatedMetadataService CreateService(
        IEnumerable<IMetadataSource> sources,
        IDictionary<string, string?>? extraConfig = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(extraConfig ?? new Dictionary<string, string?>())
            .Build();
        return new AggregatedMetadataService(sources, NullLogger<AggregatedMetadataService>.Instance, config);
    }

    [Fact]
    public async Task Merge_FirstSourceWinsPerKey()
    {
        var service = CreateService(new IMetadataSource[]
        {
            new SourceA(new() { ["Title"] = "From A", ["Authors"] = "A Author", ["Description"] = "A desc" }),
            new SourceB(new() { ["Title"] = "From B", ["Publisher"] = "B Publisher" })
        });

        var meta = await service.FetchMetadataAsync("Some Book", BookType.Book);

        Assert.Equal("From A", meta["Title"]);
        Assert.Equal("A Author", meta["Authors"]);
        Assert.Equal("B Publisher", meta["Publisher"]);
    }

    [Fact]
    public async Task Merge_SourceOrderConfigChangesPriority()
    {
        var service = CreateService(
            new IMetadataSource[]
            {
                new SourceA(new() { ["Title"] = "From A" }),
                new SourceB(new() { ["Title"] = "From B" })
            },
            new Dictionary<string, string?> { ["Tools:SourceOrder"] = "SourceB,SourceA" });

        var meta = await service.FetchMetadataAsync("Some Book", BookType.Book);

        Assert.Equal("From B", meta["Title"]);
    }

    [Fact]
    public async Task Merge_SourceOrderWinsOverExactTitleMatch()
    {
        // Regression: an exact Title match in a later source must not steal
        // priority from the first source in the configured order (AniList data
        // must keep winning over a same-named Google Books hit for LightNovels).
        var service = CreateService(new IMetadataSource[]
        {
            new SourceA(new() { ["Title"] = "Different Title", ["Description"] = "A desc" }),
            new SourceB(new() { ["Title"] = "My Book", ["Description"] = "B desc", ["Publisher"] = "B Pub" })
        });

        var meta = await service.FetchMetadataAsync("My Book", BookType.Book);

        Assert.Equal("Different Title", meta["Title"]);
        Assert.Equal("A desc", meta["Description"]);
        Assert.Equal("B Pub", meta["Publisher"]);
    }

    [Fact]
    public async Task Merge_SourceOrderConfigMatchesClassNamesByPrefix()
    {
        // Production sources are named AniListService/GoogleBooksService/ComicVineService,
        // while config uses short names (AniList/GoogleBooks/ComicVine).
        var service = CreateService(
            new IMetadataSource[]
            {
                new SourceA(new() { ["Title"] = "From A" }),
                new SourceB(new() { ["Title"] = "From B" })
            },
            new Dictionary<string, string?> { ["Tools:SourceOrder"] = "sourceb, SOURCEA" });

        var meta = await service.FetchMetadataAsync("Some Book", BookType.Book);

        Assert.Equal("From B", meta["Title"]);
    }

    [Fact]
    public async Task NormalizeTitles_FallsBackToSearchTitle()
    {
        var service = CreateService(new IMetadataSource[]
        {
            new SourceA(new() { ["TitleEnglish"] = "English Title" })
        });

        var meta = await service.FetchMetadataAsync("Search Title", BookType.Book);

        Assert.Equal("Search Title", meta["Title"]);
        Assert.Equal("English Title", meta["TitleEnglish"]);
        Assert.Equal("Search Title", meta["TitleRomaji"]);
        Assert.Equal("Search Title", meta["TitleNative"]);
    }

    [Fact]
    public async Task Merge_EmptyValuesAreSkipped()
    {
        var service = CreateService(new IMetadataSource[]
        {
            new SourceA(new() { ["Title"] = "  " }),
            new SourceB(new() { ["Title"] = "Real Title" })
        });

        var meta = await service.FetchMetadataAsync("Fallback", BookType.Book);

        Assert.Equal("Real Title", meta["Title"]);
    }
}
