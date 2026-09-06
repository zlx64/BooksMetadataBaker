using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using BooksMetadataBaker.Services.Comic;
using BooksMetadataBaker.Services.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BooksMetadataBaker.Tests;

public sealed class ArchiveComicInfoWriterTests : IDisposable
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ComicInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Series>Test Series</Series>
          <Number>1</Number>
        </ComicInfo>
        """;

    private const string Bogus7z = @"C:\nonexistent\definitely-not-7z\7z.exe";

    private static readonly byte[] DummyImage = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"archive_writer_tests_{Guid.NewGuid():N}");

    public ArchiveComicInfoWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static ArchiveComicInfoWriter CreateWriter(IDictionary<string, string?>? config = null)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();
        return new ArchiveComicInfoWriter(cfg, NullLogger<ArchiveComicInfoWriter>.Instance);
    }

    private static string CreateCbz(string path, params (string Name, string? Content)[] entries)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            if (content is null)
                s.Write(DummyImage);
            else
                s.Write(Encoding.UTF8.GetBytes(content));
        }
        return path;
    }

    private async Task<string> CreateCbtAsync(string path, params (string Name, string? Content)[] entries)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        await using var tar = new TarWriter(fs, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var (name, content) in entries)
        {
            var bytes = content is null ? DummyImage : Encoding.UTF8.GetBytes(content);
            var tmpFile = Path.Combine(_dir, "entry_" + Guid.NewGuid().ToString("N"));
            await File.WriteAllBytesAsync(tmpFile, bytes);
            try
            {
                await tar.WriteEntryAsync(tmpFile, name);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }
        return path;
    }

    private static async Task<List<string>> ReadTarNamesAsync(string path)
    {
        var names = new List<string>();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var tar = new TarReader(fs, leaveOpen: true);
        while (true)
        {
            var e = await tar.GetNextEntryAsync(copyData: false);
            if (e is null)
                break;
            names.Add(e.Name);
        }
        return names;
    }

    private static async Task<string> ReadEntryTextAsync(ZipArchive zip, string name)
    {
        await using var s = zip.GetEntry(name)!.Open();
        using var r = new StreamReader(s);
        return await r.ReadToEndAsync();
    }

    // --- ZIP (CBZ) -----------------------------------------------------------

    [Fact]
    public async Task WriteZip_AddsComicInfoAtRoot()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t1.cbz"), ("001.png", null), ("002.png", null), ("003.png", null));

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);

        Assert.True(ok, err);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(4, zip.Entries.Count);
        Assert.Equal(SampleXml, await ReadEntryTextAsync(zip, "ComicInfo.xml"));
    }

    [Fact]
    public async Task WriteZip_RoundTrip_ReadBackMatches()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t2.cbz"), ("001.png", null), ("002.png", null));

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);
        Assert.True(ok, err);

        var model = await writer.ReadExistingAsync(path, CancellationToken.None);

        Assert.NotNull(model);
        Assert.Equal("Test Series", model!.Series);
        Assert.Equal("1", model.Number);
    }

    [Fact]
    public async Task WriteZip_ReplaceExisting_PreservesEntryOrder()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t3.cbz"),
            ("a.png", null), ("ComicInfo.xml", "<ComicInfo><Series>Old</Series></ComicInfo>"), ("b.png", null));

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);
        Assert.True(ok, err);

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(["a.png", "ComicInfo.xml", "b.png"], zip.Entries.Select(e => e.FullName));
        Assert.Equal(SampleXml, await ReadEntryTextAsync(zip, "ComicInfo.xml"));
    }

    [Fact]
    public async Task WriteZip_PreservesStoredCompression()
    {
        var writer = CreateWriter();
        var path = Path.Combine(_dir, "t4.cbz");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var zipArch = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zipArch.CreateEntry("stored.png", CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(DummyImage);
        }

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);
        Assert.True(ok, err);

        using var zip = ZipFile.OpenRead(path);
        var stored = zip.GetEntry("stored.png")!;
        Assert.Equal(stored.Length, stored.CompressedLength); // still stored (uncompressed)
        var meta = zip.GetEntry("ComicInfo.xml")!;
        Assert.True(meta.CompressedLength < meta.Length); // deflated
    }

    [Fact]
    public async Task WriteZip_CorruptArchive_ReturnsError_FileUntouched()
    {
        var writer = CreateWriter();
        var path = Path.Combine(_dir, "t5.cbz");
        var original = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(path, original);

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(err));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ReadZip_ReturnsModelFromExisting()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t6.cbz"),
            ("ComicInfo.xml", "<ComicInfo><Series>From Archive</Series><Year>2019</Year></ComicInfo>"));

        var model = await writer.ReadExistingAsync(path, CancellationToken.None);

        Assert.NotNull(model);
        Assert.Equal("From Archive", model!.Series);
        Assert.Equal(2019, model.Year);
    }

    [Fact]
    public async Task ReadZip_ReturnsNull_WhenAbsent()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t7.cbz"), ("001.png", null));

        Assert.Null(await writer.ReadExistingAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ReadZip_ReturnsNull_WhenMalformed()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t8.cbz"), ("ComicInfo.xml", "<ComicInfo><Broken"));

        Assert.Null(await writer.ReadExistingAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task CountZip_FiveImagesOneCover_ReturnsFour()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t9.cbz"),
            ("001.png", null), ("002.png", null), ("003.jpg", null), ("004.png", null), ("cover.jpg", null));

        Assert.Equal(4, await writer.CountPagesAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task CountZip_BackcoverIsCounted()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t10.cbz"), ("001.png", null), ("cover.jpg", null), ("backcover.png", null));

        Assert.Equal(2, await writer.CountPagesAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task CountZip_SubdirCoverIsCounted()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t11.cbz"), ("001.png", null), ("sub/cover.jpg", null));

        Assert.Equal(2, await writer.CountPagesAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task CountZip_NonImageEntriesIgnored()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t12.cbz"),
            ("001.png", null), ("notes.txt", "hello"), ("ComicInfo.xml", "<ComicInfo/>"));

        Assert.Equal(1, await writer.CountPagesAsync(path, CancellationToken.None));
    }

    // --- TAR (CBT) -----------------------------------------------------------

    [Fact]
    public async Task WriteTar_AddsComicInfoAtRoot_RoundTrip()
    {
        var writer = CreateWriter();
        var path = await CreateCbtAsync(Path.Combine(_dir, "t13.cbt"),
            ("001.png", null), ("002.png", null), ("003.png", null));

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);
        Assert.True(ok, err);

        var model = await writer.ReadExistingAsync(path, CancellationToken.None);
        Assert.NotNull(model);
        Assert.Equal("Test Series", model!.Series);
        Assert.Equal("1", model.Number);

        var names = await ReadTarNamesAsync(path);
        Assert.Equal(4, names.Count);
        Assert.Contains("ComicInfo.xml", names);
    }

    [Fact]
    public async Task WriteTar_ReplaceExisting_KeepsAllEntries()
    {
        var writer = CreateWriter();
        var path = await CreateCbtAsync(Path.Combine(_dir, "t14.cbt"),
            ("a.png", null), ("ComicInfo.xml", "<ComicInfo><Series>Old</Series></ComicInfo>"), ("b.png", null));

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);
        Assert.True(ok, err);

        // CBT uses extract+rebuild (uniform strategy); entry order is not preserved,
        // but all entries survive and the old ComicInfo.xml is replaced, not duplicated.
        var names = await ReadTarNamesAsync(path);
        Assert.Equal(3, names.Count);
        Assert.Contains("a.png", names);
        Assert.Contains("b.png", names);
        Assert.Contains("ComicInfo.xml", names);
    }

    [Fact]
    public async Task CountTar_FiveImagesOneCover_ReturnsFour()
    {
        var writer = CreateWriter();
        var path = await CreateCbtAsync(Path.Combine(_dir, "t15.cbt"),
            ("001.png", null), ("002.png", null), ("003.jpg", null), ("004.png", null), ("cover.jpg", null));

        Assert.Equal(4, await writer.CountPagesAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task CountTar_BackcoverIsCounted()
    {
        var writer = CreateWriter();
        var path = await CreateCbtAsync(Path.Combine(_dir, "t16.cbt"),
            ("001.png", null), ("cover.jpg", null), ("backcover.png", null));

        Assert.Equal(2, await writer.CountPagesAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ReadTar_ReturnsNull_WhenAbsent()
    {
        var writer = CreateWriter();
        var path = await CreateCbtAsync(Path.Combine(_dir, "t17.cbt"), ("001.png", null));

        Assert.Null(await writer.ReadExistingAsync(path, CancellationToken.None));
    }

    // --- 7-Zip (CB7) / RAR (CBR) without tools --------------------------------

    [Fact]
    public async Task WriteCb7_7zUnavailable_ReturnsError_FileUntouched()
    {
        var writer = CreateWriter(new Dictionary<string, string?> { ["Tools:SevenZipPath"] = Bogus7z });
        var path = Path.Combine(_dir, "t18.cb7");
        var original = new byte[] { 37, 78, 61, 95 }; // 7z magic
        await File.WriteAllBytesAsync(path, original);

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal("7-Zip not available — ComicInfo.xml not embedded", err);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task WriteCbr_RarUnavailable_ReturnsError_FileUntouched()
    {
        var writer = CreateWriter(); // Tools:RarPath unset
        var path = Path.Combine(_dir, "t19.cbr");
        var original = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 }; // RAR magic
        await File.WriteAllBytesAsync(path, original);

        var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal("RAR tool not available — ComicInfo.xml not embedded", err);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ConvertToCbz_7zUnavailable_ReturnsError_FileUntouched()
    {
        var writer = CreateWriter(new Dictionary<string, string?> { ["Tools:SevenZipPath"] = Bogus7z });
        var path = Path.Combine(_dir, "t23.cbr");
        var original = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 }; // RAR magic
        await File.WriteAllBytesAsync(path, original);

        var (ok, newPath, err) = await writer.ConvertToCbzAsync(path, SampleXml, CancellationToken.None);

        Assert.False(ok);
        Assert.Null(newPath);
        Assert.Equal("7-Zip not available — cannot convert to CBZ", err);
        // Original .cbr is left in place (no .cbz produced).
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".cbz")));
    }

    [Fact]
    public async Task ConvertToCbz_UnsupportedExtension_ReturnsError()
    {
        var writer = CreateWriter();
        var path = Path.Combine(_dir, "t24.pdf");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        var (ok, newPath, err) = await writer.ConvertToCbzAsync(path, SampleXml, CancellationToken.None);

        Assert.False(ok);
        Assert.Null(newPath);
        Assert.Equal("Unsupported archive format", err);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ConvertToCbz_Cbt_ConvertsToCbzWithMetadata()
    {
        var writer = CreateWriter();
        var path = await CreateCbtAsync(Path.Combine(_dir, "t25.cbt"),
            ("001.png", null), ("002.png", null), ("003.png", null));

        var (ok, newPath, err) = await writer.ConvertToCbzAsync(path, SampleXml, CancellationToken.None);

        Assert.True(ok, err);
        Assert.Equal(Path.ChangeExtension(path, ".cbz"), newPath);
        // Original .cbt is removed; the new .cbz holds the pages plus ComicInfo.xml.
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(newPath!));
        using var zip = ZipFile.OpenRead(newPath!);
        Assert.Equal(4, zip.Entries.Count);
        Assert.Equal(SampleXml, await ReadEntryTextAsync(zip, "ComicInfo.xml"));
    }

    [Fact]
    public async Task ConvertToCbz_Cbz_RepackagesInPlace()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "t26.cbz"), ("001.png", null), ("002.png", null));

        var (ok, newPath, err) = await writer.ConvertToCbzAsync(path, SampleXml, CancellationToken.None);

        Assert.True(ok, err);
        Assert.Equal(path, newPath); // already a .cbz — repackaged in place
        Assert.True(File.Exists(path));
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(3, zip.Entries.Count);
        Assert.Equal(SampleXml, await ReadEntryTextAsync(zip, "ComicInfo.xml"));
    }

    [Fact]
    public async Task ReadCb7_7zUnavailable_ReturnsNull()
    {
        var writer = CreateWriter(new Dictionary<string, string?> { ["Tools:SevenZipPath"] = Bogus7z });
        var path = Path.Combine(_dir, "t20.cb7");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });

        Assert.Null(await writer.ReadExistingAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task CountCb7_7zUnavailable_ReturnsZero()
    {
        var writer = CreateWriter(new Dictionary<string, string?> { ["Tools:SevenZipPath"] = Bogus7z });
        var path = Path.Combine(_dir, "t21.cb7");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });

        Assert.Equal(0, await writer.CountPagesAsync(path, CancellationToken.None));
    }

    // --- 7-Zip (CB7) round trip when a 7-Zip binary is available ----------------
    // Skipped on machines without 7z/7zz on PATH (CBZ/CBT are covered in-process).

    private static string? Find7z() => ToolResolver.Which("7z") ?? ToolResolver.Which("7zz");

    [Fact]
    public async Task WriteCb7_7zAvailable_RoundTrip()
    {
        var exe = Find7z();
        if (exe is null)
            return; // 7-Zip not on PATH — no-op (xunit v2 has no runtime skip); runs on Docker/Linux CI

        var writer = CreateWriter();
        var path = Path.Combine(_dir, "t22.cb7");
        var page1 = Path.Combine(_dir, "rt_001.png");
        var page2 = Path.Combine(_dir, "rt_002.png");
        await File.WriteAllBytesAsync(page1, DummyImage);
        await File.WriteAllBytesAsync(page2, DummyImage);
        try
        {
            var (okCreate, _, _, stderrCreate, runErrCreate) = await ProcessRunner.RunAsync(
                exe, ["a", "-t7z", path, "rt_001.png", "rt_002.png"],
                NullLogger<ArchiveComicInfoWriterTests>.Instance, 30_000, default, workingDirectory: _dir);
            Assert.True(okCreate && runErrCreate is null, runErrCreate ?? stderrCreate);

            var (ok, err) = await writer.WriteAsync(path, SampleXml, CancellationToken.None);
            Assert.True(ok, err);

            var model = await writer.ReadExistingAsync(path, CancellationToken.None);
            Assert.NotNull(model);
            Assert.Equal("Test Series", model!.Series);
            Assert.Equal("1", model.Number);

            Assert.Equal(2, await writer.CountPagesAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(page1);
            File.Delete(page2);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // --- Page entry classification --------------------------------------------

    [Theory]
    [InlineData("001.png", true)]
    [InlineData("001.jpg", true)]
    [InlineData("001.JPEG", true)]
    [InlineData("001.webp", true)]
    [InlineData("001.avif", true)]
    [InlineData("001.tiff", true)]
    [InlineData("001.heic", true)]
    [InlineData("cover.jpg", false)]
    [InlineData("COVER.png", false)]
    [InlineData("!cover.jpg", false)]
    [InlineData("folder.png", false)]
    [InlineData("backcover.png", true)]
    [InlineData("sub/cover.jpg", true)]
    [InlineData("sub/001.png", true)]
    [InlineData("notes.txt", false)]
    [InlineData("ComicInfo.xml", false)]
    [InlineData("", false)]
    public void IsPageEntry_ClassifiesPerGuide(string name, bool expected)
    {
        Assert.Equal(expected, ArchiveComicInfoWriter.IsPageEntry(name));
    }

    // --- Entry listing ----------------------------------------------------------

    [Fact]
    public async Task ListEntries_Zip_ReturnsForwardSlashedNames()
    {
        var writer = CreateWriter();
        var path = CreateCbz(Path.Combine(_dir, "list.cbz"),
            ("Series v1/001.png", null), ("Series v2/001.png", null), ("ComicInfo.xml", "<x/>"));

        var (ok, names, err) = await writer.ListEntriesAsync(path, CancellationToken.None);

        Assert.True(ok, err);
        Assert.Contains("Series v1/001.png", names);
        Assert.Contains("Series v2/001.png", names);
        Assert.Contains("ComicInfo.xml", names);
    }

    [Fact]
    public async Task ListEntries_Tar_ReturnsNames()
    {
        var writer = CreateWriter();
        var path = await CreateCbtAsync(Path.Combine(_dir, "list.cbt"),
            ("Series v1/001.png", null), ("Series v2/001.png", null));

        var (ok, names, err) = await writer.ListEntriesAsync(path, CancellationToken.None);

        Assert.True(ok, err);
        Assert.Equal(2, names.Count);
        Assert.Contains("Series v1/001.png", names);
    }

    [Fact]
    public async Task ListEntries_UnsupportedExtension_Fails()
    {
        var writer = CreateWriter();

        var (ok, names, err) = await writer.ListEntriesAsync(Path.Combine(_dir, "book.pdf"), CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(names);
        Assert.Equal("Unsupported archive format", err);
    }

    // --- Multi-volume split -------------------------------------------------------

    private static MultiVolumeSplitPlan PlanFor(params (string Volume, string Prefix)[] parts) =>
        new(parts.Select(p => new MultiVolumePart(p.Volume, p.Prefix, 1)).ToList());

    [Fact]
    public async Task SplitToCbz_SplitsVolumeFolders_EachWithOwnComicInfo()
    {
        var writer = CreateWriter();
        var src = CreateCbz(Path.Combine(_dir, "multi.cbz"),
            ("Series v1/001.png", null), ("Series v1/002.png", null),
            ("Series v2/001.png", null),
            ("ComicInfo.xml", "<stale/>"));
        var sidecar = src + ".meta.json";
        File.WriteAllText(sidecar, "{}");

        var plan = PlanFor(("1", "Series v1/"), ("2", "Series v2/"));
        Func<string, string> nameFor = vol => $"Series - Volume {vol}.cbz";
        var (ok, newPaths, err) = await writer.SplitToCbzAsync(
            src, plan, ["<xml-v1/>", "<xml-v2/>"], _dir, nameFor, CancellationToken.None);

        Assert.True(ok, err);
        Assert.Equal(2, newPaths.Count);
        Assert.False(File.Exists(src));
        Assert.False(File.Exists(sidecar));

        using var zip1 = ZipFile.OpenRead(newPaths[0]);
        Assert.Equal(["001.png", "002.png", "ComicInfo.xml"], zip1.Entries.Select(e => e.FullName).OrderBy(n => n).ToList());
        Assert.Equal("<xml-v1/>", await ReadEntryTextAsync(zip1, "ComicInfo.xml"));

        using var zip2 = ZipFile.OpenRead(newPaths[1]);
        Assert.Equal(["001.png", "ComicInfo.xml"], zip2.Entries.Select(e => e.FullName).OrderBy(n => n).ToList());
        Assert.Equal("<xml-v2/>", await ReadEntryTextAsync(zip2, "ComicInfo.xml"));
    }

    [Fact]
    public async Task SplitToCbz_PreservesNestedStructureUnderVolumeFolder()
    {
        var writer = CreateWriter();
        var src = CreateCbz(Path.Combine(_dir, "nested.cbz"),
            ("Set/Series v1/001.png", null),
            ("Set/Series v2/nested/001.png", null), ("Set/Series v2/nested/002.png", null));

        var plan = PlanFor(("1", "Set/Series v1/"), ("2", "Set/Series v2/"));
        var (ok, newPaths, err) = await writer.SplitToCbzAsync(
            src, plan, ["<v1/>", "<v2/>"], _dir, vol => $"Vol {vol}.cbz", CancellationToken.None);

        Assert.True(ok, err);
        using var zip2 = ZipFile.OpenRead(newPaths[1]);
        Assert.Contains("nested/001.png", zip2.Entries.Select(e => e.FullName));
        Assert.Contains("nested/002.png", zip2.Entries.Select(e => e.FullName));
    }

    [Fact]
    public async Task SplitToCbz_FailureLeavesSourceIntactAndNoPartialOutputs()
    {
        var writer = CreateWriter();
        // Plan references a folder that does not exist -> extraction succeeds,
        // the first part fails, source and any partial output must survive/vanish correctly.
        var src = CreateCbz(Path.Combine(_dir, "badplan.cbz"),
            ("Series v1/001.png", null), ("Series v2/001.png", null));
        var missing = Path.Combine(_dir, "Series - Volume 9.cbz");

        var plan = PlanFor(("1", "Series v1/"), ("9", "DoesNotExist/"));
        var (ok, newPaths, err) = await writer.SplitToCbzAsync(
            src, plan, ["<v1/>", "<v9/>"], _dir, vol => $"Series - Volume {vol}.cbz", CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("DoesNotExist/", err);
        Assert.Empty(newPaths);
        Assert.True(File.Exists(src));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task SplitToCbz_OverlappingSourcePath_SourceNotDeletedTwice()
    {
        var writer = CreateWriter();
        // The volume-1 part is named exactly like the source archive; the move
        // replaces the source, so the source must not be deleted afterwards.
        var src = CreateCbz(Path.Combine(_dir, "Series - Volume 1.cbz"),
            ("Series v1/001.png", null), ("Series v2/001.png", null));

        var plan = PlanFor(("1", "Series v1/"), ("2", "Series v2/"));
        var (ok, newPaths, err) = await writer.SplitToCbzAsync(
            src, plan, ["<v1/>", "<v2/>"], _dir, vol => $"Series - Volume {vol}.cbz", CancellationToken.None);

        Assert.True(ok, err);
        Assert.Equal(2, newPaths.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "Series - Volume 1.cbz")));
        Assert.True(File.Exists(Path.Combine(_dir, "Series - Volume 2.cbz")));
    }

    [Fact]
    public async Task SplitToCbz_MismatchedXmlCount_FailsFast()
    {
        var writer = CreateWriter();
        var src = CreateCbz(Path.Combine(_dir, "mismatch.cbz"), ("Series v1/001.png", null));

        var (ok, _, err) = await writer.SplitToCbzAsync(
            src, PlanFor(("1", "Series v1/")), ["<v1/>", "<extra/>"], _dir,
            vol => $"Vol {vol}.cbz", CancellationToken.None);

        Assert.False(ok);
        Assert.Equal("Split plan and XML count mismatch", err);
        Assert.True(File.Exists(src));
    }
}
