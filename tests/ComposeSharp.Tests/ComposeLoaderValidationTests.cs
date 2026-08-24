using ComposeSharp.Loader;
using YamlDotNet.Core;

namespace ComposeSharp.Tests;

public sealed class ComposeLoaderValidationTests
{
    [Fact]
    public void Load_EmptyDocument_ReportsSourceAndRootPath()
    {
        using var files = new ComposeFiles(string.Empty);

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertProjectDiagnostic(exception, files.PrimaryPath, "$", "empty");
    }

    [Theory]
    [InlineData("name: sample\n", "missing")]
    [InlineData("services: []\n", "mapping")]
    public void Load_InvalidServicesNode_ReportsSourceAndProjectPath(string yaml, string message)
    {
        using var files = new ComposeFiles(yaml);

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertProjectDiagnostic(exception, files.PrimaryPath, "services", message);
    }

    [Fact]
    public void Load_NonMappingService_ReportsSourceServiceAndPath()
    {
        using var files = new ComposeFiles("services:\n  api: image-name\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", "services.api", "mapping");
    }

    [Fact]
    public void Load_ServiceWithoutImageOrBuild_ReportsSourceServiceAndPath()
    {
        using var files = new ComposeFiles("services:\n  api:\n    command: run\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", "services.api", "image");
    }

    [Fact]
    public void Load_InvalidEnvironmentShape_ReportsFullContext()
    {
        using var files = new ComposeFiles("services:\n  api:\n    image: app\n    environment: invalid\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", "services.api.environment", "list or mapping");
    }

    [Theory]
    [InlineData("not-a-port", "services.api.ports[0]", "not a supported short port")]
    [InlineData("target: 80", "services.api.ports[0]", "long port syntax")]
    public void Load_InvalidPort_ReportsFullContext(string portYaml, string path, string message)
    {
        using var files = new ComposeFiles($"services:\n  api:\n    image: app\n    ports:\n      - {portYaml}\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", path, message);
    }

    [Fact]
    public void Load_BracketedIpv6PortMapping_IsAccepted()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    ports:\n      - '[::1]:8080:80'\n");

        var project = new ComposeFileLoader().Load(files.DirectoryPath, "compose.yaml");

