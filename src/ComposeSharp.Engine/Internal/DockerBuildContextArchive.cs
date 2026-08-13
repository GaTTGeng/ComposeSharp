using System.Formats.Tar;
using System.Text;
using System.Text.RegularExpressions;

namespace ComposeSharp.Engine.Internal;

internal static class DockerBuildContextArchive
{
    public static Stream Create(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Build context directory '{directory}' does not exist.");

        var ignoreRules = DockerIgnoreRule.Read(directory);
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, leaveOpen: true))
        {
            foreach (var path in EnumerateEntries(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                var relativePath = ToArchivePath(directory, path);
                var attributes = File.GetAttributes(path);
                var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                if (DockerIgnoreRule.IsIgnored(relativePath, isDirectory, ignoreRules))
                    continue;

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    WriteSymbolicLink(writer, path, relativePath, isDirectory);
                    continue;
                }

                if (isDirectory)
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, relativePath));
                else
                    writer.WriteEntry(path, relativePath);
            }
        }

        archive.Position = 0;
        return archive;
    }

    private static IEnumerable<string> EnumerateEntries(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            yield return path;

            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (var descendant in EnumerateEntries(path))
                    yield return descendant;
            }
        }
    }

    private static void WriteSymbolicLink(TarWriter writer, string path, string relativePath, bool isDirectory)
    {
        var linkTarget = isDirectory
            ? new DirectoryInfo(path).LinkTarget
            : new FileInfo(path).LinkTarget;
        if (string.IsNullOrWhiteSpace(linkTarget))
            throw new IOException($"Unable to read symbolic link target for '{path}'.");

        writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, relativePath) { LinkName = linkTarget });
    }

    private static string ToArchivePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private sealed class DockerIgnoreRule(bool include, Regex pattern)
    {
        public static IReadOnlyList<DockerIgnoreRule> Read(string directory)
        {
            var path = Path.Combine(directory, ".dockerignore");
            if (!File.Exists(path))
                return [];

            return File.ReadLines(path)
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

        private static DockerIgnoreRule Create(string line)
        {
            var include = line.StartsWith('!');
            var pattern = include ? line[1..] : line;
            pattern = pattern.TrimStart('/').TrimEnd('/');
            if (pattern is "." or "")
                return new DockerIgnoreRule(include, new Regex("(?!)", RegexOptions.CultureInvariant));

            var expression = ToExpression(pattern);
            if (!pattern.Contains('/'))
                expression = $"(?:.*/)?{expression}";

            return new DockerIgnoreRule(include, new Regex($"^{expression}(?:/.*)?$", RegexOptions.CultureInvariant));
        }

        private bool Include { get; } = include;
        private Regex Pattern { get; } = pattern;

        private bool Matches(string relativePath, bool isDirectory)
        {
            if (Pattern.IsMatch(relativePath))
                return true;

            if (isDirectory)
                return Pattern.IsMatch(relativePath + "/");

            return false;
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
                else
                {
                    expression.Append(Regex.Escape(character.ToString()));
                }
            }
            return expression.ToString();
        }
    }
}
