using System.Formats.Tar;
using System.Text;
using ComposeSharp.Engine.Internal;

namespace ComposeSharp.Tests;

public class ContainerArchiveTests
{
    [Fact]
    public void IsContainedPath_AllowsFilesystemRootAndRejectsParentTraversal()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        var destination = Path.GetFullPath(Path.GetTempPath());

        Assert.True(ContainerArchive.IsContainedPath(root, Path.Combine(root, "file.txt")));
        Assert.False(ContainerArchive.IsContainedPath(destination, Path.Combine(destination, "..", "outside.txt")));
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_PreservesModeAndMaterializesHardLinks()
    {
        const string content = "payload";
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "payload.sh")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "payload-copy.sh")
            {
                LinkName = "payload.sh"
            });
        }
        archive.Position = 0;

        var destination = Path.Combine(Path.GetTempPath(), $"archive-extract-{Guid.NewGuid():N}");
        try
        {
            var bytesCopied = await ContainerArchive.ExtractToDirectoryAsync(archive, destination, CancellationToken.None);

            Assert.Equal(content.Length * 2, bytesCopied);
            Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(destination, "payload.sh")));
            Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(destination, "payload-copy.sh")));
            if (!OperatingSystem.IsWindows())
                Assert.True(File.GetUnixFileMode(Path.Combine(destination, "payload.sh")).HasFlag(UnixFileMode.UserExecute));
        }
        finally
        {
            archive.Dispose();
            try { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_RejectsSymlinkAncestors()
    {
        if (OperatingSystem.IsWindows())
            return;

        var parent = Path.Combine(Path.GetTempPath(), $"archive-link-{Guid.NewGuid():N}");
        var target = Path.Combine(parent, "target");
        var link = Path.Combine(parent, "link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => ContainerArchive.ExtractToDirectoryAsync(
                new MemoryStream(), Path.Combine(link, "nested"), CancellationToken.None));
        }
        finally
        {
            try { if (Directory.Exists(link)) Directory.Delete(link); }
            catch { /* best effort */ }
            try { if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_RejectsDanglingSymlinkArchiveTargets()
    {
        if (OperatingSystem.IsWindows())
            return;

        var destination = Path.Combine(Path.GetTempPath(), $"archive-dangling-link-{Guid.NewGuid():N}");
        var link = Path.Combine(destination, "link");
        var missingTarget = Path.Combine(Path.GetTempPath(), $"archive-missing-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        File.CreateSymbolicLink(link, missingTarget);

        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "link/payload.txt")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("payload"))
            });
        }
        archive.Position = 0;

        try
        {
            await Assert.ThrowsAsync<IOException>(() => ContainerArchive.ExtractToDirectoryAsync(
                archive, destination, CancellationToken.None));
            Assert.False(File.Exists(missingTarget));
        }
        finally
        {
            archive.Dispose();
            File.Delete(link);
            try { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateFromPathAsync_PreservesUnixModes()
    {
        if (OperatingSystem.IsWindows())
            return;

        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"archive-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourceFile = Path.Combine(sourceDirectory, "run.sh");
        await File.WriteAllTextAsync(sourceFile, "#!/bin/sh\n");
        var directoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var fileMode = directoryMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(sourceDirectory, directoryMode);
        File.SetUnixFileMode(sourceFile, fileMode);

        try
        {
            await using var archive = await ContainerArchive.CreateFromPathAsync(sourceDirectory, CancellationToken.None);
            using var reader = new TarReader(archive, leaveOpen: true);
            var directory = reader.GetNextEntry(copyData: false);
            var file = reader.GetNextEntry(copyData: false);

            Assert.NotNull(directory);
            Assert.NotNull(file);
            Assert.Equal(directoryMode, directory.Mode);
            Assert.Equal(fileMode, file.Mode);
        }
        finally
        {
            File.SetUnixFileMode(sourceDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try { if (Directory.Exists(sourceDirectory)) Directory.Delete(sourceDirectory, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
