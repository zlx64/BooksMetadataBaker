using System.IO.Compression;
using System.Text.Json;
using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services;
using BooksMetadataBaker.Services.Abstract;
using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class UploadProcessingServiceTests
{
    private static string BaseFolder => OperatingSystem.IsWindows() ? @"C:\library" : "/library";

    private sealed class FakeFormFile : IFormFile
    {
        private readonly MemoryStream stream;
        public FakeFormFile(string fileName, byte[] content)
        {
            FileName = fileName;
            stream = new MemoryStream(content);
        }
        public string ContentType => "application/octet-stream";
        public string ContentDisposition => null!;
        public IHeaderDictionary Headers => null!;
        public long Length => stream.Length;
        public string Name => "file";
        public string FileName { get; }
        public Stream OpenReadStream() => stream;
        public void CopyTo(Stream target) => OpenReadStream().CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            OpenReadStream().CopyToAsync(target, cancellationToken);
    }

    private sealed class FakeFormFileCollection : List<IFormFile>, IFormFileCollection
    {
        public FakeFormFileCollection(IEnumerable<IFormFile> files) : base(files)
        {
        }

        public string ContentType => string.Join(",", this.Select(f => f.ContentType));
        public string ContentDisposition => string.Join(",", this.Select(f => f.ContentDisposition));
        public IHeaderDictionary Headers => null!;
        public long Length => this.Sum(f => f.Length);
        public string Name => string.Join(",", this.Select(f => f.Name));
        public string FileName => string.Join(",", this.Select(f => f.FileName));
        public Stream OpenReadStream() => throw new NotSupportedException();
        public void CopyTo(Stream target) => throw new NotSupportedException();
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        IFormFile? IFormFileCollection.this[string name] =>
            this.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        IFormFile? IFormFileCollection.GetFile(string name) => ((IFormFileCollection)this)[name];

        IReadOnlyList<IFormFile> IFormFileCollection.GetFiles(string name) =>
            this.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

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
        public List<string> SidecarPaths { get; } = [];
        public Task<IReadOnlyList<EBookMetadataAttemptResult>> RunPipelineAsync(string filePath, IDictionary<string, string> metadata, string fallbackTitle, CancellationToken ct)
        {
            PipelineCalls++;
            IReadOnlyList<EBookMetadataAttemptResult> none = Array.Empty<EBookMetadataAttemptResult>();
            return Task.FromResult(none);
        }
        public void WriteSidecarSummary(string filePath, IDictionary<string, string> metadata, string fallbackTitle, bool success, string? errors, bool metadataApplied, bool ghostscriptRan)
            => SidecarPaths.Add(filePath);
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
        IKavitaMetadataWriter kavita,
        Dictionary<string, string> meta)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PdfLibrary:RootFolder"] = root })
            .Build();
        return new UploadProcessingService(
            config, new FixedMetadataService(meta), ebook, comic, kavita,
            NullLogger<UploadProcessingService>.Instance);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"upl_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveTitleFolder_AcceptsNormalTitles()
    {
        var result = UploadProcessingService.ResolveTitleFolder(BaseFolder, "My Book", out var error);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.StartsWith(Path.GetFullPath(BaseFolder) + Path.DirectorySeparatorChar, result, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("My Book", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    public void ResolveTitleFolder_RejectsDotTraversal(string title)
    {
        // Regression: Sanitize only stripped invalid filename chars, so ".." escaped
        // the library folder on both Windows and Linux.
        var result = UploadProcessingService.ResolveTitleFolder(BaseFolder, title, out var error);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("a/../../evil")]
    [InlineData("../library-evil")]
    public void ResolveTitleFolder_SlashTraversalNeverEscapesBaseFolder(string title)
    {
        // Sanitize neutralizes '/' on every platform; the containment check in
        // ResolveTitleFolder is the backstop that keeps the result inside the
        // base folder either way.
        var result = UploadProcessingService.ResolveTitleFolder(BaseFolder, title, out _);

        if (result is not null)
            Assert.StartsWith(Path.GetFullPath(BaseFolder) + Path.DirectorySeparatorChar, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_ReplacesInvalidCharacters()
    {
        var sanitized = UploadProcessingService.Sanitize("a<b>c:d\"e/f");
        Assert.DoesNotContain('/', sanitized);
        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain('<', sanitized);
            Assert.DoesNotContain('>', sanitized);
            Assert.DoesNotContain(':', sanitized);
            Assert.DoesNotContain('"', sanitized);
        }
        else
        {
            // On Linux Path.GetInvalidFileNameChars() is only '/' and NUL, so
            // Windows-specific chars are preserved in titles (pre-refactor behavior).
            Assert.Contains(':', sanitized);
            Assert.Contains('<', sanitized);
        }
    }

    [Fact]
    public void GetArchiveSavePath_VolumeUsesVolumeName()
    {
        var folder = Path.Combine(BaseFolder, "My Series");
        var parsed = new ParsedComicFilename("1", null, false, null, null);

        var path = UploadProcessingService.GetArchiveSavePath(folder, "My Series", ".cbz", parsed, true);

        Assert.Equal(Path.Combine(folder, "My Series - Volume 1.cbz"), path);
    }

    [Fact]
    public void GetArchiveSavePath_ChapterWithoutVolumeUsesChapterName()
    {
        var folder = Path.Combine(BaseFolder, "My Series");
        var parsed = new ParsedComicFilename(null, "53", false, null, null);

        var path = UploadProcessingService.GetArchiveSavePath(folder, "My Series", ".cbz", parsed, true);

        Assert.Equal(Path.Combine(folder, "My Series - Chapter 53.cbz"), path);
    }

    [Fact]
    public void GetArchiveSavePath_VolumeBeatsChapter()
    {
        var folder = Path.Combine(BaseFolder, "My Series");
        var parsed = new ParsedComicFilename("1", "2", false, null, null);

        var path = UploadProcessingService.GetArchiveSavePath(folder, "My Series", ".cbz", parsed, true);

        Assert.Equal(Path.Combine(folder, "My Series - Volume 1.cbz"), path);
    }

    [Fact]
    public void GetArchiveSavePath_NormalizesZeroPaddedAndDecimalNumbers()
    {
        var folder = Path.Combine(BaseFolder, "My Series");

        var volume = UploadProcessingService.GetArchiveSavePath(
            folder, "My Series", ".cbz", new ParsedComicFilename("007", null, false, null, null), true);
        var chapter = UploadProcessingService.GetArchiveSavePath(
            folder, "My Series", ".cbz", new ParsedComicFilename(null, "034.5", false, null, null), true);

        Assert.Equal(Path.Combine(folder, "My Series - Volume 7.cbz"), volume);
        Assert.Equal(Path.Combine(folder, "My Series - Chapter 34.5.cbz"), chapter);
    }

    [Fact]
    public void GetArchiveSavePath_SpecialFirstIsSp01AndCreatesSpecialsDir()
    {
        var folder = CreateTempDir();
        try
        {
            var parsed = new ParsedComicFilename(null, null, true, null, null);

            var path = UploadProcessingService.GetArchiveSavePath(folder, "My Series", ".cbz", parsed, true);

            Assert.Equal(Path.Combine(folder, "Specials", "My Series SP01.cbz"), path);
            Assert.True(Directory.Exists(Path.Combine(folder, "Specials")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void GetArchiveSavePath_SpecialNumberIncrementsAcrossTitleFolderAndSpecials()
    {
        // D10: the SP scan covers the title folder and the Specials/ subfolder.
        var folder = CreateTempDir();
        try
        {
            var specials = Path.Combine(folder, "Specials");
            Directory.CreateDirectory(specials);
            File.WriteAllText(Path.Combine(specials, "My Series SP01.cbz"), "");
            File.WriteAllText(Path.Combine(folder, "My Series SP03.cbz"), "");
            var parsed = new ParsedComicFilename(null, null, true, null, null);

            var path = UploadProcessingService.GetArchiveSavePath(folder, "My Series", ".cbz", parsed, true);

            Assert.Equal(Path.Combine(specials, "My Series SP04.cbz"), path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void GetArchiveSavePath_SpecialWithoutSubfolderGoesToTitleFolder()
    {
        var folder = CreateTempDir();
        try
        {
            var parsed = new ParsedComicFilename(null, null, true, null, null);

            var path = UploadProcessingService.GetArchiveSavePath(folder, "My Series", ".cbz", parsed, false);

            Assert.Equal(Path.Combine(folder, "My Series SP01.cbz"), path);
            Assert.False(Directory.Exists(Path.Combine(folder, "Specials")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_Archive_UsesComicPipelineNotCalibre()
    {
        // Risk R5: the format switch is the only dispatch point — archives must
        // never reach the Calibre/ebook-meta pipeline.
        var root = CreateTempDir();
        var ebook = new RecordingEBookUpdater();
        var kavita = new RecordingKavitaWriter();
        var okAttempt = new EBookMetadataAttemptResult("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, []);
        try
        {
            var svc = CreateService(root, ebook, new StubComicUpdater(okAttempt), kavita,
                new Dictionary<string, string> { ["Publisher"] = "Acme", ["PageCount"] = "24" });
            var file = new FakeFormFile("My Series vol01.cbz", [1, 2, 3]);

            var (result, _, cancelled, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.True(result.ComicInfoWritten);
            Assert.Equal(24, result.PageCount);
            Assert.Equal(EBookFormat.Cbz, result.Format);
            Assert.Equal(0, ebook.PipelineCalls);
            Assert.Equal(1, kavita.Writes);
            Assert.True(File.Exists(Path.Combine(root, "Manga", "My Series", "My Series - Volume 1.cbz")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_CbrWithoutRar_IsPartialSuccess()
    {
        var root = CreateTempDir();
        var kavita = new RecordingKavitaWriter();
        var cbrAttempt = new EBookMetadataAttemptResult(
            "x", EBookMetadataAttemptStage.ComicInfo, false, ComicMetadataUpdater.RarToolMissingError, false, false, []);
        try
        {
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(cbrAttempt), kavita,
                new Dictionary<string, string> { ["Title"] = "Book" });
            var file = new FakeFormFile("Book.cbr", [1]);

            var (result, _, _, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "Book", Type = BookType.Comic }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.True(result.Success);
            Assert.False(result.ComicInfoWritten);
            Assert.Equal($"ComicInfo: {ComicMetadataUpdater.RarToolMissingError}", result.ErrorMessage);
            Assert.Equal(1, kavita.Writes);
            // "Book.cbr" has no volume/chapter marker -> Special in the Specials/ subfolder.
            Assert.True(File.Exists(Path.Combine(root, "Comic", "Book", "Specials", "Book SP01.cbr")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_CbrConvertedToCbz_UsesNewPathAndFormat()
    {
        var root = CreateTempDir();
        var kavita = new RecordingKavitaWriter();
        // The pipeline converted the .cbr to a .cbz; it reports the new path.
        var cbzPath = Path.Combine(root, "Comic", "Book", "Specials", "Book SP01.cbz");
        var convertedAttempt = new EBookMetadataAttemptResult(
            cbzPath, EBookMetadataAttemptStage.ComicInfo, true, null, false, true, []);
        try
        {
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(convertedAttempt), kavita,
                new Dictionary<string, string> { ["Title"] = "Book" });
            var file = new FakeFormFile("Book.cbr", [1]);

            var (result, _, _, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "Book", Type = BookType.Comic }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.True(result.Success);
            Assert.True(result.ComicInfoWritten);
            Assert.Equal("Book SP01.cbz", result.File);
            Assert.Equal(EBookFormat.Cbz, result.Format);
            Assert.Equal(1, kavita.Writes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_SplitArchive_ReportsAllFilesAndSidecars()
    {
        var root = CreateTempDir();
        var kavita = new RecordingKavitaWriter();
        var dir = Path.Combine(root, "Manga", "My Series");
        var p1 = Path.Combine(dir, "My Series - Volume 1.cbz");
        var p2 = Path.Combine(dir, "My Series - Volume 2.cbz");
        var splitAttempt = new EBookMetadataAttemptResult(
            p1, EBookMetadataAttemptStage.ComicInfo, true, null, false, true, [p2]);
        var ebook = new RecordingEBookUpdater();
        try
        {
            var svc = CreateService(root, ebook, new StubComicUpdater(splitAttempt), kavita,
                new Dictionary<string, string> { ["Title"] = "My Series" });
            var file = new FakeFormFile("My Series vol 01-02.cbz", [1, 2, 3]);

            var (result, _, cancelled, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.True(result.ComicInfoWritten);
            Assert.Equal("My Series - Volume 1.cbz", result.File);
            Assert.Equal(["My Series - Volume 1.cbz", "My Series - Volume 2.cbz"], result.SplitFiles);
            Assert.Equal(EBookFormat.Cbz, result.Format);
            Assert.Equal(1, kavita.Writes);
            // Every split archive gets its own sidecar.
            Assert.Equal([p1, p2], ebook.SidecarPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_ArchiveWriteFailure_IsOverallFailure()
    {
        var root = CreateTempDir();
        var kavita = new RecordingKavitaWriter();
        var failAttempt = new EBookMetadataAttemptResult(
            "x", EBookMetadataAttemptStage.ComicInfo, false, "disk full", false, false, []);
        try
        {
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(failAttempt), kavita,
                new Dictionary<string, string> { ["Title"] = "Book" });
            var file = new FakeFormFile("Book.cbz", [1]);

            var (result, _, _, _) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "Book", Type = BookType.Comic }, file, CancellationToken.None);

            Assert.False(result.Success);
            Assert.False(result.ComicInfoWritten);
            Assert.Equal("ComicInfo: disk full", result.ErrorMessage);
            Assert.Equal(0, kavita.Writes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // P9: raw archive containers are saved under their Kavita extension
    // (zip -> cbz, 7z -> cb7, rar -> cbr) and go through the comic pipeline.

    [Fact]
    public async Task ProcessSingle_ZipUpload_IsSavedAsCbz()
    {
        var root = CreateTempDir();
        try
        {
            var okAttempt = new EBookMetadataAttemptResult("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, []);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(okAttempt),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "My Series" });
            var file = new FakeFormFile("My Series vol01.zip", [1, 2, 3]);

            var (result, _, cancelled, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.True(result.ComicInfoWritten);
            Assert.Equal(EBookFormat.Cbz, result.Format);
            Assert.True(File.Exists(Path.Combine(root, "Manga", "My Series", "My Series - Volume 1.cbz")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_7zUpload_IsSavedAsCb7()
    {
        var root = CreateTempDir();
        try
        {
            var okAttempt = new EBookMetadataAttemptResult("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, []);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(okAttempt),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "My Series" });
            var file = new FakeFormFile("My Series ch02.7z", [1, 2, 3]);

            var (result, _, _, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.True(result.Success);
            Assert.Equal(EBookFormat.Cb7, result.Format);
            Assert.True(File.Exists(Path.Combine(root, "Manga", "My Series", "My Series - Chapter 2.cb7")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_RarUpload_IsSavedAsCbrPartialSuccess()
    {
        var root = CreateTempDir();
        try
        {
            var cbrAttempt = new EBookMetadataAttemptResult(
                "x", EBookMetadataAttemptStage.ComicInfo, false, ComicMetadataUpdater.RarToolMissingError, false, false, []);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(cbrAttempt),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "Book" });
            var file = new FakeFormFile("Book.rar", [1]);

            var (result, _, _, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "Book", Type = BookType.Comic }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.True(result.Success);
            Assert.False(result.ComicInfoWritten);
            Assert.Equal(EBookFormat.Cbr, result.Format);
            Assert.True(File.Exists(Path.Combine(root, "Comic", "Book", "Specials", "Book SP01.cbr")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessSingle_TarUpload_IsSavedAsCbt()
    {
        var root = CreateTempDir();
        try
        {
            var okAttempt = new EBookMetadataAttemptResult("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, []);
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(okAttempt),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "My Series" });
            var file = new FakeFormFile("My Series vol01.tar", [1, 2, 3]);

            var (result, _, cancelled, error) = await svc.ProcessSingleAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga }, file, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.True(result.ComicInfoWritten);
            Assert.Equal(EBookFormat.Cbt, result.Format);
            Assert.True(File.Exists(Path.Combine(root, "Manga", "My Series", "My Series - Volume 1.cbt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerImageFolder_CreatesCbzAndUsesComicPipeline()
    {
        var root = CreateTempDir();
        var sourceParent = CreateTempDir();
        var source = Path.Combine(sourceParent, "My Series");
        var ebook = new RecordingEBookUpdater();
        var kavita = new RecordingKavitaWriter();
        var okAttempt = new EBookMetadataAttemptResult("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, []);
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "01.jpg"), "one");
            File.WriteAllText(Path.Combine(source, "02.png"), "two");

            var svc = CreateService(root, ebook, new StubComicUpdater(okAttempt), kavita,
                new Dictionary<string, string> { ["Title"] = "My Series" });

            var (result, _, cancelled, error) = await svc.ProcessServerImageFolderAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga },
                source, moveOriginals: false, CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.True(result.ComicInfoWritten);
            Assert.Equal(EBookFormat.Cbz, result.Format);
            Assert.Equal(0, ebook.PipelineCalls);
            Assert.Equal(1, kavita.Writes);

            var expected = Path.Combine(root, "Manga", "My Series", "Specials", "My Series SP01.cbz");
            Assert.True(File.Exists(expected));
            using var zip = ZipFile.OpenRead(expected);
            Assert.Equal(["01.jpg", "02.png"], zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray());
            Assert.True(File.Exists(Path.Combine(source, "01.jpg")));
            Assert.True(File.Exists(Path.Combine(source, "02.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(sourceParent, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerImageFolder_MoveOriginals_DeletesImagesAndEmptyDirs()
    {
        var root = CreateTempDir();
        var sourceParent = CreateTempDir();
        var source = Path.Combine(sourceParent, "My Series");
        try
        {
            var sub = Path.Combine(source, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(source, "01.jpg"), "one");
            File.WriteAllText(Path.Combine(sub, "02.jpg"), "two");
            File.WriteAllText(Path.Combine(source, "notes.txt"), "keep");

            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(
                new EBookMetadataAttemptResult("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, [])),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "My Series" });

            var (result, _, _, error) = await svc.ProcessServerImageFolderAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga },
                source, moveOriginals: true, CancellationToken.None);

            Assert.Null(error);
            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(root, "Manga", "My Series", "Specials", "My Series SP01.cbz")));
            Assert.False(File.Exists(Path.Combine(source, "01.jpg")));
            Assert.False(File.Exists(Path.Combine(sub, "02.jpg")));
            Assert.False(Directory.Exists(sub));
            Assert.True(File.Exists(Path.Combine(source, "notes.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(sourceParent, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessServerImageFolder_NoImages_ReturnsError()
    {
        var root = CreateTempDir();
        var source = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(source, "notes.txt"), "x");
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "My Series" });

            var (result, _, _, error) = await svc.ProcessServerImageFolderAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga },
                source, moveOriginals: false, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("No image files found in folder", result.ErrorMessage);
            Assert.Equal("No image files found in folder", error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessImageFolderUpload_StagesFilesAndCreatesCbz()
    {
        var root = CreateTempDir();
        try
        {
            var files = new FakeFormFileCollection(new IFormFile[]
            {
                new FakeFormFile("01.jpg", [1]),
                new FakeFormFile("02.jpg", [2])
            });
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(
                new EBookMetadataAttemptResult("x", EBookMetadataAttemptStage.ComicInfo, true, null, false, true, [])),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "My Series" });

            var (result, _, cancelled, error) = await svc.ProcessImageFolderUploadAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga },
                files, "My Series", JsonSerializer.Serialize(new[] { "01.jpg", "sub/02.jpg" }), CancellationToken.None);

            Assert.Null(error);
            Assert.False(cancelled);
            Assert.True(result.Success);
            Assert.True(result.ComicInfoWritten);

            var expected = Path.Combine(root, "Manga", "My Series", "Specials", "My Series SP01.cbz");
            Assert.True(File.Exists(expected));
            using var zip = ZipFile.OpenRead(expected);
            Assert.Equal(["01.jpg", "sub/02.jpg"], zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessImageFolderUpload_NoImages_ReturnsError()
    {
        var root = CreateTempDir();
        try
        {
            var files = new FakeFormFileCollection(new IFormFile[]
            {
                new FakeFormFile("notes.txt", [1])
            });
            var svc = CreateService(root, new RecordingEBookUpdater(), new StubComicUpdater(),
                new RecordingKavitaWriter(), new Dictionary<string, string> { ["Title"] = "My Series" });

            var (result, _, _, error) = await svc.ProcessImageFolderUploadAsync(
                new UploadRequest { Title = "My Series", Type = BookType.Manga },
                files, "My Series", JsonSerializer.Serialize(new[] { "notes.txt" }), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("No image files found in folder", result.ErrorMessage);
            Assert.Equal("No image files found in folder", error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
