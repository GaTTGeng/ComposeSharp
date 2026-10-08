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
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
        }
    }
}
