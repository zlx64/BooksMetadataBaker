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
        // CBR→CBZ conversion outcome (default: fails, so the pipeline falls back to
        // the graceful-skip RarToolMissingError).
        public bool ConvertOk { get; init; }
        public string? ConvertNewPath { get; init; }
        public string? ConvertError { get; init; }
        // Multi-volume split outcome (default: listing yields no entries, so no split).
        public IReadOnlyList<string> EntryNames { get; init; } = Array.Empty<string>();
        public bool ListOk { get; init; } = true;
        public string? ListError { get; init; }
        public bool SplitOk { get; init; }
        public IReadOnlyList<string> SplitNewPaths { get; init; } = Array.Empty<string>();
        public string? SplitError { get; init; }
        public string? LastPath { get; private set; }
        public string? LastConvertPath { get; private set; }
        public string? WrittenXml { get; private set; }
        public IReadOnlyList<string>? LastSplitXmls { get; private set; }
        public Func<string, string>? LastSplitNamer { get; private set; }

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

        public Task<(bool Ok, string? NewPath, string? Error)> ConvertToCbzAsync(string path, string xml, CancellationToken ct)
        {
            LastConvertPath = path;
            return Task.FromResult((ConvertOk, ConvertNewPath, ConvertError));
        }

        public Task<(bool Ok, IReadOnlyList<string> Names, string? Error)> ListEntriesAsync(string path, CancellationToken ct) =>
            Task.FromResult((ListOk, EntryNames, ListError));

        public Task<(bool Ok, IReadOnlyList<string> NewPaths, string? Error)> SplitToCbzAsync(
            string path, MultiVolumeSplitPlan plan, IReadOnlyList<string> xmls,
            string targetDir, Func<string, string> fileNameForVolume, CancellationToken ct)
        {
            LastSplitXmls = xmls;
            LastSplitNamer = fileNameForVolume;
            return Task.FromResult((SplitOk, SplitNewPaths, SplitError));
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
    public async Task Pipeline_CbrWithoutRar_ConvertsToCbzAndReportsNewPath()
    {
        var writer = new StubArchiveWriter
        {
            WriteOk = false,
            WriteError = ArchiveComicInfoWriter.RarToolUnavailableError,
            ConvertOk = true,
            ConvertNewPath = @"C:\library\Book.cbz"
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.True(attempt.Success);
        Assert.True(attempt.MetadataApplied);
        Assert.Null(attempt.ErrorMessage);
        Assert.Equal(@"C:\library\Book.cbz", attempt.FilePath);
        Assert.Equal(@"C:\library\Book.cbr", writer.LastConvertPath);
    }

    [Fact]
    public async Task Pipeline_Cb7WriteFails_ConvertsToCbzAndReportsNewPath()
    {
        // Any in-place write failure (not just CBR) now triggers the convert-to-CBZ fallback.
        var writer = new StubArchiveWriter
        {
            WriteOk = false,
            WriteError = "7-Zip in-place update failed",
            ConvertOk = true,
            ConvertNewPath = @"C:\library\Book.cbz"
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cb7",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.True(attempt.Success);
        Assert.True(attempt.MetadataApplied);
        Assert.Null(attempt.ErrorMessage);
        Assert.Equal(@"C:\library\Book.cbz", attempt.FilePath);
        Assert.Equal(@"C:\library\Book.cb7", writer.LastConvertPath);
    }

    [Fact]
    public async Task Pipeline_WriteFailsAndConversionFails_KeepsOriginalError()
    {
        var writer = new StubArchiveWriter
        {
            WriteOk = false,
            WriteError = "disk full",
            ConvertOk = false,
            ConvertError = "extract failed"
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbt",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Comic,
            CancellationToken.None);

        var attempt = Assert.Single(attempts);
        Assert.False(attempt.Success);
        Assert.False(attempt.MetadataApplied);
        Assert.Equal("disk full", attempt.ErrorMessage);
        Assert.Equal(@"C:\library\Book.cbt", writer.LastConvertPath);
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

    // --- Multi-volume split ------------------------------------------------------

    private static IReadOnlyList<string> MultiVolumeEntries => new[]
    {
        "Series v1/001.jpg", "Series v1/002.jpg",
        "Series v2/001.jpg",
    };

    [Fact]
    public async Task Pipeline_MultiVolumeArchive_SplitsWithPerVolumeComicInfo()
    {
        var writer = new StubArchiveWriter
        {
            EntryNames = MultiVolumeEntries,
            SplitOk = true,
            SplitNewPaths = new[] { @"C:\library\Series - Volume 1.cbz", @"C:\library\Series - Volume 2.cbz" }
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Series - Volume 1-5.cbr",
            new Dictionary<string, string> { ["Title"] = "Test Series", ["Publisher"] = "Acme" },
            "Fallback Title",
            Parsed(volume: "1-5"),
            BookType.Manga,
            CancellationToken.None,
            vol => $"Series - Volume {vol}.cbz");

        var attempt = Assert.Single(attempts);
        Assert.True(attempt.Success);
        Assert.True(attempt.MetadataApplied);
        Assert.Equal(@"C:\library\Series - Volume 1.cbz", attempt.FilePath);
        Assert.Equal([@"C:\library\Series - Volume 2.cbz"], attempt.AdditionalFilePaths);
        // The single-archive write path must not have run.
        Assert.Null(writer.WrittenXml);
        Assert.Null(writer.LastPath);

        Assert.Equal(2, writer.LastSplitXmls!.Count);
        var xmls = writer.LastSplitXmls;
        Assert.Contains("<Volume>1</Volume>", xmls[0]);
        Assert.Contains("<PageCount>2</PageCount>", xmls[0]);
        Assert.Contains("<Volume>2</Volume>", xmls[1]);
        Assert.Contains("<PageCount>1</PageCount>", xmls[1]);
        // Shared metadata is merged into every part.
        Assert.Contains("<Series>Test Series</Series>", xmls[0]);
        Assert.Contains("<Publisher>Acme</Publisher>", xmls[1]);
    }

    [Fact]
    public async Task Pipeline_SplitNamerNotProvided_KeepsSingleArchivePath()
    {
        var writer = new StubArchiveWriter
        {
            EntryNames = MultiVolumeEntries,
            SplitOk = true,
            SplitNewPaths = new[] { @"C:\library\A.cbz" }
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Manga,
            CancellationToken.None);

        Assert.True(Assert.Single(attempts).Success);
        Assert.NotNull(writer.WrittenXml);
        Assert.Null(writer.LastSplitXmls);
    }

    [Fact]
    public async Task Pipeline_EntriesNotMultiVolume_KeepsSingleArchivePath()
    {
        var writer = new StubArchiveWriter
        {
            EntryNames = new[] { "Book/001.jpg", "Book/002.jpg" },
            SplitOk = true
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Manga,
            CancellationToken.None,
            vol => $"Vol {vol}.cbz");

        Assert.True(Assert.Single(attempts).Success);
        Assert.NotNull(writer.WrittenXml);
        Assert.Null(writer.LastSplitXmls);
    }

    [Fact]
    public async Task Pipeline_SplitFails_FallsBackToSingleArchiveWrite()
    {
        var writer = new StubArchiveWriter
        {
            EntryNames = MultiVolumeEntries,
            SplitOk = false,
            SplitError = "disk full"
        };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Manga,
            CancellationToken.None,
            vol => $"Vol {vol}.cbz");

        Assert.True(Assert.Single(attempts).Success);
        Assert.NotNull(writer.WrittenXml);
        Assert.NotNull(writer.LastPath);
    }

    [Fact]
    public async Task Pipeline_EntryListingFails_FallsBackToSingleArchiveWrite()
    {
        var writer = new StubArchiveWriter { ListOk = false, ListError = "7-Zip not available" };

        var attempts = await Create(writer).RunPipelineAsync(
            @"C:\library\Book.cbr",
            new Dictionary<string, string> { ["Title"] = "T" },
            "Fallback",
            Parsed(),
            BookType.Manga,
            CancellationToken.None,
            vol => $"Vol {vol}.cbz");

        Assert.True(Assert.Single(attempts).Success);
        Assert.NotNull(writer.WrittenXml);
        Assert.Null(writer.LastSplitXmls);
    }
}
