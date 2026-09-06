using BooksMetadataBaker.Services.Comic;
using Xunit;

namespace BooksMetadataBaker.Tests;

public sealed class MultiVolumeSplitterTests
{
    // --- Qualifying layouts ---------------------------------------------------

    [Fact]
    public void TopLevelVolumeFolders_ProducesSortedPlan()
    {
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            "Series v03/001.jpg", "Series v03/002.jpg",
            "Series v01/001.jpg",
            "Series v02/001.jpg", "Series v02/002.jpg", "Series v02/003.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.Parts.Count);
        Assert.Equal("1", plan.Parts[0].Volume);
        Assert.Equal("Series v01/", plan.Parts[0].FolderPrefix);
        Assert.Equal(1, plan.Parts[0].PageCount);
        Assert.Equal("2", plan.Parts[1].Volume);
        Assert.Equal(3, plan.Parts[1].PageCount);
        Assert.Equal("3", plan.Parts[2].Volume);
        Assert.Equal("Series v03/", plan.Parts[2].FolderPrefix);
        Assert.Equal(2, plan.Parts[2].PageCount);
    }

    [Fact]
    public void RootFolderWithVolumeSubfolders_ProducesPlan_CountingNestedPages()
    {
        // The real-world RAR shape: one root folder, volume subfolders inside,
        // one volume's pages living in a nested subfolder.
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            "Set vol 01-05/Series v01/001.jpg",
            "Set vol 01-05/Series v02/nested/001.jpg",
            "Set vol 01-05/Series v02/nested/002.jpg",
            "Set vol 01-05/Series v03/001.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.Parts.Count);
        Assert.Equal("Set vol 01-05/Series v02/", plan.Parts[1].FolderPrefix);
        Assert.Equal(2, plan.Parts[1].PageCount);
    }

    [Fact]
    public void BackslashEntries_AreNormalized()
    {
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            @"Set\Series v01\001.jpg",
            @"Set\Series v02\001.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal("Set/Series v01/", plan!.Parts[0].FolderPrefix);
    }

    [Fact]
    public void UnderscoreVolumeNames_Parse()
    {
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            "DLRAW.Series_v01/001.jpg",
            "DLRAW.Series_v02/001.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal("1", plan!.Parts[0].Volume);
        Assert.Equal("2", plan.Parts[1].Volume);
    }

    [Fact]
    public void NonContiguousVolumes_StillProducePlan()
    {
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            "Series - Volume 1/001.jpg",
            "Series - Volume 5/001.jpg",
            "Series - Volume 3/001.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal(["1", "3", "5"], plan!.Parts.Select(p => p.Volume));
    }

    [Fact]
    public void DecimalVolume_Parses()
    {
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            "Series Vol. 1/001.jpg",
            "Series Vol. 1.5/001.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal(["1", "1.5"], plan!.Parts.Select(p => p.Volume));
    }

    [Fact]
    public void RootComicInfoXml_IsAllowedAtBothLevels()
    {
        var topLevel = MultiVolumeSplitter.TryPlan(new[]
        {
            "ComicInfo.xml",
            "Series v1/001.jpg",
            "Series v2/001.jpg",
        });
        var rootFolder = MultiVolumeSplitter.TryPlan(new[]
        {
            "Set/ComicInfo.xml",
            "Set/v1/001.jpg",
            "Set/v2/001.jpg",
        });

        Assert.NotNull(topLevel);
        Assert.NotNull(rootFolder);
    }

    [Fact]
    public void NonPageFilesInVolumeFolder_DoNotBlockPlan()
    {
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            "Series v1/001.jpg", "Series v1/notes.txt",
            "Series v2/001.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal(1, plan!.Parts[0].PageCount);
    }

    // --- Non-qualifying layouts ------------------------------------------------

    [Fact]
    public void FlatPages_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[] { "001.jpg", "002.jpg", "003.png" }));
    }

    [Fact]
    public void SingleRootFolderWithOnlyPages_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Koi Seyo Mayakashi Tenshi Domo Vol.06/00.jpeg",
            "Koi Seyo Mayakashi Tenshi Domo Vol.06/01.jpg",
        }));
    }

    [Fact]
    public void LooseFileAtArchiveRoot_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "loose.jpg",
            "Series v1/001.jpg",
            "Series v2/001.jpg",
        }));
    }

    [Fact]
    public void LooseFileUnderRootFolder_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Set/loose.jpg",
            "Set/v1/001.jpg",
            "Set/v2/001.jpg",
        }));
    }

    [Fact]
    public void DuplicateVolumeNumbers_NoPlan()
    {
        // Two different folder names that both parse to volume 1.
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Series v1/001.jpg",
            "Series Vol 1/001.jpg",
            "Series v2/001.jpg",
        }));
    }

    [Fact]
    public void VolumeRangeFolder_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Series vol 01-05/001.jpg",
            "Series vol 06-08/001.jpg",
        }));
    }

    [Fact]
    public void ChapterNamedFolders_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Series ch01/001.jpg",
            "Series ch02/001.jpg",
        }));
    }

    [Fact]
    public void BareNumberFolders_NoPlan()
    {
        // "01"/"02" are not verifiable as volumes (could be chapters).
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "01/001.jpg",
            "02/001.jpg",
        }));
    }

    [Fact]
    public void FolderWithoutVolumeMarker_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Part One/001.jpg",
            "Part Two/001.jpg",
        }));
    }

    [Fact]
    public void VolumeFolderWithoutPages_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Series v1/notes.txt",
            "Series v2/001.jpg",
        }));
    }

    [Fact]
    public void OnlyOneVolumeFolder_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Series v1/001.jpg",
        }));
    }

    [Fact]
    public void RootFolderWithSingleSubfolder_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(new[]
        {
            "Set/v1/001.jpg",
        }));
    }

    [Fact]
    public void EmptyOrNull_NoPlan()
    {
        Assert.Null(MultiVolumeSplitter.TryPlan(Array.Empty<string>()));
        Assert.Null(MultiVolumeSplitter.TryPlan(null));
    }

    [Fact]
    public void DirectoryEntries_AreIgnored()
    {
        var plan = MultiVolumeSplitter.TryPlan(new[]
        {
            "Series v1/",
            "Series v1/001.jpg",
            "Series v2/",
            "Series v2/001.jpg",
        });

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Parts.Count);
    }
}
