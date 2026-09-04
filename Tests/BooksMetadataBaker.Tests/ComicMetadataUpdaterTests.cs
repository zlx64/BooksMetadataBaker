using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services.Abstract;
using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Types;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ComicMetadataUpdaterTests
{
    private sealed class StubArchiveWriter : IArchiveComicInfoWriter
    {
        public ComicInfoModel? Existing { get; init; }
        public int Pages { get; init; }
        public bool WriteOk { get; init; } = true;
        public string? WriteError { get; init; }
        public string? LastPath { get; private set; }
        public string? WrittenXml { get; private set; }

        public Task<ComicInfoModel?> ReadExistingAsync(string path, CancellationToken ct) =>
            Task.FromResult(Existing);

        public Task<int> CountPagesAsync(string path, CancellationToken ct) =>
            Task.FromResult(Pages);

        public Task<(bool Ok, string? Error)> WriteAsync(string path, string xml, CancellationToken ct)
        {
            LastPath = path;
            WrittenXml = xml;
            return Task.FromResult((WriteOk, WriteError));
        }
    }

    private static ComicMetadataUpdater Create(StubArchiveWriter writer) =>
        new(writer, NullLogger<ComicMetadataUpdater>.Instance);

    private static ParsedComicFilename Parsed(string? volume = null, string? chapter = null) =>
        new(volume, chapter, volume is null && chapter is null, null, null);

    [Fact]
    public async Task Pipeline_Success_WritesComicInfoAndReportsApplied()
    {
        var writer = new StubArchiveWriter { Pages = 12 };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Test Series - Volume 1.cbz",
            new Dictionary<string, string> { ["Title"] = "Test Series", ["Publisher"] = "Acme" },
            "Fallback Title",
            Parsed(volume: "1"),
            BookType.Comic,
            CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.Equal(EBookMetadataAttemptStage.ComicInfo, attempt.Stage);
        Assert.True(attempt.Success);
        Assert.True(attempt.MetadataApplied);
        Assert.False(attempt.GhostscriptRan);
        Assert.Null(attempt.ErrorMessage);
        Assert.Contains("<Series>Test Series</Series>", writer.WrittenXml);
        Assert.Contains("<Volume>1</Volume>", writer.WrittenXml);
        Assert.Contains("<Publisher>Acme</Publisher>", writer.WrittenXml);
        Assert.Contains("<PageCount>12</PageCount>", writer.WrittenXml);
    }

    [Fact]
    public async Task Pipeline_ExistingComicInfoWinsOverFetchedMetadata()
    {
        var writer = new StubArchiveWriter
        {
            Existing = new ComicInfoModel { Series = "Existing Series", Writer = "Existing Writer" }
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbz",
            new Dictionary<string, string> { ["Title"] = "Fetched Title", ["StaffWriter"] = "Fetched Writer" },
            "Fallback Title",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        Assert.True(Assert.Single(attempts).Success);
        Assert.Contains("<Series>Existing Series</Series>", writer.WrittenXml);
        Assert.Contains("<Writer>Existing Writer</Writer>", writer.WrittenXml);
        Assert.DoesNotContain("Fetched Title", writer.WrittenXml);
        Assert.DoesNotContain("Fetched Writer", writer.WrittenXml);
    }

    [Fact]
    public async Task Pipeline_WriteFailure_ReportsErrorAndNoMetadataApplied()
    {
        var writer = new StubArchiveWriter { WriteOk = false, WriteError = "disk full" };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbz",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.False(attempt.Success);
        Assert.False(attempt.MetadataApplied);
        Assert.Equal("disk full", attempt.ErrorMessage);
    }

    [Fact]
    public async Task Pipeline_CbrWithoutRar_MapsToRarToolMissingError()
    {
        var writer = new StubArchiveWriter
        {
            WriteOk = false,
            WriteError = ArchiveComicInfoWriter.RarToolUnavailableError
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.False(attempt.Success);
        Assert.Equal(ComicMetadataUpdater.RarToolMissingError, attempt.ErrorMessage);
    }

    [Fact]
    public async Task Pipeline_CancelledBeforeStart_ReturnsCancelledAttemptWithoutWriting()
    {
        var writer = new StubArchiveWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbz",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            cts.Token);

        var attempt = Assert.Single(attempts);
        Assert.False(attempt.Success);
        Assert.Equal("Cancelled", attempt.ErrorMessage);
        Assert.Null(writer.LastPath);
    }

    [Fact]
    public async Task Pipeline_MetadataPageCountUsedWhenArchiveHasNoCountablePages()
    {
        var writer = new StubArchiveWriter { Pages = 0 };

        await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T", ["PageCount"] = "24" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        Assert.Contains("<PageCount>24</PageCount>", writer.WrittenXml);
    }

    [Fact]
    public async Task Pipeline_NoPagesAndNoMetaPageCount_OmitsPageCountTag()
    {
        var writer = new StubArchiveWriter { Pages = 0 };

        await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        Assert.DoesNotContain("<PageCount>", writer.WrittenXml);
    }
}
