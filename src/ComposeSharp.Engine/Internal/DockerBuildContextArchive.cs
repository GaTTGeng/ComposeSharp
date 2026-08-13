using System.Formats.Tar;

namespace ComposeSharp.Engine.Internal;

internal static class DockerBuildContextArchive
{
    public static Stream Create(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Build context directory '{directory}' does not exist.");

        var archive = new MemoryStream();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        using (var writer = new TarWriter(archive, leaveOpen: true))
        {
            foreach (var path in Directory.EnumerateDirectories(directory, "*", options).OrderBy(path => path, StringComparer.Ordinal))
            {
                var entryName = ToArchivePath(directory, path);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, entryName));
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", options).OrderBy(path => path, StringComparer.Ordinal))
                writer.WriteEntry(path, ToArchivePath(directory, path));
        }

        archive.Position = 0;
        return archive;
    }

    private static string ToArchivePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
