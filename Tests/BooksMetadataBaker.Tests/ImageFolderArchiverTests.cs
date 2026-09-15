using System.IO.Compression;
using BooksMetadataBaker.Services.Comic;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class ImageFolderArchiverTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"imgfolder_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void HasDirectImageFiles_OnlyCountsFilesDirectlyInsideFolder()
    {
        var root = CreateTempDir();
        try
        {
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "01.jpg"), "x");

            Assert.False(ImageFolderArchiver.HasDirectImageFiles(root));

            File.WriteAllText(Path.Combine(root, "02.png"), "x");
            Assert.True(ImageFolderArchiver.HasDirectImageFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetImageFiles_IsRecursive_NaturallySorted_ExcludesHidden()
    {
        var root = CreateTempDir();
        try
        {
            var sub = Path.Combine(root, "sub");
            var hidden = Path.Combine(root, ".hidden");
            Directory.CreateDirectory(sub);
            Directory.CreateDirectory(hidden);

            File.WriteAllText(Path.Combine(root, "10.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "2.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "1.jpg"), "x");
            File.WriteAllText(Path.Combine(sub, "10.jpg"), "x");
            File.WriteAllText(Path.Combine(sub, "2.jpg"), "x");
            File.WriteAllText(Path.Combine(hidden, "1.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "x");

            var files = ImageFolderArchiver.GetImageFiles(root);

            Assert.Equal(
                ["1.jpg", "2.jpg", "10.jpg", "sub/2.jpg", "sub/10.jpg"],
                files.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCbzFromDirectory_CreatesZipWithRelativePaths()
    {
        var source = CreateTempDir();
        var destDir = CreateTempDir();
        try
        {
            var sub = Path.Combine(source, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(source, "01.jpg"), "one");
            File.WriteAllText(Path.Combine(sub, "02.jpg"), "two");

            var dest = Path.Combine(destDir, "out.cbz");
            var count = await ImageFolderArchiver.CreateCbzFromDirectoryAsync(source, dest);

            Assert.Equal(2, count);
            using var zip = ZipFile.OpenRead(dest);
            var entries = zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
            Assert.Equal(["01.jpg", "sub/02.jpg"], entries);

            var first = zip.Entries.Single(e => e.FullName == "01.jpg");
            await using var stream = first.Open();
            using var reader = new StreamReader(stream);
            Assert.Equal("one", await reader.ReadToEndAsync());
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCbzFromDirectory_ExplicitPathsOnlyIncludesThosePaths()
    {
        var source = CreateTempDir();
        var destDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(source, "01.jpg"), "one");
            File.WriteAllText(Path.Combine(source, "02.jpg"), "two");

            var dest = Path.Combine(destDir, "out.cbz");
            var count = await ImageFolderArchiver.CreateCbzFromDirectoryAsync(
                source, dest, ["02.jpg"]);

            Assert.Equal(1, count);
            using var zip = ZipFile.OpenRead(dest);
            Assert.Equal(["02.jpg"], zip.Entries.Select(e => e.FullName).ToArray());
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCbzFromDirectory_EscapingPath_Throws()
    {
        var source = CreateTempDir();
        var destDir = CreateTempDir();
        try
        {
            var dest = Path.Combine(destDir, "out.cbz");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ImageFolderArchiver.CreateCbzFromDirectoryAsync(source, dest, ["../evil.jpg"]));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public void DeleteSourceFiles_DeletesImagesAndEmptyDirs_ProtectsDestination()
    {
        var source = CreateTempDir();
        try
        {
            var sub = Path.Combine(source, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(source, "01.jpg"), "one");
            File.WriteAllText(Path.Combine(sub, "02.jpg"), "two");
            File.WriteAllText(Path.Combine(source, "notes.txt"), "keep");
            var protectedFile = Path.Combine(source, "out.cbz");
            File.WriteAllText(protectedFile, "protected");

            ImageFolderArchiver.DeleteSourceFiles(
                source,
                ["01.jpg", "sub/02.jpg", "out.cbz"],
                protectedFile);

            Assert.False(File.Exists(Path.Combine(source, "01.jpg")));
            Assert.False(File.Exists(Path.Combine(sub, "02.jpg")));
            Assert.False(Directory.Exists(sub));
            Assert.True(File.Exists(Path.Combine(source, "notes.txt")));
            Assert.True(File.Exists(protectedFile));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void TryResolveRelative_RejectsAbsoluteAndTraversalPaths()
    {
        var root = CreateTempDir();
        try
        {
            Assert.False(ImageFolderArchiver.TryResolveRelative(root, "", out _));
            Assert.False(ImageFolderArchiver.TryResolveRelative(root, "../evil.jpg", out _));
            Assert.False(ImageFolderArchiver.TryResolveRelative(root, Path.Combine(root, "evil.jpg"), out _));

            Assert.True(ImageFolderArchiver.TryResolveRelative(root, "sub/01.jpg", out var safe));
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "sub", "01.jpg"), safe);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
