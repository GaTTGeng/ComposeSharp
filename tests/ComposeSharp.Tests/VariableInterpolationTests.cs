using ComposeSharp.Loader;
using ComposeSharp.Loader.Models;
using ComposeSharp.Loader.Interpolation;

namespace ComposeSharp.Tests;

/// <summary>
/// Covers Compose variable interpolation: value precedence, default/escape syntax, and the
/// validation diagnostics raised when a required variable is missing.
/// </summary>
public sealed class VariableInterpolationTests
{
    // Precedence rule: the process environment wins over values from the project's .env file.
    [Fact]
    public void Expand_PrefersProcessEnvironmentOverDotEnv()
    {
        const string name = "COMPOSESHARP_INTERPOLATION_PRECEDENCE";

        WithEnvironmentVariable(name, "from-process", () =>
        {
            var result = VariableInterpolator.Expand(
                $"${{{name}}}",
                new Dictionary<string, string> { [name] = "from-dotenv" });

            Assert.Equal("from-process", result);
        });
    }

    [Theory]
    [InlineData(StringComparison.Ordinal)]
    [InlineData(StringComparison.OrdinalIgnoreCase)]
    public void Expand_MatchesOverrideNamesUsingPlatformEnvironmentSemantics(StringComparison dictionaryComparer)
    {
        const string name = "composeSharp_case_sensitive_override";

        WithEnvironmentVariable(name, null, () =>
        {
            WithFixture(
                string.Empty,
                string.Empty,
                "services:\n  app:\n    image: ${" + name + "}",
                directory =>
                {
                    var project = new ComposeFileMerger().MergeWithEnv(
                        directory,
                        ["compose.yaml"],
                        new Dictionary<string, string>(
                            dictionaryComparer == StringComparison.OrdinalIgnoreCase
                                ? StringComparer.OrdinalIgnoreCase
                                : StringComparer.Ordinal)
                        {
                            [name.ToUpperInvariant()] = "override"
                        });

                    Assert.Equal(OperatingSystem.IsWindows() ? "override" : string.Empty,
                        Assert.Single(project.Services).Image);
                });
        });
    }

    [Fact]
    public async Task MergeWithEnv_IsolatesConcurrentOverridesWithoutChangingProcessEnvironment()
    {
        const string name = "COMPOSESHARP_MERGE_OVERRIDE_ISOLATION";
        const string processValue = "from-process";
        var previousValue = Environment.GetEnvironmentVariable(name);
        var directories = new[]
        {
            Path.Combine(Path.GetTempPath(), "compose-merge-env-" + Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), "compose-merge-env-" + Guid.NewGuid().ToString("N"))
        };

