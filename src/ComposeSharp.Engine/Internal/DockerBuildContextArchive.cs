using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ComposeSharp.Engine.Internal;

internal static class DockerBuildContextArchive
{
    private const string ExternalDockerfileArchivePathPrefix = "__external_dockerfile__";

    public static Stream Create(string directory, string? dockerfile = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Build context directory '{directory}' does not exist.");

        cancellationToken.ThrowIfCancellationRequested();
        var dockerfileSourcePath = GetDockerfileSourcePath(directory, dockerfile);
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
                WriteDirectoryEntries(writer, directory, directory, dockerfileArchivePath, archive.Name, ignoreRules, cancellationToken);

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
        string temporaryArchivePath,
        IReadOnlyList<DockerIgnoreRule> ignoreRules,
        CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsAreEqual(path, temporaryArchivePath))
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
                    WriteDirectoryEntries(writer, rootDirectory, path, dockerfileArchivePath, temporaryArchivePath, ignoreRules, cancellationToken);
                continue;
            }

            if (isSymbolicLink)
                WriteSymbolicLink(writer, path, relativePath, isDirectory, cancellationToken);
            else if (isDirectory)
            {
                var entry = new PaxTarEntry(TarEntryType.Directory, relativePath);
                if (!OperatingSystem.IsWindows())
                    entry.Mode = File.GetUnixFileMode(path);
                writer.WriteEntry(entry);
                WriteDirectoryEntries(writer, rootDirectory, path, dockerfileArchivePath, temporaryArchivePath, ignoreRules, cancellationToken);
            }
            else if (IsNamedPipe(path))
                WriteNamedPipe(writer, path, relativePath, cancellationToken);
            else
                WriteFile(writer, path, relativePath, cancellationToken);
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

    private static bool IsNamedPipe(string path)
    {
        if (OperatingSystem.IsWindows())
            return false;

        var mode = OperatingSystem.IsLinux()
            ? GetLinuxFileMode(path)
            : OperatingSystem.IsMacOS()
                ? GetMacOsFileMode(path)
                : 0u;
        return (mode & 0xF000) == 0x1000;
    }

    private static uint GetLinuxFileMode(string path)
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => LStat(path, out LinuxX64Stat stat) == 0
                ? stat.Mode
                : ThrowUnableToInspectFile(path),
            Architecture.Arm64 => LStat(path, out LinuxArm64Stat stat) == 0
                ? stat.Mode
                : ThrowUnableToInspectFile(path),
            Architecture.X86 => LStat(path, out Linux32BitStat stat) == 0
                ? stat.Mode
                : ThrowUnableToInspectFile(path),
            Architecture.Arm => LStat(path, out Linux32BitStat stat) == 0
                ? stat.Mode
                : ThrowUnableToInspectFile(path),
            _ => 0u
        };

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

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out LinuxX64Stat stat);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out LinuxArm64Stat stat);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out Linux32BitStat stat);

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, out MacOsStat stat);

    [StructLayout(LayoutKind.Explicit, Size = 512)]
    private struct LinuxX64Stat
    {
        [FieldOffset(24)]
        public uint Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 512)]
    private struct LinuxArm64Stat
    {
        [FieldOffset(16)]
        public uint Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 512)]
    private struct Linux32BitStat
    {
        [FieldOffset(16)]
        public uint Mode;
    }

    [StructLayout(LayoutKind.Explicit, Size = 512)]
    private struct MacOsStat
    {
        [FieldOffset(4)]
        public uint Mode;
    }

    private static string GetDockerfileSourcePath(string directory, string? dockerfile)
        => Path.GetFullPath(Path.Combine(directory, dockerfile ?? "Dockerfile"));

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
            var segments = new List<string>();
            foreach (var segment in pattern.Replace('\\', '/').Split('/'))
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
                if (character == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
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
