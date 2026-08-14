using System.Formats.Tar;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using ComposeSharp.Loader;

namespace ComposeSharp.Tests;

public sealed class DockerBuildParametersFactoryTests
{
    [Fact]
    public void Create_MapsSupportedBuildAndOperationSettings()
    {
        var service = LoadService("""
            services:
              app:
                image: example/app:latest
                build:
                  context: .
                  dockerfile: Containerfile
                  args:
                    MODE: debug
                    KEEP: configured
                  cache_from: [example/cache:latest]
                  target: runtime
                  tags: [example/app:stable]
                  labels:
                    build.owner: loader
                  network: host
                  extra_hosts:
                    gateway: host-gateway
                  shm_size: 64M
                  platforms: [linux/amd64]
                  pull: true
                  no_cache: true
            """);

        var parameters = DockerBuildParametersFactory.Create(service, new ComposeBuildOptions
        {
            BuildArgs = new Dictionary<string, string> { ["MODE"] = "release" },
            Labels = new Dictionary<string, string> { ["build.owner"] = "operation" },
            Target = "test",
            Platform = "linux/arm64",
            Memory = "128M"
        });

        Assert.Equal(["example/app:latest", "example/app:stable"], parameters.Tags);
        Assert.Equal("Containerfile", parameters.Dockerfile);
        Assert.Equal("release", parameters.BuildArgs!["MODE"]);
        Assert.Equal("configured", parameters.BuildArgs["KEEP"]);
        Assert.Equal("operation", parameters.Labels!["build.owner"]);
        Assert.Equal(["example/cache:latest"], parameters.CacheFrom);
        Assert.Equal("test", parameters.Target);
        Assert.Equal("linux/arm64", parameters.Platform);
        Assert.Equal("host", parameters.NetworkMode);
        Assert.Equal(["gateway:host-gateway"], parameters.ExtraHosts);
        Assert.Equal(64L * 1024 * 1024, parameters.ShmSize);
        Assert.Equal(128L * 1024 * 1024, parameters.Memory);
        Assert.Equal("true", parameters.Pull);
        Assert.True(parameters.NoCache);
    }