        try
        {
            foreach (var directory in directories)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, ".env"), $"{name}=from-dotenv");
                File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
                    services:
                      app:
                        image: ${VARIABLE}
                    """.Replace("VARIABLE", name, StringComparison.Ordinal));
            }

            Environment.SetEnvironmentVariable(name, processValue);
            using var startTogether = new Barrier(2);
            var first = MergeWithOverrideAsync(directories[0], name, "first-override", startTogether);
            var second = MergeWithOverrideAsync(directories[1], name, "second-override", startTogether);

            var results = await Task.WhenAll(first, second);

            Assert.Equal("first-override", results[0]);
            Assert.Equal("second-override", results[1]);
            Assert.Equal(processValue, Environment.GetEnvironmentVariable(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previousValue);
            foreach (var directory in directories)
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                catch { /* best effort */ }
            }
        }
    }

    // .env supplies quoted values for interpolation, but service env_file entries only reach the
    // container environment and are never used as interpolation sources.
    [Fact]
    public void Load_UsesDotEnvSupportsQuotedValuesAndKeepsEnvFileOutOfInterpolation()
    {
        WithFixture(
            """
            DOTENV_IMAGE="example/image:1.2 # preserved"
            export DOTENV_LABEL='value with spaces'
            """,
            "ENV_FILE_ONLY=from-env-file",
            """
            services:
              app:
                image: "${DOTENV_IMAGE}"
                labels:
                  quoted: ${DOTENV_LABEL}
                  env-file-source: ${ENV_FILE_ONLY:-default-value}
                env_file: service.env
            """,
            loader =>
            {
                var app = Assert.Single(loader.Services);
                Assert.Equal("example/image:1.2 # preserved", app.Image);
                Assert.Equal("value with spaces", app.Labels["quoted"]);
                Assert.Equal("default-value", app.Labels["env-file-source"]);
                Assert.Contains("ENV_FILE_ONLY=from-env-file", app.Environment);
            });
    }

    // Covers unset expansion, both default forms, and $$ escaping of a literal dollar sign.
    [Theory]
    [InlineData("${MISSING}", "")]
    [InlineData("${MISSING-default}", "default")]
    [InlineData("${MISSING:-default}", "default")]
    [InlineData("$${MISSING}", "${MISSING}")]
    [InlineData("$$MISSING", "$MISSING")]
    public void Expand_HandlesUnsetDefaultsAndEscapedDollars(string input, string expected)
    {
        Assert.Equal(expected, VariableInterpolator.Expand(input, new Dictionary<string, string>()));
    }

    [Fact]
    public void Expand_PreservesPrivateUseCharactersInVariableValues()
    {
        const string name = "COMPOSESHARP_PRIVATE_USE_VALUE";
        const string value = "\uE000";

        WithEnvironmentVariable(name, value, () =>
        {
            Assert.Equal(value, VariableInterpolator.Expand($"${{{name}}}", new Dictionary<string, string>()));
        });
    }

    // A selected fallback value is itself expanded, so nested ${VAR} references resolve.
    [Fact]
    public void Expand_ExpandsVariablesInSelectedFallbackValues()
    {
        const string image = "COMPOSESHARP_IMAGE_FOR_FALLBACK";
        const string defaultImage = "COMPOSESHARP_DEFAULT_IMAGE_FOR_FALLBACK";

        WithEnvironmentVariable(image, null, () =>
        {
            WithEnvironmentVariable(defaultImage, null, () =>
            {
                var result = VariableInterpolator.Expand(
                    $"${{{image}:-${defaultImage}}}",
                    new Dictionary<string, string> { [defaultImage] = "example/image:1.2.3" });

                Assert.Equal("example/image:1.2.3", result);
            });
        });
    }

    // Interpolation failures surface as validation errors at the document root ("$") and name
    // both the missing variable and the compose file that referenced it.
    [Fact]
    public void Load_RequiredVariableFailureNamesVariableAndComposeFile()
    {
        WithFixture(
            string.Empty,
            string.Empty,
            """
            services:
              app:
                image: ${COMPOSESHARP_REQUIRED_IMAGE:?set an image}
            """,
            directory =>
            {
                var exception = Assert.Throws<ComposeValidationException>(
                    () => new ComposeFileLoader().Load(directory, "compose.yaml"));

                Assert.Contains("COMPOSESHARP_REQUIRED_IMAGE", exception.Message);
                Assert.Contains(Path.Combine(directory, "compose.yaml"), exception.Message);
                Assert.Equal("$", exception.PropertyPath);
                Assert.IsType<InvalidOperationException>(exception.InnerException);
            });
    }

    // Writes .env, service.env, and compose.yaml into a temp directory and runs an assertion on the loaded project.
    private static void WithFixture(
        string dotEnv,
        string envFile,
        string composeFile,
        Action<ComposeProject> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "compose-interpolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, ".env"), dotEnv);
            File.WriteAllText(Path.Combine(directory, "service.env"), envFile);
            File.WriteAllText(Path.Combine(directory, "compose.yaml"), composeFile);

            assertion(new ComposeFileLoader().Load(directory, "compose.yaml"));
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // Same fixture as above, but hands the temp directory to the assertion (e.g. for path checks).
    private static void WithFixture(
        string dotEnv,
        string envFile,
        string composeFile,
        Action<string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "compose-interpolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, ".env"), dotEnv);
            File.WriteAllText(Path.Combine(directory, "service.env"), envFile);
            File.WriteAllText(Path.Combine(directory, "compose.yaml"), composeFile);

            assertion(directory);
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static void WithEnvironmentVariable(string name, string? value, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    private static Task<string?> MergeWithOverrideAsync(
        string directory,
        string name,
        string value,
        Barrier startTogether)
    {
        return Task.Run(() =>
        {
            Assert.True(startTogether.SignalAndWait(TimeSpan.FromSeconds(10)));
            var project = new ComposeFileMerger().MergeWithEnv(
                directory,
                ["compose.yaml"],
                new Dictionary<string, string> { [name] = value });
            return Assert.Single(project.Services).Image;
        });
    }
}
