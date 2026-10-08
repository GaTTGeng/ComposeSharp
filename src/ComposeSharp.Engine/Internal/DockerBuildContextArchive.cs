using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Packs a build-context directory into a tar stream for the Docker Engine build API,
/// honoring <c>.dockerignore</c> rules and preserving symlinks and Unix file modes.
/// </summary>
internal static class DockerBuildContextArchive
{
    // Reserved tar entry name used to stage a Dockerfile that lives outside the build context
    // (the engine's build API can only reference Dockerfiles present in the archive).
    private const string ExternalDockerfileArchivePathPrefix = "__external_dockerfile__";
    private const int LinuxOperationNotPermittedError = 1;
    private const int LinuxFunctionNotImplementedError = 38;
    private const string UnicodeScalarExpression = @"(?:[^/\uD800-\uDFFF]|[\uD800-\uDBFF][\uDC00-\uDFFF])";

    public static Stream Create(
        string directory,
        string? dockerfile = null,
        string? dockerfileArchivePath = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Build context directory '{directory}' does not exist.");

        cancellationToken.ThrowIfCancellationRequested();
        // Stage 1: resolve the Dockerfile on disk and classify it as in-context or external.
        var dockerfilePath = GetDockerfilePath(directory, dockerfile);
        var dockerfileSourcePath = ResolveFileSystemPath(dockerfilePath);
        dockerfileArchivePath ??= GetDockerfileArchivePath(directory, dockerfile);
        var isExternalDockerfile = !IsWithinDirectory(directory, dockerfileSourcePath);
        if (PathExists(dockerfileSourcePath))
        {
            EnsureRegularFile(dockerfileSourcePath, "Dockerfile");
        }
        else if (isExternalDockerfile)
        {
            throw new FileNotFoundException($"Dockerfile '{dockerfile}' does not exist.", dockerfileSourcePath);
        }
        // Stage 2: load .dockerignore rules; they gate which entries reach the tar stream.
        var ignoreRules = DockerIgnoreRule.Read(directory, dockerfilePath);
        var archive = CreateTemporaryArchive();
        try
        {
            using (var writer = new TarWriter(archive, leaveOpen: true))
            {
                // When the Dockerfile's real path is outside the context but a link to it sits
                // inside, the in-context link entry is replaced by a real copy staged under the
                // reserved external-dockerfile name so the build finds a regular file.
                var linkedDockerfilePath = isExternalDockerfile && IsWithinDirectory(directory, dockerfilePath)
                    ? dockerfilePath
                    : null;
                var stagedDockerfileArchivePath = isExternalDockerfile ? dockerfileArchivePath : null;
                // Stage 3: walk the context tree and emit one tar entry per included filesystem entry.
                WriteDirectoryEntries(writer, directory, directory, dockerfileArchivePath, linkedDockerfilePath,
                    stagedDockerfileArchivePath, archive.Name, ignoreRules, cancellationToken);

                // Stage 4: append the staged external Dockerfile copy under its reserved archive name.
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

    // Returns the path the Dockerfile will have inside the tar stream: its context-relative path
    // when it lives in the context, otherwise a free reserved name for the staged external copy.
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
        string? stagedDockerfileArchivePath,
        string temporaryArchivePath,
        IReadOnlyList<DockerIgnoreRule> ignoreRules,
        CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Never pack the temporary archive itself, and drop the in-context Dockerfile link
            // when its content is staged separately under the reserved name.
            if (PathsAreEqual(path, temporaryArchivePath))
                continue;
            if (linkedDockerfilePath is not null && PathsAreEqual(path, linkedDockerfilePath))
                continue;

            var relativePath = ToArchivePath(rootDirectory, path);
            if (string.Equals(relativePath, stagedDockerfileArchivePath, StringComparison.Ordinal))
                continue;
            var attributes = File.GetAttributes(path);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var isSymbolicLink = attributes.HasFlag(FileAttributes.ReparsePoint);
            var isIgnored = !string.Equals(relativePath, dockerfileArchivePath, StringComparison.Ordinal) &&
                            DockerIgnoreRule.IsIgnored(relativePath, ignoreRules);
            if (isIgnored)
            {
                // Ignored directories are still entered when a later negation (!) rule might
                // re-include a descendant, or when the staged Dockerfile lives inside them.
                if (isDirectory && !isSymbolicLink &&
                    (ContainsArchivePath(relativePath, dockerfileArchivePath) ||
                     DockerIgnoreRule.ShouldTraverseIgnoredDirectory(relativePath, ignoreRules)))
                    WriteDirectoryEntries(writer, rootDirectory, path, dockerfileArchivePath, linkedDockerfilePath,
                        stagedDockerfileArchivePath, temporaryArchivePath, ignoreRules, cancellationToken);
                continue;
            }

            if (isSymbolicLink)
                // Preserve links as link entries rather than packing their targets.
                WriteSymbolicLink(writer, path, relativePath, isDirectory, cancellationToken);
            else if (isDirectory)
            {
                var entry = new PaxTarEntry(TarEntryType.Directory, relativePath)
                {
                    ModificationTime = File.GetLastWriteTimeUtc(path)
                };
                // Windows has no Unix mode bits; only Unix hosts can preserve permissions.
                if (!OperatingSystem.IsWindows())
                    entry.Mode = File.GetUnixFileMode(path);
                writer.WriteEntry(entry);
                WriteDirectoryEntries(writer, rootDirectory, path, dockerfileArchivePath, linkedDockerfilePath,
                    stagedDockerfileArchivePath, temporaryArchivePath, ignoreRules, cancellationToken);
            }
            else
            {
                // Dispatch by Unix file type: sockets are skipped, devices are rejected, FIFOs and regular files are packed.
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
    {
        var leftPath = Path.GetFullPath(left);
        var rightPath = Path.GetFullPath(right);
        // Windows paths are case-insensitive; compare literal and symlink-resolved forms either way.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(leftPath, rightPath, comparison) ||
               string.Equals(ResolveFileSystemPath(leftPath), ResolveFileSystemPath(rightPath), comparison);
    }

    // Picks the first reserved name that does not collide with an entry already in the context.
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

    private static void EnsureRegularFile(string path, string fileDescription)
    {
        if (Directory.Exists(path))
            throw new NotSupportedException($"{fileDescription} '{path}' must be a regular file.");

        switch (GetUnixFileType(path))
        {
            case UnixFileType.Regular:
                return;
            case UnixFileType.NamedPipe:
                throw new NotSupportedException($"{fileDescription} '{path}' cannot be a named pipe.");
            case UnixFileType.Socket:
                throw new NotSupportedException($"{fileDescription} '{path}' cannot be a Unix socket.");
            case UnixFileType.Device:
                throw new NotSupportedException($"{fileDescription} '{path}' cannot be a Unix device node.");
            default:
                throw new NotSupportedException($"{fileDescription} '{path}' must be a regular file.");
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

        // Windows reports relative link targets with backslashes; the tar format expects forward slashes.
        if (OperatingSystem.IsWindows() && !Path.IsPathRooted(linkTarget))
            linkTarget = linkTarget.Replace('\\', '/');

        writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, relativePath)
        {
            LinkName = linkTarget,
            ModificationTime = File.GetLastWriteTimeUtc(path)
        });
    }

    private static UnixFileType GetUnixFileType(string path)
    {
        // Windows entries are always treated as regular files; special-node types only exist on Unix.
        if (OperatingSystem.IsWindows())
            return UnixFileType.Regular;

        // Decode the file-type nibble of st_mode (0xF000 mask).
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

    // statx is preferred on Linux but is unavailable on older kernels or under restricted
    // seccomp/EPERM policies, so those errors fall back to lstat with per-architecture layouts.
    private static uint GetLinuxFileMode(string path)
    {
        try
        {
            var result = StatX(-100, path, 0x100, 0x1, out LinuxStatx stat);
            if (result == 0)
                return stat.Mode;

            return ShouldFallBackToLStat(Marshal.GetLastWin32Error())
                ? GetLinuxFileModeFromLStat(path)
                : ThrowUnableToInspectFile(path);
        }
        catch (EntryPointNotFoundException)
        {
            return GetLinuxFileModeFromLStat(path);
        }
    }

    private static bool ShouldFallBackToLStat(int error)
        => error is LinuxFunctionNotImplementedError or LinuxOperationNotPermittedError;

    private static uint GetLinuxFileModeFromLStat(string path)
    {
        // lstat's struct stat layout differs per CPU architecture, so each one gets its own pinvoke shape.
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
        var entry = new PaxTarEntry(TarEntryType.Fifo, archivePath)
        {
            ModificationTime = File.GetLastWriteTimeUtc(path)
        };
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
        => ResolveFileSystemPath(GetDockerfilePath(directory, dockerfile));

    private static string GetDockerfilePath(string directory, string? dockerfile)
    {
        var path = Path.GetFullPath(Path.Combine(directory, dockerfile ?? "Dockerfile"));
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? GetCanonicalPath(path) : path;
    }

    private static string ResolveFileSystemPath(string path)
    {
        // Walk path segments and resolve each symlink so link-vs-target comparisons use real locations.
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

        // Re-match every segment against on-disk names so case differences resolve to the real entry.
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
        // Containment holds only when the relative path neither escapes via ".." nor is rooted.
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }

    private static string ToArchivePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static FileStream CreateTemporaryArchive()
    {
        // DeleteOnClose guarantees the temp tar is removed even if packing throws.
        var path = Path.Combine(Path.GetTempPath(), $"docker-build-context-{Guid.NewGuid():N}.tar");
        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);
    }

    private sealed class DockerIgnoreRule(bool include, string patternText, Regex pattern)
    {
        public static IReadOnlyList<DockerIgnoreRule> Read(string directory, string dockerfileSourcePath)
        {
            // A Dockerfile-specific <Dockerfile>.dockerignore takes precedence over the
            // context-root .dockerignore, matching the engine's own lookup rules.
            var dockerfileIgnorePath = dockerfileSourcePath + ".dockerignore";
            var path = PathExists(dockerfileIgnorePath)
                ? dockerfileIgnorePath
                : Path.Combine(directory, ".dockerignore");
            if (!PathExists(path))
                return [];

            EnsureRegularFile(path, "Docker ignore file");

            // Strip a possible BOM, drop comments and blanks, then compile each remaining pattern.
            return File.ReadLines(path)
                .Select((line, index) => index == 0 ? line.TrimStart('\uFEFF') : line)
                .Where(line => !line.StartsWith('#'))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Select(Create)
                .ToList();
        }

        public static bool IsIgnored(string relativePath, IReadOnlyList<DockerIgnoreRule> rules)
        {
            // Last matching rule wins, so a later "!" negation can re-include an earlier exclusion.
            var ignored = false;
            foreach (var rule in rules)
            {
                if (rule.Matches(relativePath))
                    ignored = !rule.Include;
            }
            return ignored;
        }

        // Only an include (negation) rule can pull a descendant of an ignored directory back in.
        public static bool ShouldTraverseIgnoredDirectory(string relativePath, IReadOnlyList<DockerIgnoreRule> rules)
            => rules.Any(rule => rule.CanMatchDescendant(relativePath));

        private static DockerIgnoreRule Create(string line)
        {
            var include = line.StartsWith('!');
            var pattern = NormalizePattern(include ? line[1..] : line);
            if (pattern is "." or "")
                return new DockerIgnoreRule(include, pattern, new Regex("(?!)", RegexOptions.CultureInvariant));

            var expression = ToExpression(pattern);
            // A pattern without "/" matches at any depth; the trailing group also matches all descendants.
            if (!pattern.Contains('/'))
                expression = $"(?:.*/)?{expression}";

            return new DockerIgnoreRule(include, pattern, new Regex($"^{expression}(?:/.*)?$", RegexOptions.CultureInvariant));
        }

        private static string NormalizePattern(string pattern)
        {
            // Ignore patterns always use forward slashes, including when authored on Windows.
            if (OperatingSystem.IsWindows())
                pattern = pattern.Replace('\\', '/');

            // Collapse "." and "x/.." so the compiled pattern has no traversal segments.
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

        private bool Matches(string relativePath) => Pattern.IsMatch(relativePath);

        private bool CanMatchDescendant(string relativePath)
        {
            if (!Include)
                return false;
            // Bare patterns and ** can reach any depth without further checks.
            if (!PatternText.Contains('/'))
                return true;

            if (PatternText.Contains("**", StringComparison.Ordinal))
                return true;

            // Otherwise require per-segment glob agreement along the path prefix.
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
            // Translate one glob pattern into a regular expression: ** crosses directories,
            // * stays inside a segment, ? matches a single Unicode scalar.
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
                    expression.Append(UnicodeScalarExpression);
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
            var end = FindCharacterClassEnd(pattern, index + 1);
            if (end < 0 || end == index + 1)
                return false;

            var content = pattern[(index + 1)..end];
            var isNegated = content.Length > 1 && content[0] == '^';
            if (isNegated)
                content = content[1..];
            var scalarExpressions = ParseCharacterClassRanges(content)
                .Select(range => ToUnicodeScalarRangeExpression(range.Start, range.End))
                .ToList();
            if (scalarExpressions.Count == 0)
            {
                expression.Append(isNegated ? UnicodeScalarExpression : "(?!)");
                index = end;
                return true;
            }

            var scalarClassExpression = string.Join("|", scalarExpressions);
            if (isNegated)
                expression.Append("(?!(?:").Append(scalarClassExpression).Append("))").Append(UnicodeScalarExpression);
            else
                expression.Append("(?:").Append(scalarClassExpression).Append(')');

            index = end;
            return true;
        }

        private static IReadOnlyList<(int Start, int End)> ParseCharacterClassRanges(string content)
        {
            // Tokenize by Unicode scalar first so surrogate pairs are not split into fake ranges.
            var tokens = new List<(int Scalar, bool IsEscaped)>();
            for (var index = 0; index < content.Length;)
            {
                var isEscaped = content[index] == '\\' && index + 1 < content.Length;
                if (isEscaped)
                    index++;
                tokens.Add((ReadUnicodeScalar(content, ref index), isEscaped));
            }

            var ranges = new List<(int Start, int End)>();
            for (var index = 0; index < tokens.Count;)
            {
                // An unescaped '-' between two scalars forms an inclusive range.
                if (index + 2 < tokens.Count && !tokens[index + 1].IsEscaped &&
                    tokens[index + 1].Scalar == '-')
                {
                    if (tokens[index].Scalar <= tokens[index + 2].Scalar)
                        ranges.Add((tokens[index].Scalar, tokens[index + 2].Scalar));
                    index += 3;
                    continue;
                }

                ranges.Add((tokens[index].Scalar, tokens[index].Scalar));
                index++;
            }

            return ranges;
        }

        private static int ReadUnicodeScalar(string text, ref int index)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                var scalar = char.ConvertToUtf32(text, index);
                index += 2;
                return scalar;
            }

            return text[index++];
        }

        private static string ToUnicodeScalarRangeExpression(int startScalar, int endScalar)
        {
            // BMP scalars map to plain \u ranges; supplementary scalars need explicit
            // surrogate-pair expressions because .NET regular expressions are UTF-16 based.
            var expressions = new List<string>();
            if (startScalar <= 0xFFFF)
                expressions.AddRange(ToBmpRangeExpressions(startScalar, Math.Min(endScalar, 0xFFFF)));
            if (endScalar >= 0x10000)
                expressions.Add(ToSupplementaryRangeExpression(Math.Max(startScalar, 0x10000), endScalar));
            return expressions.Count switch
            {
                0 => "(?!)",
                1 => expressions[0],
                _ => $"(?:{string.Join("|", expressions)})"
            };
        }

        private static IReadOnlyList<string> ToBmpRangeExpressions(int startScalar, int endScalar)
        {
            var expressions = new List<string>();
            if (startScalar <= 0xD7FF)
                expressions.Add(ToBmpRangeExpression(startScalar, Math.Min(endScalar, 0xD7FF)));
            if (endScalar >= 0xE000)
                expressions.Add(ToBmpRangeExpression(Math.Max(startScalar, 0xE000), endScalar));
            return expressions;
        }

        private static string ToBmpRangeExpression(int startScalar, int endScalar)
            => startScalar == endScalar
                ? ToUnicodeEscape(startScalar)
                : $"[{ToUnicodeEscape(startScalar)}-{ToUnicodeEscape(endScalar)}]";

        private static string ToSupplementaryRangeExpression(int startScalar, int endScalar)
        {
            var startHigh = (char)(((startScalar - 0x10000) >> 10) + 0xD800);
            var startLow = (char)(((startScalar - 0x10000) & 0x3FF) + 0xDC00);
            var endHigh = (char)(((endScalar - 0x10000) >> 10) + 0xD800);
            var endLow = (char)(((endScalar - 0x10000) & 0x3FF) + 0xDC00);
            if (startHigh == endHigh)
                return $"{ToUnicodeEscape(startHigh)}[{ToUnicodeEscape(startLow)}-{ToUnicodeEscape(endLow)}]";

            var expressions = new List<string>
            {
                $"{ToUnicodeEscape(startHigh)}[{ToUnicodeEscape(startLow)}-\\uDFFF]"
            };
            if (startHigh + 1 < endHigh)
                expressions.Add($"[{ToUnicodeEscape((char)(startHigh + 1))}-{ToUnicodeEscape((char)(endHigh - 1))}]" +
                                "[\\uDC00-\\uDFFF]");
            expressions.Add($"{ToUnicodeEscape(endHigh)}[\\uDC00-{ToUnicodeEscape(endLow)}]");
            return $"(?:{string.Join("|", expressions)})";
        }

        private static string ToUnicodeEscape(int scalar) => $"\\u{scalar:X4}";

        private static int FindCharacterClassEnd(string pattern, int start)
        {
            for (var index = start; index < pattern.Length; index++)
            {
                if (pattern[index] == '\\' && index + 1 < pattern.Length)
                {
                    index++;
                    continue;
                }

                if (pattern[index] == ']')
                    return index;
            }

            return -1;
        }
    }

    // Wraps file data so long entry copies observe the caller's cancellation token.
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