    [Fact]
    public void Create_RejectsUnsupportedByteFormat()
    {
        var service = LoadService("""
            services:
              app:
                build: .
            """);

        var exception = Assert.Throws<ArgumentException>(() => DockerBuildParametersFactory.Create(service, new ComposeBuildOptions { Memory = "1.5M" }));

        Assert.Contains("Memory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AcceptsStandardByteUnitSuffixes()
    {
        var service = LoadService("""
            services:
              app:
                build:
                  context: .
                  shm_size: 64mb
            """);

        var parameters = DockerBuildParametersFactory.Create(service, new ComposeBuildOptions { Memory = "2MiB" });

        Assert.Equal(64L * 1024 * 1024, parameters.ShmSize);
        Assert.Equal(2L * 1024 * 1024, parameters.Memory);
    }

    [Fact]
    public void Create_UsesServicePlatformWhenBuildAndOperationPlatformsAreNotConfigured()
    {
        var service = LoadService("""
            services:
              app:
                platform: linux/amd64
                build: .
            """);

        var parameters = DockerBuildParametersFactory.Create(service, options: null);

        Assert.Equal("linux/amd64", parameters.Platform);
    }

    [Fact]
    public void CreateArchive_IncludesBuildFilesUsingRelativePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(directory, "src", "app.txt"), "content");

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            var entries = new List<string>();
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
                entries.Add(entry.Name);

            Assert.Contains("Containerfile", entries);
            Assert.Contains("src/app.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_ResolvesValuelessBuildArgsFromTheEnvironment()
    {
        var service = LoadService("""
            services:
              app:
                build:
                  context: .
                  args: [BUILD_TEST_VALUE, EXPLICIT_EMPTY=]
            """);
        const string environmentVariable = "BUILD_TEST_VALUE";
        var previousValue = Environment.GetEnvironmentVariable(environmentVariable);
        Environment.SetEnvironmentVariable(environmentVariable, "inherited");

        try
        {
            var parameters = DockerBuildParametersFactory.Create(service, options: null);

            Assert.Equal("inherited", parameters.BuildArgs![environmentVariable]);
            Assert.Equal(string.Empty, parameters.BuildArgs["EXPLICIT_EMPTY"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, previousValue);
        }
    }

    [Fact]
    public void GetDockerfileArchivePath_UsesOnDiskCasingOnWindows()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(directory, "dockerfile");

            Assert.Equal("Dockerfile", archivePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_AppliesDockerIgnoreRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "private"));
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "*.log\n[0-9].secret\n!keep.log\nprivate/\n");
        File.WriteAllText(Path.Combine(directory, "ignored.log"), "ignored");
        File.WriteAllText(Path.Combine(directory, "keep.log"), "included");
        File.WriteAllText(Path.Combine(directory, "1.secret"), "ignored");
        File.WriteAllText(Path.Combine(directory, "private", "secret.txt"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.Contains("keep.log", entries);
            Assert.DoesNotContain("ignored.log", entries);
            Assert.DoesNotContain("1.secret", entries);
            Assert.DoesNotContain("private", entries);
            Assert.DoesNotContain("private/secret.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateArchive_RejectsDockerIgnoreNamedPipes(bool dockerfileSpecific)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var ignorePath = Path.Combine(directory, dockerfileSpecific ? "Dockerfile.dockerignore" : ".dockerignore");
        if (dockerfileSpecific)
            File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        if (MkFifo(ignorePath, 0x1A4) != 0)
            throw new IOException("Unable to create a named pipe for the archive test.");

        try
        {
            var exception = Assert.Throws<NotSupportedException>(() => DockerBuildContextArchive.Create(directory));

            Assert.Contains("Docker ignore file", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("named pipe", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_RetainsEmptyDirectoryForChildOnlyDockerIgnorePattern()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "assets/*\n");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.Contains("assets", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_NormalizesDockerIgnorePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "./secrets.env\n");
        File.WriteAllText(Path.Combine(directory, "secrets.env"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("secrets.env", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_HonorsEscapedDockerIgnoreMetacharactersOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "report\\[old\\].txt\n");
        File.WriteAllText(Path.Combine(directory, "report[old].txt"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("report[old].txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_HonorsEscapedClosingBracketsInDockerIgnoreCharacterClassesOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "report[\\]].txt\n");
        File.WriteAllText(Path.Combine(directory, "report].txt"), "secret");
        File.WriteAllText(Path.Combine(directory, "report\\.txt"), "included");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("report].txt", entries);
            Assert.Contains("report\\.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_HonorsEscapedHyphensInDockerIgnoreCharacterClassesOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "report[\\-].txt\n");
        File.WriteAllText(Path.Combine(directory, "report-.txt"), "ignored");
        File.WriteAllText(Path.Combine(directory, "reporta.txt"), "included");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("report-.txt", entries);
            Assert.Contains("reporta.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_MatchesUnicodeScalarsForDockerIgnoreQuestionMarks()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var fileName = $"{char.ConvertFromUtf32(0x1F600)}.txt";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "?.txt\n");
        File.WriteAllText(Path.Combine(directory, fileName), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain(fileName, entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_MatchesUnicodeScalarsForDockerIgnoreCharacterClasses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var fileName = $"{char.ConvertFromUtf32(0x1F600)}.txt";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), $"[{char.ConvertFromUtf32(0x1F600)}].txt\n");
        File.WriteAllText(Path.Combine(directory, fileName), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain(fileName, entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_MatchesUnicodeScalarsForNegatedDockerIgnoreCharacterClasses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var excludedFileName = $"{char.ConvertFromUtf32(0x1F600)}.txt";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), $"[^{char.ConvertFromUtf32(0x1F600)}].txt\n");
        File.WriteAllText(Path.Combine(directory, excludedFileName), "included");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "ignored");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.Contains(excludedFileName, entries);
            Assert.DoesNotContain("a.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_MatchesUnicodeScalarRangesForDockerIgnoreCharacterClasses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var rangeStart = char.ConvertFromUtf32(0x1F600);
        var rangeEnd = char.ConvertFromUtf32(0x1F603);
        var fileName = $"{char.ConvertFromUtf32(0x1F601)}.txt";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), $"[{rangeStart}-{rangeEnd}].txt\n");
        File.WriteAllText(Path.Combine(directory, fileName), "ignored");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "included");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain(fileName, entries);
            Assert.Contains("a.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_MatchesUnicodeScalarsForBmpOnlyNegatedDockerIgnoreCharacterClasses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var fileName = $"{char.ConvertFromUtf32(0x1F600)}.txt";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "[^a].txt\n");
        File.WriteAllText(Path.Combine(directory, fileName), "ignored");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "included");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain(fileName, entries);
            Assert.Contains("a.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_MatchesUnicodeScalarRangesForNegatedDockerIgnoreCharacterClasses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var rangeStart = char.ConvertFromUtf32(0x1F600);
        var rangeEnd = char.ConvertFromUtf32(0x1F603);
        var retainedFileName = $"{char.ConvertFromUtf32(0x1F601)}.txt";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), $"[^{rangeStart}-{rangeEnd}].txt\n");
        File.WriteAllText(Path.Combine(directory, retainedFileName), "included");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "ignored");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.Contains(retainedFileName, entries);
            Assert.DoesNotContain("a.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_MatchesCrossPlaneUnicodeRangesForDockerIgnoreCharacterClasses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var rangeEnd = char.ConvertFromUtf32(0x1F600);
        var fileName = $"{rangeEnd}.txt";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), $"[a-{rangeEnd}].txt\n");
        File.WriteAllText(Path.Combine(directory, "b.txt"), "ignored");
        File.WriteAllText(Path.Combine(directory, fileName), "ignored");
        File.WriteAllText(Path.Combine(directory, $"{char.ConvertFromUtf32(0x1F601)}.txt"), "included");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("b.txt", entries);
            Assert.DoesNotContain(fileName, entries);
            Assert.Contains($"{char.ConvertFromUtf32(0x1F601)}.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_TreatsExclamationMarksAsPositiveDockerIgnoreCharacterClassMembers()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "[!a].txt\n");
        File.WriteAllText(Path.Combine(directory, "!.txt"), "ignored");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "ignored");
        File.WriteAllText(Path.Combine(directory, "b.txt"), "included");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("!.txt", entries);
            Assert.DoesNotContain("a.txt", entries);
            Assert.Contains("b.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_TreatsDescendingDockerIgnoreCharacterClassRangesAsEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "[z-a].txt\n[^z-a].neg\n");
        File.WriteAllText(Path.Combine(directory, "z.txt"), "retained");
        File.WriteAllText(Path.Combine(directory, "a.txt"), "retained");
        File.WriteAllText(Path.Combine(directory, "-.txt"), "retained");
        File.WriteAllText(Path.Combine(directory, "z.neg"), "ignored");
        File.WriteAllText(Path.Combine(directory, $"{char.ConvertFromUtf32(0x1F600)}.neg"), "ignored");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.Contains("z.txt", entries);
            Assert.Contains("a.txt", entries);
            Assert.Contains("-.txt", entries);
            Assert.DoesNotContain("z.neg", entries);
            Assert.DoesNotContain($"{char.ConvertFromUtf32(0x1F600)}.neg", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_HonorsLeadingWhitespaceInDockerIgnorePatterns()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "  #secret\n");
        File.WriteAllText(Path.Combine(directory, "#secret"), "ignored");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("#secret", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PathsAreEqual_ResolvesDirectorySymbolicLinks()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var targetDirectory = Path.Combine(directory, "target");
        var aliasDirectory = Path.Combine(directory, "alias");
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, "archive.tar");
        File.WriteAllText(targetPath, "archive");

        try
        {
            Directory.CreateSymbolicLink(aliasDirectory, targetDirectory);
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            var method = typeof(DockerBuildContextArchive).GetMethod("PathsAreEqual",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            Assert.True((bool)method.Invoke(null, [Path.Combine(aliasDirectory, "archive.tar"), targetPath])!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_SkipsIgnoredDirectoryUnlessAnIncludeRuleCanRestoreEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var ignoredDirectory = Path.Combine(directory, "ignored");
        Directory.CreateDirectory(ignoredDirectory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "ignored/\n!ignored/keep.txt\n");
        File.WriteAllText(Path.Combine(ignoredDirectory, "skip.txt"), "skip");
        File.WriteAllText(Path.Combine(ignoredDirectory, "keep.txt"), "keep");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("ignored/skip.txt", entries);
            Assert.Contains("ignored/keep.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_TraversesIgnoredWildcardDirectoryForIncludedDescendant()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var cacheDirectory = Path.Combine(directory, "cache1");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "cache*/\n!cache*/keep.txt\n");
        File.WriteAllText(Path.Combine(cacheDirectory, "skip.txt"), "skip");
        File.WriteAllText(Path.Combine(cacheDirectory, "keep.txt"), "keep");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("cache1/skip.txt", entries);
            Assert.Contains("cache1/keep.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_RetainsDockerfileNestedInIgnoredDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var dockerDirectory = Path.Combine(directory, "docker");
        Directory.CreateDirectory(dockerDirectory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "docker/\n");
        File.WriteAllText(Path.Combine(dockerDirectory, "Dockerfile"), "FROM scratch");

        try
        {
            var entries = ReadArchiveEntries(directory, "docker/Dockerfile");

            Assert.Contains("docker/Dockerfile", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_AppliesFirstDockerIgnoreRuleWhenFileHasUtf8Bom()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "secrets.env\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(Path.Combine(directory, "secrets.env"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("secrets.env", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesUnixExecutablePermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "entrypoint.sh");
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        File.WriteAllText(scriptPath, "#!/bin/sh\n");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                          UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                          UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "entrypoint.sh")
                    continue;

                Assert.True(entry.Mode.HasFlag(UnixFileMode.UserExecute));
                return;
            }

            Assert.Fail("The executable file was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesFileModificationTime()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "source.txt");
        var modificationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.WriteAllText(filePath, "content");
        File.SetLastWriteTimeUtc(filePath, modificationTime);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "source.txt")
                    continue;

                Assert.Equal(modificationTime, entry.ModificationTime.UtcDateTime);
                return;
            }

            Assert.Fail("The source file was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesUnixDirectoryPermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var privateDirectory = Path.Combine(directory, "private");
        Directory.CreateDirectory(privateDirectory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        File.SetUnixFileMode(privateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "private")
                    continue;

                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, entry.Mode);
                return;
            }

            Assert.Fail("The directory was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesDirectoryModificationTime()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(directory, "source");
        Directory.CreateDirectory(sourceDirectory);
        var modificationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourceDirectory, modificationTime);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "source")
                    continue;

                Assert.Equal(modificationTime, entry.ModificationTime.UtcDateTime);
                return;
            }

            Assert.Fail("The source directory was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_UsesDockerfileSpecificIgnoreFileAndRetainsDockerfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var dockerDirectory = Path.Combine(directory, "docker");
        Directory.CreateDirectory(dockerDirectory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "root-only.txt\n");
        File.WriteAllText(Path.Combine(directory, "root-only.txt"), "root");
        File.WriteAllText(Path.Combine(directory, "custom-secret.txt"), "secret");
        File.WriteAllText(Path.Combine(dockerDirectory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(dockerDirectory, "Containerfile.dockerignore"), "custom-secret.txt\nContainerfile\n");

        try
        {
            var entries = ReadArchiveEntries(directory, "docker/Containerfile");

            Assert.Contains("docker/Containerfile", entries);
            Assert.Contains("root-only.txt", entries);
            Assert.DoesNotContain("custom-secret.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_StagesDockerfileOutsideTheContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        Directory.CreateDirectory(contextDirectory);
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(contextDirectory, "app.txt"), "content");

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(contextDirectory, "../Containerfile");
            var entries = ReadArchiveEntries(contextDirectory, "../Containerfile");

            Assert.Equal("__external_dockerfile__", archivePath);
            Assert.Contains(archivePath, entries);
            Assert.Contains("app.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_RejectsExternalDockerfileNamedPipes()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        var dockerfilePath = Path.Combine(directory, "Dockerfile");
        Directory.CreateDirectory(contextDirectory);
        if (MkFifo(dockerfilePath, 0x1A4) != 0)
            throw new IOException("Unable to create a named pipe for the archive test.");

        try
        {
            var exception = Assert.Throws<NotSupportedException>(() =>
                DockerBuildContextArchive.Create(contextDirectory, "../Dockerfile"));

            Assert.Contains("named pipe", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateArchive_RejectsInternalDockerfileNamedPipes(bool throughSymbolicLink)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pipePath = Path.Combine(directory, throughSymbolicLink ? "recipe" : "Dockerfile");
        if (MkFifo(pipePath, 0x1A4) != 0)
            throw new IOException("Unable to create a named pipe for the archive test.");

        if (throughSymbolicLink)
        {
            try
            {
                File.CreateSymbolicLink(Path.Combine(directory, "Dockerfile"), "recipe");
            }
            catch (IOException)
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
        }

        try
        {
            var exception = Assert.Throws<NotSupportedException>(() => DockerBuildContextArchive.Create(directory));

            Assert.Contains("named pipe", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_StagesDockerfileSymlinkedOutsideTheContextByContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        Directory.CreateDirectory(contextDirectory);
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        try
        {
            File.CreateSymbolicLink(Path.Combine(contextDirectory, "Dockerfile"), "../Containerfile");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(contextDirectory, "Dockerfile");
            using var archive = DockerBuildContextArchive.Create(contextDirectory, "Dockerfile");
            using var reader = new TarReader(archive);
            var entries = new List<TarEntry>();
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
                entries.Add(entry);

            Assert.Equal("__external_dockerfile__", archivePath);
            Assert.DoesNotContain(entries, entry => entry.Name == "Dockerfile");
            Assert.Contains(entries, entry => entry.Name == archivePath && entry.EntryType == TarEntryType.RegularFile);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_UsesRequestedDockerfileSpecificIgnoreFileWhenDockerfileIsLinked()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var dockerDirectory = Path.Combine(directory, "docker");
        Directory.CreateDirectory(dockerDirectory);
        File.WriteAllText(Path.Combine(dockerDirectory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(directory, "Dockerfile.dockerignore"), "secrets.env\n");
        File.WriteAllText(Path.Combine(directory, "secrets.env"), "secret");
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory, "Dockerfile"), "docker/Containerfile");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            var entries = ReadArchiveEntries(directory, "Dockerfile");

            Assert.DoesNotContain("secrets.env", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_StagesDockerfileThroughExternalLinkedDirectoryByContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        var externalDockerDirectory = Path.Combine(directory, "shared-docker");
        Directory.CreateDirectory(contextDirectory);
        Directory.CreateDirectory(externalDockerDirectory);
        File.WriteAllText(Path.Combine(externalDockerDirectory, "Dockerfile"), "FROM scratch");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(contextDirectory, "docker"), "../shared-docker");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(contextDirectory, "docker/Dockerfile");
            using var archive = DockerBuildContextArchive.Create(contextDirectory, "docker/Dockerfile");
            using var reader = new TarReader(archive);
            var entries = new List<TarEntry>();
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
                entries.Add(entry);

            Assert.Equal("__external_dockerfile__", archivePath);
            Assert.Contains(entries, entry => entry.Name == archivePath && entry.EntryType == TarEntryType.RegularFile);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_StagesExternalDockerfileAtAnUnusedPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        Directory.CreateDirectory(contextDirectory);
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(contextDirectory, "__external_dockerfile__"), "context file");

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(contextDirectory, "../Containerfile");
            var entries = ReadArchiveEntries(contextDirectory, "../Containerfile");

            Assert.NotEqual("__external_dockerfile__", archivePath);
            Assert.Equal(1, entries.Count(entry => entry == archivePath));
            Assert.Contains("__external_dockerfile__", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_UsesTheSpecifiedExternalDockerfileArchivePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        Directory.CreateDirectory(contextDirectory);
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(contextDirectory, "__external_dockerfile__"), "unrelated context file");

        try
        {
            using var archive = DockerBuildContextArchive.Create(
                contextDirectory, "../Containerfile", "__external_dockerfile__");
            using var reader = new TarReader(archive);
            var entries = new List<string>();
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
                entries.Add(entry.Name);

            Assert.Equal(1, entries.Count(entry => entry == "__external_dockerfile__"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_ThrowsWhenCancellationIsRequested()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            Assert.Throws<OperationCanceledException>(() => DockerBuildContextArchive.Create(directory, cancellationToken: cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesSymbolicLinks()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "target.txt"), "content");
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory, "linked.txt"), "target.txt");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            Assert.IsType<FileStream>(archive);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "linked.txt")
                    continue;

                Assert.Equal(TarEntryType.SymbolicLink, entry.EntryType);
                Assert.Equal("target.txt", entry.LinkName);
                return;
            }

            Assert.Fail("The symbolic link was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateArchive_PreservesSymbolicLinkModificationTime(bool directoryLink)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, directoryLink ? "target" : "target.txt");
        var linkPath = Path.Combine(directory, directoryLink ? "linked" : "linked.txt");
        var modificationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        if (directoryLink)
            Directory.CreateDirectory(targetPath);
        else
            File.WriteAllText(targetPath, "content");

        try
        {
            if (directoryLink)
                Directory.CreateSymbolicLink(linkPath, Path.GetFileName(targetPath));
            else
                File.CreateSymbolicLink(linkPath, Path.GetFileName(targetPath));
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            File.SetLastWriteTimeUtc(linkPath, modificationTime);

            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != Path.GetFileName(linkPath))
                    continue;

                Assert.Equal(TarEntryType.SymbolicLink, entry.EntryType);
                Assert.Equal(modificationTime, entry.ModificationTime.UtcDateTime);
                return;
            }

            Assert.Fail("The symbolic link was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_NormalizesRelativeSymbolicLinkTargets()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var targetDirectory = Path.Combine(directory, "target");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "file.txt"), "content");
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory, "linked.txt"), "target\\file.txt");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "linked.txt")
                    continue;

                Assert.Equal("target/file.txt", entry.LinkName);
                return;
            }

            Assert.Fail("The symbolic link was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesLiteralBackslashesInUnixSymbolicLinkTargets()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "release\\app"), "content");
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory, "current"), "release\\app");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "current")
                    continue;

                Assert.Equal("release\\app", entry.LinkName);
                return;
            }

            Assert.Fail("The symbolic link was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesNamedPipes()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pipePath = Path.Combine(directory, "events");
        var modificationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        if (MkFifo(pipePath, 0x1A4) != 0)
            throw new IOException("Unable to create a named pipe for the archive test.");

        try
        {
            File.SetLastWriteTimeUtc(pipePath, modificationTime);

            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "events")
                    continue;

                Assert.Equal(TarEntryType.Fifo, entry.EntryType);
                Assert.Equal(modificationTime, entry.ModificationTime.UtcDateTime);
                return;
            }

            Assert.Fail("The named pipe was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_SkipsUnixSockets()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var socketPath = Path.Combine(directory, "server.sock");

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));

            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("server.sock", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(38, true)]
    [InlineData(2, false)]
    public void ShouldFallBackToLStat_HandlesStatXErrors(int error, bool expected)
    {
        var method = typeof(DockerBuildContextArchive).GetMethod("ShouldFallBackToLStat",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var actual = Assert.IsType<bool>(method.Invoke(null, [error]));

        Assert.Equal(expected, actual);
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);

    private static IReadOnlyList<string> ReadArchiveEntries(string directory, string? dockerfile = null)
    {
        using var archive = DockerBuildContextArchive.Create(directory, dockerfile);
        using var reader = new TarReader(archive);
        var entries = new List<string>();
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
            entries.Add(entry.Name);
        return entries;
    }

    private static ComposeSharp.Loader.Models.ServiceDefinition LoadService(string compose)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-parameters-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "compose.yaml");
            File.WriteAllText(path, compose);
            return new ComposeFileLoader().Load(directory, "compose.yaml").Services.Single();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
