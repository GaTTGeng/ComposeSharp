using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ComposeSharp.Engine.Internal;

internal static class DockerBuildContextArchive
{
    private const string ExternalDockerfileArchivePathPrefix = "__external_dockerfile__";
    private const int LinuxFunctionNotImplementedError = 38;

    public static Stream Create(string directory, string? dockerfile = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Build context directory '{directory}' does not exist.");

        cancellationToken.ThrowIfCancellationRequested();
        var dockerfilePath = GetDockerfilePath(directory, dockerfile);
        var dockerfileSourcePath = ResolveDockerfileLinkTarget(dockerfilePath);
        var dockerfileArchivePath = GetDockerfileArchivePath(directory, dockerfile);
        var isExternalDockerfile = !IsWithinDirectory(directory, dockerfileSourcePath);
        if (isExternalDockerfile && !File.Exists(dockerfileSourcePath))
            throw new FileNotFoundException($"Dockerfile '{dockerfile}' does not exist.", dockerfileSourcePath);
        var ignoreRules = DockerIgnoreRule.Read(directory, dockerfileSourcePath);
        var archive = CreateTemporaryArchive();
        try
        {
            using (var writer = new TarWriter(archive, leaveOpen: true))
            {
                var linkedDockerfilePath = isExternalDockerfile && IsWithinDirectory(directory, dockerfilePath)
                    ? dockerfilePath
                    : null;
                WriteDirectoryEntries(writer, directory, directory, dockerfileArchivePath, linkedDockerfilePath, archive.Name, ignoreRules, cancellationToken);

                if (isExternalDockerfile)
                    WriteFile(writer, dockerfileSourcePath, dockerfileArchivePath, cancellationToken);
            }

            archive.Position = 0;
            return archive;
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    public static string GetDockerfileArchivePath(string directory, string? dockerfile)
    {
        var dockerfileSourcePath = GetDockerfileSourcePath(directory, dockerfile);
        return IsWithinDirectory(directory, dockerfileSourcePath)
            ? ToArchivePath(directory, dockerfileSourcePath)
            : GetAvailableExternalDockerfileArchivePath(directory);
    }

    private static void WriteDirectoryEntries(
        TarWriter writer,
        string rootDirectory,
        string directory,
        string dockerfileArchivePath,
        string? linkedDockerfilePath,
        string temporaryArchivePath,
        IReadOnlyList<DockerIgnoreRule> ignoreRules,
        CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsAreEqual(path, temporaryArchivePath))
                continue;
            if (linkedDockerfilePath is not null && PathsAreEqual(path, linkedDockerfilePath))
                continue;

            var relativePath = ToArchivePath(rootDirectory, path);
            var attributes = File.GetAttributes(path);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var isSymbolicLink = attributes.HasFlag(FileAttributes.ReparsePoint);
            var isIgnored = !string.Equals(relativePath, dockerfileArchivePath, StringComparison.Ordinal) &&
                            DockerIgnoreRule.IsIgnored(relativePath, isDirectory, ignoreRules);
            if (isIgnored)
            {
                if (isDirectory && !isSymbolicLink &&
                    (ContainsArchivePath(relativePath, dockerfileArchivePath) ||
                     DockerIgnoreRule.ShouldTraverseIgnoredDirectory(relativePath, ignoreRules)))
                    WriteDirectoryEntries(writer, rootDirectory, path, dockerfileArchivePath, linkedDockerfilePath, temporaryArchivePath, ignoreRules, cancellationToken);
                continue;
            }

            if (isSymbolicLink)
                WriteSymbolicLink(writer, path, relativePath, isDirectory, cancellationToken);
            else if (isDirectory)
            {
                var entry = new PaxTarEntry(TarEntryType.Directory, relativePath)
                {
                    ModificationTime = File.GetLastWriteTimeUtc(path)
                };
                if (!OperatingSystem.IsWindows())
                    entry.Mode = File.GetUnixFileMode(path);
                writer.WriteEntry(entry);
                WriteDirectoryEntries(writer, rootDirectory, path, dockerfileArchivePath, linkedDockerfilePath, temporaryArchivePath, ignoreRules, cancellationToken);
            }
            else
            {
                var fileType = GetUnixFileType(path);
                if (fileType == UnixFileType.Socket)
                    continue;
                if (fileType == UnixFileType.Device)
                    throw new NotSupportedException($"Build context contains an unsupported Unix device node at '{path}'.");
                if (fileType == UnixFileType.NamedPipe)
                    WriteNamedPipe(writer, path, relativePath, cancellationToken);
                else
                    WriteFile(writer, path, relativePath, cancellationToken);
            }
        }
    }