        var port = Assert.Single(Assert.Single(project.Services).Ports);
        Assert.Equal("8080", port.HostPort);
        Assert.Equal("80/tcp", port.ContainerPort);
    }

    [Theory]
    [InlineData("healthcheck:\n      interval: eventually", "services.api.healthcheck.interval")]
    [InlineData("stop_grace_period: eventually", "services.api.stop_grace_period")]
    public void Load_InvalidDuration_ReportsFullContext(string propertyYaml, string path)
    {
        using var files = new ComposeFiles($"services:\n  api:\n    image: app\n    {propertyYaml}\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", path, "duration");
        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Theory]
    [InlineData("shm_size: huge", "services.api.shm_size")]
    [InlineData("mem_limit: 1.5G", "services.api.mem_limit")]
    [InlineData("deploy:\n      resources:\n        limits:\n          memory: huge", "services.api.deploy.resources.limits.memory")]
    public void Load_InvalidByteValue_ReportsFullContext(string propertyYaml, string path)
    {
        using var files = new ComposeFiles($"services:\n  api:\n    image: app\n    {propertyYaml}\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", path, "byte value");
    }

    [Theory]
    [InlineData("deploy:\n      replicas: many", "services.api.deploy.replicas", "integer")]
    [InlineData("healthcheck:\n      disable: sometimes", "services.api.healthcheck.disable", "boolean")]
    [InlineData("deploy:\n      update_config:\n        max_failure_ratio: often", "services.api.deploy.update_config.max_failure_ratio", "numeric")]
    public void Load_InvalidScalarConversion_ReportsFullContext(string propertyYaml, string path, string message)
    {
        using var files = new ComposeFiles($"services:\n  api:\n    image: app\n    {propertyYaml}\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", path, message);
        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Theory]
    [InlineData("build: []", "services.api.build", "scalar or YAML mapping")]
    [InlineData("deploy: []", "services.api.deploy", "YAML mapping")]
    [InlineData("healthcheck: []", "services.api.healthcheck", "YAML mapping")]
    [InlineData("logging: []", "services.api.logging", "YAML mapping")]
    [InlineData("extends: []", "services.api.extends", "scalar or YAML mapping")]
    [InlineData("deploy:\n      resources: []", "services.api.deploy.resources", "YAML mapping")]
    [InlineData("deploy:\n      resources:\n        limits: []", "services.api.deploy.resources.limits", "YAML mapping")]
    [InlineData("deploy:\n      resources:\n        reservations: []", "services.api.deploy.resources.reservations", "YAML mapping")]
    public void Load_InvalidNestedShape_ReportsFullContext(string propertyYaml, string path, string message)
    {
        using var files = new ComposeFiles($"services:\n  api:\n    image: app\n    {propertyYaml}\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", path, message);
    }

    [Fact]
    public void Load_MalformedYaml_RetainsParserLocationAndInnerException()
    {
        using var files = new ComposeFiles("services:\n  api: [\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertProjectDiagnostic(exception, files.PrimaryPath, "$", "malformed");
        Assert.NotNull(exception.Line);
        Assert.NotNull(exception.Column);
        Assert.IsAssignableFrom<YamlException>(exception.InnerException);
    }

    [Fact]
    public void LoadMerged_InvalidBaseNode_ReportsBaseSource()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    environment: invalid\n",
            "services:\n  api:\n    labels:\n      overlay: true\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.LoadMerged());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", "services.api.environment", "list or mapping");
    }

    [Fact]
    public void LoadMerged_InvalidOverlayNode_ReportsOverlaySource()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n",
            "services:\n  api:\n    build: []\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.LoadMerged());

        AssertServiceDiagnostic(exception, files.OverlayPath!, "api", "services.api.build", "scalar or YAML mapping");
    }

    [Fact]
    public void LoadMerged_DottedServiceName_DoesNotOverwriteSiblingFieldProvenance()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: []\n",
            "services:\n  api.image:\n    image: app\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.LoadMerged());

        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", "services.api.image", "scalar");
    }

    [Theory]
    [InlineData("image: app", "image: null", "services.api.image")]
    [InlineData("build: .", "build: null", "services.api.build")]
    public void LoadMerged_OverlayRemovingOnlyImageOrBuild_ReportsOverlaySource(
        string baseProperty,
        string overlayProperty,
        string path)
    {
        using var files = new ComposeFiles(
            $"services:\n  api:\n    {baseProperty}\n",
            $"services:\n  api:\n    {overlayProperty}\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.LoadMerged());

        AssertServiceDiagnostic(exception, files.OverlayPath!, "api", path, "image");
    }

    [Fact]
    public void LoadMerged_InvalidBaseListItem_ReportsBaseSourceAfterOverlayAppend()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    ports: [invalid]\n",
            "services:\n  api:\n    ports: [8080:80]\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.LoadMerged());

        AssertServiceDiagnostic(
            exception, files.PrimaryPath, "api", "services.api.ports[0]", "short port mapping");
    }

    [Fact]
    public void LoadMerged_InvalidAppendedListItem_ReportsOverlaySource()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    ports: [8080:80]\n",
            "services:\n  api:\n    ports: [invalid]\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.LoadMerged());

        AssertServiceDiagnostic(
            exception, files.OverlayPath!, "api", "services.api.ports[1]", "short port mapping");
    }

    private static void AssertProjectDiagnostic(
        ComposeValidationException exception,
        string source,
        string path,
        string message)
    {
        Assert.Equal(Path.GetFullPath(source), exception.SourceFile);
        Assert.Equal(path, exception.PropertyPath);
        Assert.Null(exception.ServiceName);
        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(source), exception.Message, StringComparison.Ordinal);
    }

    private static void AssertServiceDiagnostic(
        ComposeValidationException exception,
        string source,
        string service,
        string path,
        string message)
    {
        Assert.Equal(Path.GetFullPath(source), exception.SourceFile);
        Assert.Equal(service, exception.ServiceName);
        Assert.Equal(path, exception.PropertyPath);
        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(source), exception.Message, StringComparison.Ordinal);
        Assert.Contains(service, exception.Message, StringComparison.Ordinal);
    }

    private sealed class ComposeFiles : IDisposable
    {
        public ComposeFiles(string primary, string? overlay = null)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"compose-validation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            PrimaryPath = Path.Combine(DirectoryPath, "compose.yaml");
            File.WriteAllText(PrimaryPath, primary);
            if (overlay is not null)
            {
                OverlayPath = Path.Combine(DirectoryPath, "compose.override.yaml");
                File.WriteAllText(OverlayPath, overlay);
            }
        }

        public string DirectoryPath { get; }
        public string PrimaryPath { get; }
        public string? OverlayPath { get; }

        public object Load() => new ComposeFileLoader().Load(DirectoryPath, Path.GetFileName(PrimaryPath));

        public object LoadMerged() => new ComposeFileLoader().LoadMerged(
            DirectoryPath,
            [Path.GetFileName(PrimaryPath), Path.GetFileName(OverlayPath!)]);

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
