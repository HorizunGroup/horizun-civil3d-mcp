using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public sealed class AtomicOutputTests
{
    [Fact]
    public void FailedProductionPreservesExistingFile()
    {
        var dir = TestDirectories.CreateTempSubdirectory("hz-output-");
        try
        {
            var path = Path.Combine(dir.FullName, "drawing.pdf");
            File.WriteAllText(path, "previous PDF");
            Assert.Throws<IOException>(() => AtomicOutput.Write(path, true,
                stage => { File.WriteAllText(stage, "partial"); throw new IOException("plot failed"); },
                _ => true));
            Assert.Equal("previous PDF", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir.FullName));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void FailedValidationPreservesExistingFile()
    {
        var dir = TestDirectories.CreateTempSubdirectory("hz-output-");
        try
        {
            var path = Path.Combine(dir.FullName, "drawing.pdf");
            File.WriteAllText(path, "previous PDF");
            Assert.Throws<IOException>(() => AtomicOutput.Write(path, true,
                stage => File.WriteAllText(stage, "bad PDF"), _ => false));
            Assert.Equal("previous PDF", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir.FullName));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void VerifiedReplacementKeepsExplicitBackup()
    {
        var dir = TestDirectories.CreateTempSubdirectory("hz-output-");
        try
        {
            var path = Path.Combine(dir.FullName, "drawing.pdf");
            File.WriteAllText(path, "previous PDF");
            var result = AtomicOutput.Write(path, true,
                stage => File.WriteAllText(stage, "verified PDF"),
                stage => File.ReadAllText(stage) == "verified PDF");
            Assert.Equal("verified PDF", File.ReadAllText(path));
            Assert.NotNull(result.BackupPath);
            Assert.Equal("previous PDF", File.ReadAllText(result.BackupPath!));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void ExistingFileWithoutOverwriteIsNeverReplaced()
    {
        var dir = TestDirectories.CreateTempSubdirectory("hz-output-");
        try
        {
            var path = Path.Combine(dir.FullName, "points.csv");
            File.WriteAllText(path, "previous CSV");
            Assert.Throws<IOException>(() => AtomicOutput.Write(path, false,
                stage => File.WriteAllText(stage, "new CSV"), _ => true));
            Assert.Equal("previous CSV", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir.FullName));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void DestinationChangedDuringProductionIsNotReplaced()
    {
        var dir = TestDirectories.CreateTempSubdirectory("hz-output-");
        try
        {
            var path = Path.Combine(dir.FullName, "drawing.pdf");
            File.WriteAllText(path, "original PDF");
            var expected = AtomicOutput.Capture(path);
            Assert.Throws<IOException>(() => AtomicOutput.Write(path, true,
                stage =>
                {
                    File.WriteAllText(stage, "new PDF");
                    File.WriteAllText(path, "concurrent PDF");
                },
                _ => true, expected));
            Assert.Equal("concurrent PDF", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir.FullName));
        }
        finally { dir.Delete(true); }
    }
}
