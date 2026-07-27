using ComposeSharp.Loader;
using ComposeSharp.Loader.Interpolation;

namespace ComposeSharp.Tests;

public sealed class VariableInterpolationTests
{
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
                var exception = Assert.Throws<InvalidOperationException>(
                    () => new ComposeFileLoader().Load(directory, "compose.yaml"));

                Assert.Contains("COMPOSESHARP_REQUIRED_IMAGE", exception.Message);
                Assert.Contains(Path.Combine(directory, "compose.yaml"), exception.Message);
            });
    }

    private static void WithFixture(
        string dotEnv,
        string envFile,
        string composeFile,
        Action<ComposeSharp.Loader.Models.ComposeProject> assertion)
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
            Directory.Delete(directory, recursive: true);
        }
    }

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
            Directory.Delete(directory, recursive: true);
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
}
