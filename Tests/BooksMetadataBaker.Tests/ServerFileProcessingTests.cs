using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services;
using BooksMetadataBaker.Services.Abstract;
using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ServerFileProcessingTests
{
    private sealed class FixedMetadataService : IAggregatedMetadataService
    {
        private readonly Dictionary<string, string> data;
        public FixedMetadataService(Dictionary<string, string> data) => this.data = data;
        public Task<Dictionary<string, string>> FetchMetadataAsync(string title, BookType type, string? volumeToken = null, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class RecordingEBookUpdater : IEBookMetadataUpdater
    {
        public int PipelineCalls;
        public Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, CancellationToken ct)
        {
            PipelineCalls++;
            return Task.FromResult<IReadOnlyList<EBookMetadataAttemptResult>>(
                [new EBookMetadataAttemptResult(filePath, EBookMetadataAttemptStage.Direct, true, null, false, false, [])]);
        }
        public void WriteSidecarSummary(string filePath, IDictionary<string, string> metadata, string fallbackTitle, bool success, string? errors, bool metadataApplied, bool ghostscriptRan)
        {
        }
    }

    private sealed class StubComicUpdater : IComicMetadataUpdater
    {
        private readonly IReadOnlyList<EBookMetadataAttemptResult> attempts;
        public StubComicUpdater(params EBookMetadataAttemptResult[] attempts) => this.attempts = attempts;
        public Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, ParsedComicFilename parsed, BookType type, CancellationToken ct, Func<string, string>? splitFileNameForVolume = null)
            => Task.FromResult(attempts);
    }

    private sealed class RecordingKavitaWriter : IKavitaMetadataWriter
    {
        public int Writes;
        public Task WriteAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }

    private static UploadProcessingService CreateService(
        string root,
        IEBookMetadataUpdater ebook,
        IComicMetadataUpdater comic,
        IKavitaMetadataWriter kavita)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PdfLibrary:RootFolder"] = root })
            .Build();
        return new UploadProcessingService(
            config,
            new FixedMetadataService(new Dictionary<string, string> { ["Title"] = "T" }),
            ebook, comic, kavita,
            NullLogger<UploadProcessingService>.Instance);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"srvfile_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static readonly byte[] Content = [1, 2, 3];

    private static EBookMetadataAttemptResult OkComicAttempt() =>
        new("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, []);

    [Fact]
    public async Task ProcessServerFile_Copy_OrganizesFileAndKeepsSource()
    {
        var root = CreateTempDir();
        var incoming = CreateTempDir();
        try
        {
            var src = Path.Combine(incoming, "My Series vol01.cbz");
            File.WriteAllBytes(src, Content);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(OkComicAttempt()), new RecordingKavitaWriter());

            var (result, _, cancelled, error) = await svc.ProcessServerFileAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, src, moveOriginals: false, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            var dest = Path.Combine(root, "Manga", "My Series", "My Series - Volume 1.cbz");
            Assert.True(File.Exists(dest));
            Assert.Equal(Content, File.ReadAllBytes(dest));
            Assert.True(File.Exists(src)); // copy keeps the source
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(incoming, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerFile_Move_OrganizesFileAndDeletesSource()
    {
        var root = CreateTempDir();
        var incoming = CreateTempDir();
        try
        {
            var src = Path.Combine(incoming, "My Series vol01.cbz");
            File.WriteAllBytes(src, Content);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(OkComicAttempt()), new RecordingKavitaWriter());

            var (result, _, cancelled, error) = await svc.ProcessServerFileAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, src, moveOriginals: true, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            var dest = Path.Combine(root, "Manga", "My Series", "My Series - Volume 1.cbz");
            Assert.True(File.Exists(dest));
            Assert.Equal(Content, File.ReadAllBytes(dest));
            Assert.False(File.Exists(src)); // move removes the source
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(incoming, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerFile_Move_OverwritesExistingDestination()
    {
        var root = CreateTempDir();
        var incoming = CreateTempDir();
        try
        {
            var dest = Path.Combine(root, "Manga", "My Series", "My Series - Volume 1.cbz");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, [9, 9, 9]);
            var src = Path.Combine(incoming, "My Series vol01.cbz");
            File.WriteAllBytes(src, Content);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(OkComicAttempt()), new RecordingKavitaWriter());

            var (result, _, _, error) = await svc.ProcessServerFileAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, src, moveOriginals: true, CancellationToken.None);

            Assert.Null(error);
            Assert.True(result.Success);
            Assert.Equal(Content, File.ReadAllBytes(dest));
            Assert.False(File.Exists(src));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(incoming, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerFile_InPlace_SucceedsWithoutCopy()
    {
        // The source already sits at the organized save path (re-processing):
        // no copy is performed and the file is left intact.
        var root = CreateTempDir();
        try
        {
            var src = Path.Combine(root, "Manga", "My Series", "My Series - Volume 1.cbz");
            Directory.CreateDirectory(Path.GetDirectoryName(src)!);
            File.WriteAllBytes(src, [7, 7, 7]);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(OkComicAttempt()), new RecordingKavitaWriter());

            var (result, _, cancelled, error) = await svc.ProcessServerFileAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, src, moveOriginals: true, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.Equal([7, 7, 7], File.ReadAllBytes(src));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerFile_MissingSource_ReturnsError()
    {
        var root = CreateTempDir();
        var incoming = CreateTempDir();
        try
        {
            var missing = Path.Combine(incoming, "gone.cbz");
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(OkComicAttempt()), new RecordingKavitaWriter());

            var (result, _, cancelled, error) = await svc.ProcessServerFileAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, missing, moveOriginals: false, CancellationToken.None);

            Assert.Equal("File not found", error);
            Assert.False(cancelled);
            Assert.False(result.Success);
            Assert.Equal("File not found", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(incoming, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerFile_Pdf_UsesEbookPipeline()
    {
        var root = CreateTempDir();
        var incoming = CreateTempDir();
        try
        {
            var src = Path.Combine(incoming, "My Book.pdf");
            File.WriteAllBytes(src, Content);
            var ebook = new RecordingEBookUpdater();
            var kavita = new RecordingKavitaWriter();
            var svc = CreateService(root, ebook, new StubComicUpdater(), kavita);

            var (result, _, cancelled, error) = await svc.ProcessServerFileAsync(
                new UploadRequest { Title = "My Book", Type = BookType.Book }, src, moveOriginals: false, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.Equal(1, ebook.PipelineCalls);
            Assert.Equal(1, kavita.Writes);
            // Ebooks are saved inside the title folder: <root>/<TypeFolder>/<Title>/<Title>.pdf
            Assert.True(File.Exists(Path.Combine(root, "Novel", "My Book", "My Book.pdf")));
            Assert.True(File.Exists(src));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(incoming, recursive: true);
        }
    }
}
