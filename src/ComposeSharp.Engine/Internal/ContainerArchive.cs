using System.Formats.Tar;
using System.Text.RegularExpressions;

namespace ComposeSharp.Engine.Internal;

internal static class ContainerArchive
{
    // Docker serializes Go's os.FileMode; ModeDir is the high bit, unlike POSIX st_mode.
    public const uint DirectoryMode = 0x80000000;
    public static async Task<FileStream> CreateFromPathAsync(string sourcePath, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoint(sourcePath);
        var archive = CreateTemporaryArchive();
        try
        {
            using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException("The copy source must not be a filesystem root.", nameof(sourcePath));

                if (Directory.Exists(sourcePath))
                {
                    await WriteDirectoryAsync(writer, sourcePath, name, cancellationToken);
                }
                else
                {
                    await WriteFileAsync(writer, sourcePath, name, cancellationToken);
                }
            }

            archive.Position = 0;
            return archive;
        }
        catch
        {
            await archive.DisposeAsync();
            throw;
        }
    }

    public static long GetContentLength(string sourcePath)
    {
        if (File.Exists(sourcePath))
            return new FileInfo(sourcePath).Length;

        return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    public static async Task<long> ExtractToDirectoryAsync(Stream archive, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.GetFullPath(destinationPath);
        EnsureNoReparsePointInAncestors(destination);
        Directory.CreateDirectory(destination);
        long bytesCopied = 0;
        var directoryModes = new List<(string Path, UnixFileMode Mode)>();

        // TarReader's substreams rely on seeking when the HTTP response is chunked. Spool the
        // daemon response first so large archives stay off the managed heap and entries read fully.
        await using var bufferedArchive = CreateTemporaryArchive();
        await archive.CopyToAsync(bufferedArchive, cancellationToken);
        bufferedArchive.Position = 0;
        using var reader = new TarReader(bufferedArchive, leaveOpen: true);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeName = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destination, relativeName));
            if (!IsContainedPath(destination, target))
                throw new InvalidDataException($"Docker returned an archive entry outside the destination directory: '{entry.Name}'.");
            EnsureNoReparsePointInPath(destination, target);

            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
                directoryModes.Add((target, entry.Mode));
                continue;
            }

            if (entry.EntryType == TarEntryType.HardLink)
            {
                if (string.IsNullOrWhiteSpace(entry.LinkName))
                    throw new InvalidDataException($"Docker returned a hard link without a target: '{entry.Name}'.");
                var linkName = entry.LinkName.Replace('/', Path.DirectorySeparatorChar);
                var linkTarget = Path.GetFullPath(Path.Combine(destination, linkName));
                if (!IsContainedPath(destination, linkTarget))
                    throw new InvalidDataException($"Docker returned a hard link outside the destination directory: '{entry.LinkName}'.");
                EnsureNoReparsePointInPath(destination, linkTarget);
                if (!File.Exists(linkTarget))
                    throw new InvalidDataException($"Docker returned a hard link before its target: '{entry.LinkName}'.");
                var linkParent = Path.GetDirectoryName(target);
                if (string.IsNullOrEmpty(linkParent))
                    throw new InvalidDataException($"Docker returned an invalid archive entry path: '{entry.Name}'.");
                Directory.CreateDirectory(linkParent);
                await MaterializeHardLinkAsync(linkTarget, target, cancellationToken);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(target, File.GetUnixFileMode(linkTarget));
                bytesCopied += new FileInfo(linkTarget).Length;
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new InvalidDataException($"Docker returned an unsupported archive entry type '{entry.EntryType}'.");

            var parent = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(parent))
                throw new InvalidDataException($"Docker returned an invalid archive entry path: '{entry.Name}'.");
            Directory.CreateDirectory(parent);
            await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (entry.DataStream is not null)
                    await entry.DataStream.CopyToAsync(output, cancellationToken);
            }
            bytesCopied += entry.Length;
            ApplyUnixMode(target, entry.Mode);
        }

        foreach (var (path, mode) in directoryModes.OrderByDescending(directory => directory.Path.Length))
            ApplyUnixMode(path, mode);

        return bytesCopied;
    }

    internal static bool IsContainedPath(string destinationPath, string targetPath)
    {
        var destination = Path.GetFullPath(destinationPath);
        var target = Path.GetFullPath(targetPath);
        var relative = Path.GetRelativePath(destination, target);
        return relative == "." ||
               (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    private static void ApplyUnixMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, mode);
    }

    private static async Task MaterializeHardLinkAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task WriteDirectoryAsync(TarWriter writer, string directoryPath, string archivePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = new PaxTarEntry(TarEntryType.Directory, archivePath)
        {
            ModificationTime = Directory.GetLastWriteTimeUtc(directoryPath)
        };
        if (!OperatingSystem.IsWindows())
            directory.Mode = File.GetUnixFileMode(directoryPath);
        await writer.WriteEntryAsync(directory, cancellationToken);

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoint(entryPath);
            var entryName = Path.GetFileName(entryPath);
            var childArchivePath = $"{archivePath}/{entryName}";
            if (Directory.Exists(entryPath))
                await WriteDirectoryAsync(writer, entryPath, childArchivePath, cancellationToken);
            else if (File.Exists(entryPath))
                await WriteFileAsync(writer, entryPath, childArchivePath, cancellationToken);
            else
                throw new IOException($"The copy source contains an unsupported filesystem entry: '{entryPath}'.");
        }
    }

    private static async Task WriteFileAsync(TarWriter writer, string sourcePath, string archivePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, archivePath)
        {
            DataStream = stream,
            ModificationTime = File.GetLastWriteTimeUtc(sourcePath)
        };
        if (!OperatingSystem.IsWindows())
            entry.Mode = File.GetUnixFileMode(sourcePath);
        await writer.WriteEntryAsync(entry, cancellationToken);
    }

    private static FileStream CreateTemporaryArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compose-copy-{Guid.NewGuid():N}.tar");
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);
    }

    private static void EnsureNoReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Copying symbolic links or reparse points is not supported: '{path}'.");
    }

    private static void EnsureNoReparsePointInAncestors(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"The extraction destination traverses a local symbolic link or reparse point: '{current}'.");
        }
    }

    private static void EnsureNoReparsePointInPath(string destination, string target)
    {
        var relative = Path.GetRelativePath(destination, target);
        var current = destination;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Docker returned an archive path that traverses a local symbolic link: '{current}'.");
        }
    }
}

internal readonly record struct ContainerCopyPath(bool IsContainer, string? Service, string? Path)
{
    public static ContainerCopyPath Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (System.IO.Path.IsPathRooted(value))
            return new ContainerCopyPath(false, null, value);

        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 || value[separator + 1] != '/' ||
            !ServiceNamePattern.IsMatch(value[..separator]))
            return new ContainerCopyPath(false, null, value);

        return new ContainerCopyPath(true, value[..separator], value[(separator + 1)..]);
    }

    private static readonly Regex ServiceNamePattern = new(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.Compiled);
}