    private static bool ContainsArchivePath(string directoryPath, string archivePath)
        => archivePath.StartsWith(directoryPath + "/", StringComparison.Ordinal);

    private static bool PathsAreEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string GetAvailableExternalDockerfileArchivePath(string directory)
    {
        for (var suffix = 0; ; suffix++)
        {
            var archivePath = suffix == 0
                ? ExternalDockerfileArchivePathPrefix
                : $"{ExternalDockerfileArchivePathPrefix}-{suffix}";
            if (!PathExists(Path.Combine(directory, archivePath)))
                return archivePath;
        }
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void WriteFile(TarWriter writer, string path, string archivePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = File.OpenRead(path);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, archivePath)
        {
            DataStream = new CancellationAwareReadStream(input, cancellationToken),
            ModificationTime = File.GetLastWriteTimeUtc(path)
        };
        if (!OperatingSystem.IsWindows())
            entry.Mode = File.GetUnixFileMode(path);
        writer.WriteEntry(entry);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void WriteSymbolicLink(TarWriter writer, string path, string relativePath, bool isDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var linkTarget = isDirectory
            ? new DirectoryInfo(path).LinkTarget
            : new FileInfo(path).LinkTarget;
        if (string.IsNullOrWhiteSpace(linkTarget))
            throw new IOException($"Unable to read symbolic link target for '{path}'.");

        if (!Path.IsPathRooted(linkTarget))
            linkTarget = linkTarget.Replace('\\', '/');

        writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, relativePath) { LinkName = linkTarget });
    }

    private static UnixFileType GetUnixFileType(string path)
    {
        if (OperatingSystem.IsWindows())
            return UnixFileType.Regular;

        var mode = OperatingSystem.IsLinux()
            ? GetLinuxFileMode(path)
            : OperatingSystem.IsMacOS()
                ? GetMacOsFileMode(path)
                : 0u;
        return (mode & 0xF000) switch
        {
            0x1000 => UnixFileType.NamedPipe,
            0x2000 or 0x6000 => UnixFileType.Device,
            0xC000 => UnixFileType.Socket,
            _ => UnixFileType.Regular
        };
    }

    private static uint GetLinuxFileMode(string path)
    {
        try
        {
            var result = StatX(-100, path, 0x100, 0x1, out LinuxStatx stat);
            if (result == 0)
                return stat.Mode;

            return Marshal.GetLastWin32Error() == LinuxFunctionNotImplementedError
                ? GetLinuxFileModeFromLStat(path)
                : ThrowUnableToInspectFile(path);
        }
        catch (EntryPointNotFoundException)
        {
            return GetLinuxFileModeFromLStat(path);
        }
    }

    private static uint GetLinuxFileModeFromLStat(string path)
    {
        var result = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => LStat(path, out LinuxX64Stat stat) == 0 ? stat.Mode : ThrowUnableToInspectFile(path),
            Architecture.Arm64 => LStat(path, out LinuxArm64Stat stat) == 0 ? stat.Mode : ThrowUnableToInspectFile(path),
            Architecture.X86 => LStat(path, out LinuxX86Stat stat) == 0 ? stat.Mode : ThrowUnableToInspectFile(path),
            Architecture.Arm => LStat(path, out LinuxX86Stat stat) == 0 ? stat.Mode : ThrowUnableToInspectFile(path),
            _ => throw new PlatformNotSupportedException(
                $"Unable to inspect filesystem entries without statx on {RuntimeInformation.ProcessArchitecture}.")
        };

        return result;
    }

    private static uint GetMacOsFileMode(string path)
        => LStat(path, out MacOsStat stat) == 0
            ? stat.Mode
            : ThrowUnableToInspectFile(path);

    private static uint ThrowUnableToInspectFile(string path)
        => throw new IOException($"Unable to inspect filesystem entry '{path}' (error {Marshal.GetLastWin32Error()}).");

    private static void WriteNamedPipe(TarWriter writer, string path, string archivePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = new PaxTarEntry(TarEntryType.Fifo, archivePath);
        if (!OperatingSystem.IsWindows())
            entry.Mode = File.GetUnixFileMode(path);
        writer.WriteEntry(entry);
    }

    private enum UnixFileType
    {
        Regular,
        NamedPipe,
        Socket,
        Device
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int StatX(int directoryFileDescriptor, string path, int flags, uint mask, out LinuxStatx stat);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out MacOsStat stat);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out LinuxX64Stat stat);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out LinuxArm64Stat stat);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out LinuxX86Stat stat);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(28)]
        public ushort Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxX64Stat
    {
        [FieldOffset(24)]
        public uint Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxArm64Stat
    {
        [FieldOffset(16)]
        public uint Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxX86Stat
    {
        [FieldOffset(12)]
        public ushort Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 512)]
    private struct MacOsStat
    {
        [FieldOffset(4)]
        public uint Mode;
    }

    private static string GetDockerfileSourcePath(string directory, string? dockerfile)
        => ResolveDockerfileLinkTarget(GetDockerfilePath(directory, dockerfile));

    private static string GetDockerfilePath(string directory, string? dockerfile)
    {
        var path = Path.GetFullPath(Path.Combine(directory, dockerfile ?? "Dockerfile"));
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? GetCanonicalPath(path) : path;
    }

    private static string ResolveDockerfileLinkTarget(string path)
    {
        var root = Path.GetPathRoot(path)!;
        var resolvedPath = root;
        var relativePath = Path.GetRelativePath(root, path);
        foreach (var segment in relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            resolvedPath = Path.Combine(resolvedPath, segment);
            FileSystemInfo entry = Directory.Exists(resolvedPath)
                ? new DirectoryInfo(resolvedPath)
                : new FileInfo(resolvedPath);
            if (entry.LinkTarget is not null)
                resolvedPath = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? resolvedPath;
        }

        return resolvedPath;
    }

    private static string GetCanonicalPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var root = Path.GetPathRoot(path)!;
        var relativePath = Path.GetRelativePath(root, path);
        var currentPath = root;
        foreach (var segment in relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var entries = Directory.EnumerateFileSystemEntries(currentPath).ToList();
            var matchingPath = entries.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal)) ??
                               entries.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase));
            if (matchingPath is null)
                return path;

            currentPath = matchingPath;
        }

        return currentPath;
    }

    private static bool IsWithinDirectory(string directory, string path)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }

    private static string ToArchivePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static FileStream CreateTemporaryArchive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"docker-build-context-{Guid.NewGuid():N}.tar");
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);
    }

    private sealed class DockerIgnoreRule(bool include, string patternText, Regex pattern)
    {
        public static IReadOnlyList<DockerIgnoreRule> Read(string directory, string dockerfileSourcePath)
        {
            var dockerfileIgnorePath = dockerfileSourcePath + ".dockerignore";
            var path = File.Exists(dockerfileIgnorePath)
                ? dockerfileIgnorePath
                : Path.Combine(directory, ".dockerignore");
            if (!File.Exists(path))
                return [];

            return File.ReadLines(path)
                .Select((line, index) => index == 0 ? line.TrimStart('\uFEFF') : line)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(Create)
                .ToList();
        }

        public static bool IsIgnored(string relativePath, bool isDirectory, IReadOnlyList<DockerIgnoreRule> rules)
        {
            var ignored = false;
            foreach (var rule in rules)
            {
                if (rule.Matches(relativePath, isDirectory))
                    ignored = !rule.Include;
            }
            return ignored;
        }

        public static bool ShouldTraverseIgnoredDirectory(string relativePath, IReadOnlyList<DockerIgnoreRule> rules)
            => rules.Any(rule => rule.CanMatchDescendant(relativePath));

        private static DockerIgnoreRule Create(string line)
        {
            var include = line.StartsWith('!');
            var pattern = NormalizePattern(include ? line[1..] : line);
            if (pattern is "." or "")
                return new DockerIgnoreRule(include, pattern, new Regex("(?!)", RegexOptions.CultureInvariant));

            var expression = ToExpression(pattern);
            if (!pattern.Contains('/'))
                expression = $"(?:.*/)?{expression}";

            return new DockerIgnoreRule(include, pattern, new Regex($"^{expression}(?:/.*)?$", RegexOptions.CultureInvariant));
        }

        private static string NormalizePattern(string pattern)
        {
            if (OperatingSystem.IsWindows())
                pattern = pattern.Replace('\\', '/');

            var segments = new List<string>();
            foreach (var segment in pattern.Split('/'))
            {
                if (segment is "" or ".")
                    continue;
                if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
                {
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            return string.Join('/', segments);
        }

        private bool Include { get; } = include;
        private string PatternText { get; } = patternText;
        private Regex Pattern { get; } = pattern;

        private bool Matches(string relativePath, bool isDirectory)
            => Pattern.IsMatch(relativePath) || (isDirectory && Pattern.IsMatch(relativePath + "/"));

        private bool CanMatchDescendant(string relativePath)
        {
            if (!Include)
                return false;
            if (!PatternText.Contains('/'))
                return true;

            if (PatternText.Contains("**", StringComparison.Ordinal))
                return true;

            var patternSegments = PatternText.Split('/');
            var pathSegments = relativePath.Split('/');
            for (var index = 0; index < Math.Min(patternSegments.Length, pathSegments.Length); index++)
            {
                var expression = ToExpression(patternSegments[index]);
                if (!Regex.IsMatch(pathSegments[index], $"^{expression}$", RegexOptions.CultureInvariant))
                    return false;
            }

            return true;
        }

        private static string ToExpression(string pattern)
        {
            var expression = new StringBuilder();
            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];
                if (character == '\\' && index + 1 < pattern.Length)
                {
                    index++;
                    expression.Append(Regex.Escape(pattern[index].ToString()));
                }
                else if (character == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    index++;
                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:.*/)?");
                    }
                    else
                    {
                        expression.Append(".*");
                    }
                }
                else if (character == '*')
                {
                    expression.Append("[^/]*");
                }
                else if (character == '?')
                {
                    expression.Append("[^/]");
                }
                else if (character == '[' && TryAppendCharacterClass(pattern, ref index, expression))
                {
                }
                else
                {
                    expression.Append(Regex.Escape(character.ToString()));
                }
            }
            return expression.ToString();
        }

        private static bool TryAppendCharacterClass(string pattern, ref int index, StringBuilder expression)
        {
            var end = pattern.IndexOf(']', index + 1);
            if (end < 0 || end == index + 1)
                return false;

            var content = pattern[(index + 1)..end];
            expression.Append('[');
            if (content[0] is '!' or '^')
            {
                expression.Append('^');
                content = content[1..];
            }

            foreach (var character in content)
            {
                if (character is '\\' or ']')
                    expression.Append('\\');
                expression.Append(character);
            }
            expression.Append(']');
            index = end;
            return true;
        }
    }

    private sealed class CancellationAwareReadStream(Stream inner, CancellationToken cancellationToken) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.Read(buffer, offset, count);
        }
        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.Read(buffer);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReadAsync(buffer, offset, count, cancellationToken);
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReadAsync(buffer, cancellationToken);
        }
    }
}
