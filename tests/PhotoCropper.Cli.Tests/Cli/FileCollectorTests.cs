using PhotoCropper.Cli.Services;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Cli.Tests.Cli;

public sealed class FileCollectorTests : IDisposable
{
    private readonly string _tempDir;

    public FileCollectorTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("FileCollectorTests");
    }

    [Fact]
    public void CollectFiles_SingleDirectFiles_ShouldIncludeValidImagesOnly()
    {
        string validJpg = Path.Combine(_tempDir, "photo1.JPG");
        string validPng = Path.Combine(_tempDir, "photo2.png");
        string validWebp = Path.Combine(_tempDir, "photo3.webp");
        string invalidTxt = Path.Combine(_tempDir, "notes.txt");

        File.WriteAllText(validJpg, "fake image");
        File.WriteAllText(validPng, "fake image");
        File.WriteAllText(validWebp, "fake image");
        File.WriteAllText(invalidTxt, "fake notes");

        var collected = FileCollector.CollectFiles([validJpg, validPng, validWebp, invalidTxt], recursive: false);

        collected.Count.ShouldBe(3);
        collected.ShouldContain(Path.GetFullPath(validJpg));
        collected.ShouldContain(Path.GetFullPath(validPng));
        collected.ShouldContain(Path.GetFullPath(validWebp));
        collected.ShouldNotContain(Path.GetFullPath(invalidTxt));
    }

    [Fact]
    public void CollectFiles_DirectoryTopLevelOnly_ShouldNotRecurseWhenRecursiveIsFalse()
    {
        string subDir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(subDir);

        string rootPhoto = Path.Combine(_tempDir, "root.jpg");
        string nestedPhoto = Path.Combine(subDir, "nested.jpg");

        File.WriteAllText(rootPhoto, "fake image");
        File.WriteAllText(nestedPhoto, "fake image");

        var collected = FileCollector.CollectFiles([_tempDir], recursive: false);

        collected.Count.ShouldBe(1);
        collected[0].ShouldBe(Path.GetFullPath(rootPhoto));
    }

    [Fact]
    public void CollectFiles_DirectoryRecursive_ShouldIncludeSubdirectories()
    {
        string subDir = Path.Combine(_tempDir, "nested", "level2");
        Directory.CreateDirectory(subDir);

        string rootPhoto = Path.Combine(_tempDir, "root.png");
        string nestedPhoto = Path.Combine(subDir, "deep.tif");

        File.WriteAllText(rootPhoto, "fake image");
        File.WriteAllText(nestedPhoto, "fake image");

        var collected = FileCollector.CollectFiles([_tempDir], recursive: true);

        collected.Count.ShouldBe(2);
        collected.ShouldContain(Path.GetFullPath(rootPhoto));
        collected.ShouldContain(Path.GetFullPath(nestedPhoto));
    }

    [Fact]
    public void CollectFiles_DuplicateInputs_ShouldDeduplicateResults()
    {
        string photo = Path.Combine(_tempDir, "duplicate.jpeg");
        File.WriteAllText(photo, "fake image");

        var collected = FileCollector.CollectFiles([photo, photo, _tempDir], recursive: false);

        collected.Count.ShouldBe(1);
        collected[0].ShouldBe(Path.GetFullPath(photo));
    }

    [Fact]
    public void CollectFiles_NonExistentPaths_ShouldReturnEmptyList()
    {
        string nonExistent = Path.Combine(_tempDir, "does_not_exist.jpg");
        var collected = FileCollector.CollectFiles([nonExistent], recursive: false);

        collected.ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
