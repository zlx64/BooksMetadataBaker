using BooksMetadataBaker.Controllers;
using BooksMetadataBaker.Models;
using BooksMetadataBaker.Services.Abstract;
using BooksMetadataBaker.Services.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class FilesControllerTests
{
    // Non-existent, stable root used for the pure path-resolution tests.
    private static string BaseRoot => OperatingSystem.IsWindows() ? @"C:\srvfiles" : "/srvfiles";

    private sealed class FakeProcessor : IUploadProcessingService
    {
        public UploadRequest? LastRequest;
        public string? LastSourcePath;
        public bool LastMoveOriginals;
        public int Calls;
        public (EBookUploadProcessResult Result, IDictionary<string, string> Metadata, bool Cancelled, string? Error) Next;

        public Task<(EBookUploadProcessResult Result, IDictionary<string, string> Metadata, bool Cancelled, string? Error)> ProcessSingleAsync(UploadRequest info, IFormFile file, CancellationToken ct)
            => Task.FromResult(Next);

        public Task<(EBookUploadProcessResult Result, IDictionary<string, string> Metadata, bool Cancelled, string? Error)> ProcessServerFileAsync(UploadRequest info, string sourcePath, bool moveOriginals, CancellationToken ct)
        {
            Calls++;
            LastRequest = info;
            LastSourcePath = sourcePath;
            LastMoveOriginals = moveOriginals;
            return Task.FromResult(Next);
        }
    }

    private static FakeProcessor SuccessProcessor() => new()
    {
        Next = (
            new EBookUploadProcessResult("Out.cbz", true, null, 1, new Dictionary<string, string>(), false, false, false, EBookFormat.Cbz, true, 2, []),
            new Dictionary<string, string> { ["Title"] = "T" },
            false, null)
    };

    private static FilesController CreateController(FakeProcessor processor, IDictionary<string, string?> configValues)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        return new FilesController(processor, config, NullLogger<FilesController>.Instance);
    }

    private static Dictionary<string, string?> ConfigWith(string root) => new()
    {
        ["ServerFiles:Enabled"] = "true",
        ["ServerFiles:RootFolder"] = root,
        ["MangaComics:Enabled"] = "true"
    };

    // The browse response is an anonymous type — read it reflectively.
    private static object? Prop(object? obj, string name) => obj?.GetType().GetProperty(name)!.GetValue(obj);

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"files_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---------------------------------------------------------------
    // ResolveUnderRoot
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveUnderRoot_NullOrBlank_ReturnsRoot(string? path)
    {
        var result = FilesController.ResolveUnderRoot(BaseRoot, path, out var error);

        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(BaseRoot), result);
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("sub/dir/file.cbz")]
    [InlineData("/sub/dir/file.cbz")]
    [InlineData("  sub  ")]
    public void ResolveUnderRoot_RelativePaths_ResolveUnderRoot(string path)
    {
        var result = FilesController.ResolveUnderRoot(BaseRoot, path, out var error);

        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(Path.Combine(BaseRoot, path.Trim().TrimStart('/'))), result);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../evil")]
    [InlineData("a/../../evil")]
    [InlineData("a\\..\\..\\evil")]
    [InlineData("../srvfiles-evil")]
    public void ResolveUnderRoot_Traversal_IsRejected(string path)
    {
        var result = FilesController.ResolveUnderRoot(BaseRoot, path, out var error);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void ResolveUnderRoot_AbsolutePath_IsRejected()
    {
        var abs = Path.GetTempPath(); // absolute on both platforms

        var result = FilesController.ResolveUnderRoot(BaseRoot, abs, out var error);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void ResolveUnderRoot_NulByte_IsRejected()
    {
        var result = FilesController.ResolveUnderRoot(BaseRoot, "a\0b", out var error);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ---------------------------------------------------------------
    // ListDirectory
    // ---------------------------------------------------------------

    [Fact]
    public void ListDirectory_DirsFirstThenFiles_HidesDotfiles_MarksSelectable()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "zdir"));
            Directory.CreateDirectory(Path.Combine(root, "adir"));
            File.WriteAllText(Path.Combine(root, "b.txt"), "x");
            File.WriteAllText(Path.Combine(root, "A.pdf"), "x");
            File.WriteAllText(Path.Combine(root, "x.cbz"), "x");
            File.WriteAllText(Path.Combine(root, ".hidden"), "x");

            var entries = FilesController.ListDirectory(root, root, [".pdf", ".cbz"]);

            Assert.Equal(["adir", "zdir", "A.pdf", "b.txt", "x.cbz"], entries.Select(e => e.Name).ToArray());
            Assert.All(entries.Where(e => e.IsDir), e => Assert.False(e.Selectable));
            Assert.True(entries.Single(e => e.Name == "A.pdf").Selectable);
            Assert.True(entries.Single(e => e.Name == "x.cbz").Selectable);
            Assert.False(entries.Single(e => e.Name == "b.txt").Selectable);
            Assert.Equal("b.txt", entries.Single(e => e.Name == "b.txt").Path);
            Assert.Equal("adir", entries[0].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListDirectory_SubdirPathsAreRelativeToRootWithForwardSlashes()
    {
        var root = CreateTempDir();
        try
        {
            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "inner.cbz"), "x");

            var entries = FilesController.ListDirectory(root, sub, [".cbz"]);

            var entry = Assert.Single(entries);
            Assert.Equal("inner.cbz", entry.Name);
            Assert.Equal("sub/inner.cbz", entry.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------
    // Browse (GET /api/files)
    // ---------------------------------------------------------------

    [Fact]
    public void Browse_Root_ReturnsEntriesWithNullParent()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a vol01.cbz"), "x");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, ".hidden"), "x");

            var controller = CreateController(SuccessProcessor(), ConfigWith(root));

            var result = controller.Browse(null);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("", Prop(ok.Value, "path"));
            Assert.Null(Prop(ok.Value, "parent"));
            var entries = (List<ServerFileEntry>)Prop(ok.Value, "entries")!;
            Assert.Equal(["sub", "a vol01.cbz", "notes.txt"], entries.Select(e => e.Name).ToArray());
            Assert.True(entries.Single(e => e.Name == "a vol01.cbz").Selectable);
            Assert.False(entries.Single(e => e.Name == "notes.txt").Selectable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Browse_Subdir_ParentIsRelativePath()
    {
        var root = CreateTempDir();
        try
        {
            var deep = Path.Combine(root, "sub", "deep");
            Directory.CreateDirectory(deep);

            var controller = CreateController(SuccessProcessor(), ConfigWith(root));

            var sub = Assert.IsType<OkObjectResult>(controller.Browse("sub"));
            Assert.Equal("sub", Prop(sub.Value, "path"));
            Assert.Equal("", Prop(sub.Value, "parent"));

            var deepResult = Assert.IsType<OkObjectResult>(controller.Browse("sub/deep"));
            Assert.Equal("sub/deep", Prop(deepResult.Value, "path"));
            Assert.Equal("sub", Prop(deepResult.Value, "parent"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Browse_FallsBackToLibraryRoot_WhenServerRootEmpty()
    {
        var root = CreateTempDir();
        try
        {
            var controller = CreateController(SuccessProcessor(), new Dictionary<string, string?>
            {
                ["ServerFiles:Enabled"] = "true",
                ["ServerFiles:RootFolder"] = "",
                ["PdfLibrary:RootFolder"] = root,
                ["MangaComics:Enabled"] = "true"
            });

            var result = controller.Browse(null);

            Assert.IsType<OkObjectResult>(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Browse_Disabled_Returns404()
    {
        var root = CreateTempDir();
        try
        {
            var controller = CreateController(SuccessProcessor(), new Dictionary<string, string?>
            {
                ["ServerFiles:Enabled"] = "false",
                ["ServerFiles:RootFolder"] = root
            });

            var result = controller.Browse(null);

            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Browse_RootNotConfigured_Returns404()
    {
        var controller = CreateController(SuccessProcessor(), new Dictionary<string, string?>
        {
            ["ServerFiles:Enabled"] = "true"
        });

        var result = controller.Browse(null);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void Browse_RootMissing_Returns404()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}");
        var controller = CreateController(SuccessProcessor(), ConfigWith(missing));

        var result = controller.Browse(null);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void Browse_TraversalPath_Returns400()
    {
        var root = CreateTempDir();
        try
        {
            var controller = CreateController(SuccessProcessor(), ConfigWith(root));

            var result = controller.Browse("..");

            Assert.IsType<BadRequestObjectResult>(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Browse_MissingFolder_Returns404()
    {
        var root = CreateTempDir();
        try
        {
            var controller = CreateController(SuccessProcessor(), ConfigWith(root));

            var result = controller.Browse("missing");

            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------
    // Process (POST /api/files/process)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Process_Success_CallsProcessorWithFullPathAndReturnsShape()
    {
        var root = CreateTempDir();
        try
        {
            var src = Path.Combine(root, "My Series vol01.cbz");
            File.WriteAllText(src, "x");
            var processor = SuccessProcessor();
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "My Series", Type = BookType.Manga, Path = "My Series vol01.cbz" },
                CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, processor.Calls);
            Assert.Equal(src, processor.LastSourcePath);
            Assert.False(processor.LastMoveOriginals);
            Assert.Equal("My Series", processor.LastRequest!.Title);
            Assert.Equal(BookType.Manga, processor.LastRequest.Type);

            var files = (EBookUploadProcessResult[])Prop(ok.Value, "Files")!;
            var file = Assert.Single(files);
            Assert.Equal("Out.cbz", file.File);
            Assert.True(file.Success);
            Assert.False((bool)Prop(ok.Value, "Cancelled")!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_MoveOriginals_IsPassedThrough()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "f.cbz"), "x");
            var processor = SuccessProcessor();
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Manga, Path = "f.cbz", MoveOriginals = true },
                CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.True(processor.LastMoveOriginals);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_MissingTitle_Returns400()
    {
        var root = CreateTempDir();
        try
        {
            var processor = SuccessProcessor();
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "  ", Type = BookType.Manga, Path = "f.cbz" },
                CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, processor.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_InvalidExtension_Returns400()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
            var processor = SuccessProcessor();
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Manga, Path = "notes.txt" },
                CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, processor.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_FileNotFound_Returns404()
    {
        var root = CreateTempDir();
        try
        {
            var processor = SuccessProcessor();
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Manga, Path = "missing.cbz" },
                CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal(0, processor.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_TraversalPath_Returns400()
    {
        var root = CreateTempDir();
        try
        {
            var processor = SuccessProcessor();
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Manga, Path = "../outside.cbz" },
                CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, processor.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_MangaDisabled_ArchiveRejected()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "f.cbz"), "x");
            var config = ConfigWith(root);
            config["MangaComics:Enabled"] = "false";
            var processor = SuccessProcessor();
            var controller = CreateController(processor, config);

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Manga, Path = "f.cbz" },
                CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, processor.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_Disabled_Returns404()
    {
        var root = CreateTempDir();
        try
        {
            var config = ConfigWith(root);
            config["ServerFiles:Enabled"] = "false";
            var processor = SuccessProcessor();
            var controller = CreateController(processor, config);

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Manga, Path = "f.cbz" },
                CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_ProcessorErrorWithEmptyFile_Returns500()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "f.cbz"), "x");
            var processor = new FakeProcessor
            {
                Next = (
                    new EBookUploadProcessResult("", false, "boom", 0, new Dictionary<string, string>(), false, false, false, EBookFormat.Cbz, false, 0, []),
                    new Dictionary<string, string>(),
                    false, "boom")
            };
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Manga, Path = "f.cbz" },
                CancellationToken.None);

            // StatusCode(500, value) yields an ObjectResult with the status set.
            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, obj.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Process_PartialSuccess_StillReturns200()
    {
        // A non-empty File with an error is a partial success (e.g. CBR without a
        // RAR tool) — the file was saved and organized, so the client sees 200.
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "f.cbr"), "x");
            var processor = new FakeProcessor
            {
                Next = (
                    new EBookUploadProcessResult("f.cbr", true, "ComicInfo: skipped", 1, new Dictionary<string, string>(), false, false, false, EBookFormat.Cbr, false, 0, []),
                    new Dictionary<string, string>(),
                    false, "ComicInfo: skipped")
            };
            var controller = CreateController(processor, ConfigWith(root));

            var result = await controller.Process(
                new ServerFileRequest { Title = "T", Type = BookType.Comic, Path = "f.cbr" },
                CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
