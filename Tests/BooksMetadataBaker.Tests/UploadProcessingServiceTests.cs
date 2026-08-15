using BooksMetadataBaker.Services;
using Xunit;

namespace BooksMetadataBaker.Tests;

public class UploadProcessingServiceTests
{
    private static string BaseFolder => OperatingSystem.IsWindows() ? @"C:\library" : "/library";

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
}
