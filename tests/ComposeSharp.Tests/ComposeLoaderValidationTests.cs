// Rider inspection rationale: The inline lambda keeps the local assertion or callback context visible.
// ReSharper disable ConvertClosureToMethodGroup
using ComposeSharp.Loader;
using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Loader.Models;
using YamlDotNet.Core;

namespace ComposeSharp.Tests;

/// <summary>
/// Covers loader validation diagnostics: every rejected document or property must report the
/// source file, service name (when applicable), property path, and a message naming the defect.
/// </summary>
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

    // Bracketed IPv6 host syntax ([::1]:8080:80) is accepted; the default protocol suffix is appended to the container port.
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

    [Fact]
    public void Load_UnsupportedFields_ReturnsValueFreeDiagnosticsWithSourceAndPath()
    {
        using var files = new ComposeFiles(
            "mystery: hidden-project-value\nservices:\n  api:\n    image: app\n    logging:\n      driver: private-driver\n    access_token: hidden-service-value\n");

        var project = (ComposeProject)files.Load();

        (string SourceFile, string PropertyPath, string? ServiceName, string Code)[] expected =
        [
            (files.PrimaryPath, "mystery", null, "unsupported-field"),
            (files.PrimaryPath, "services.api.logging", "api", "unsupported-field"),
            (files.PrimaryPath, "services.api.access_token", "api", "unsupported-field")
        ];
        Assert.Equal(
            expected,
            project.Diagnostics.Select(diagnostic =>
                (diagnostic.SourceFile, diagnostic.PropertyPath, diagnostic.ServiceName, diagnostic.Code)));
        Assert.DoesNotContain(project.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_PartialFields_ReturnsDiagnosticsAndLeavesSupportedDefaultsQuiet()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    privileged: false\n    ports:\n      - '127.0.0.1:8080:80'\n  db:\n    image: database\n");

        var project = (ComposeProject)files.Load();

        var diagnostic = Assert.Single(project.Diagnostics);
        Assert.Equal("services.api.ports[0]", diagnostic.PropertyPath);
        Assert.Equal("api", diagnostic.ServiceName);
        Assert.Equal("partially-supported-field", diagnostic.Code);
        Assert.DoesNotContain("127.0.0.1", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NormalizesScalarHealthcheckAndDiagnosesScalarCommands()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    command: echo ready\n    entrypoint: /bin/sh\n    healthcheck:\n      test: echo healthy\n");

        var project = new ComposeFileLoader().Load(files.DirectoryPath, Path.GetFileName(files.PrimaryPath));

        Assert.Equal(["CMD-SHELL", "echo healthy"], Assert.Single(project.Services).Healthcheck!.Test);
        Assert.Equal(
            ["services.api.command", "services.api.entrypoint"],
            project.Diagnostics.Select(diagnostic => diagnostic.PropertyPath));
    }

    [Fact]
    public void Load_MultipleServiceNetworksAreDiagnosedButSingleNetworkIsQuiet()
    {
        using var files = new ComposeFiles(
            "services:\n  list:\n    image: app\n    networks: [front, back]\n  mapping:\n    image: app\n    networks:\n      front: null\n      back: null\n  single:\n    image: app\n    networks: [front]\n");

        var project = (ComposeProject)files.Load();

        Assert.Equal(
            ["services.list.networks", "services.mapping.networks"],
            project.Diagnostics.Select(diagnostic => diagnostic.PropertyPath));
    }

    [Fact]
    public void Load_BuildPrivilegedIsDiagnosedAsNotApplied()
    {
        using var files = new ComposeFiles("services:\n  api:\n    build:\n      context: .\n      privileged: true\n");

        var diagnostic = Assert.Single(((ComposeProject)files.Load()).Diagnostics);

        Assert.Equal("services.api.build.privileged", diagnostic.PropertyPath);
        Assert.Equal("partially-supported-field", diagnostic.Code);
        Assert.Contains("retained but not applied", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_BuildExtensionFieldsAreIgnoredByDiagnostics()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    build:\n      context: .\n      x-cache-key: local\n");

        Assert.Empty(((ComposeProject)files.Load()).Diagnostics);
    }

    [Fact]
    public void Load_NestedExtensionFieldsAreIgnoredByDiagnostics()
    {
        using var files = new ComposeFiles(
            "volumes:\n  data:\n    x-volume-note: local\nnetworks:\n  front:\n    x-network-note: local\nsecrets:\n  credential:\n    x-secret-note: local\nconfigs:\n  settings:\n    x-config-note: local\nservices:\n  api:\n    image: app\n    deploy:\n      x-rollout-note: local\n    networks:\n      front:\n        x-attachment-note: local\n");

        Assert.Empty(((ComposeProject)files.Load()).Diagnostics);
    }

    [Fact]
    public void Load_StringBooleanDefaultsAreQuiet()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    build:\n      context: .\n      privileged: \"false\"\n    oom_kill_disable: \"false\"\n");

        Assert.Empty(((ComposeProject)files.Load()).Diagnostics);
    }

    [Fact]
    public void Load_LongFormEnvironmentFileIsDiagnosed()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    env_file:\n      - path: ./app.env\n        required: true\n");

        var diagnostic = Assert.Single(((ComposeProject)files.Load()).Diagnostics);

        Assert.Equal("services.api.env_file[0]", diagnostic.PropertyPath);
        Assert.Equal("partially-supported-field", diagnostic.Code);
    }

    [Fact]
    public void Load_MultipleBuildPlatformsAreDiagnosed()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    build:\n      context: .\n      platforms: [linux/amd64, linux/arm64]\n");

        var diagnostic = Assert.Single(((ComposeProject)files.Load()).Diagnostics);

        Assert.Equal("services.api.build.platforms", diagnostic.PropertyPath);
        Assert.Equal("partially-supported-field", diagnostic.Code);
    }

    [Fact]
    public void Load_DuplicateContainerPortMappingsAreDiagnosed()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    ports: [\"8080:80\", \"8081:80\"]\n");

        var diagnostic = Assert.Single(((ComposeProject)files.Load()).Diagnostics);

        Assert.Equal("services.api.ports[1]", diagnostic.PropertyPath);
        Assert.Equal("partially-supported-field", diagnostic.Code);
    }

    [Fact]
    public void Load_ServicePlatformIsAppliedAsBuildFallback()
    {
        using var files = new ComposeFiles(
            "services:\n  built:\n    build: .\n    platform: linux/amd64\n  image_only:\n    image: app\n    platform: linux/arm64\n");

        var diagnostic = Assert.Single(((ComposeProject)files.Load()).Diagnostics);

        Assert.Equal("services.image_only.platform", diagnostic.PropertyPath);
        Assert.Equal("unsupported-field", diagnostic.Code);
    }

    [Fact]
    public void Load_LongFormServiceVolumeIsDiagnosed()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    volumes:\n      - type: bind\n        source: ./data\n        target: /data\n    networks:\n      first: {}\n      second: {}\n");

        var diagnostics = ((ComposeProject)files.Load()).Diagnostics;

        Assert.Contains(diagnostics, diagnostic => diagnostic is
            { PropertyPath: "services.api.volumes[0]", Code: "partially-supported-field" });
        Assert.Contains(diagnostics, diagnostic => diagnostic is
            { PropertyPath: "services.api.networks", Code: "partially-supported-field" });
    }

    [Theory]
    [InlineData(@"C:\data:/data")]
    [InlineData(@"d:\data:/data:ro")]
    [InlineData("C:/data:/data")]
    [InlineData("d:/data:/data:rw")]
    public void Load_WindowsDriveVolumeSourcesAreDiagnosed(string volume)
    {
        using var files = new ComposeFiles(
            $"services:\n  api:\n    image: app\n    volumes:\n      - '{volume}'\n");

        var diagnostic = Assert.Single(((ComposeProject)files.Load()).Diagnostics);

        Assert.Equal(files.PrimaryPath, diagnostic.SourceFile);
        Assert.Equal("services.api.volumes[0]", diagnostic.PropertyPath);
        Assert.Equal("api", diagnostic.ServiceName);
        Assert.Equal("partially-supported-field", diagnostic.Code);
        Assert.DoesNotContain(volume, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/host/data:/data:ro")]
    [InlineData("./data:/data")]
    [InlineData("data:/data")]
    [InlineData("c:/data")]
    [InlineData("/data")]
    public void Load_PosixAndNamedVolumeSourcesRemainQuiet(string volume)
    {
        using var files = new ComposeFiles(
            $"services:\n  api:\n    image: app\n    volumes:\n      - '{volume}'\n");

        Assert.Empty(((ComposeProject)files.Load()).Diagnostics);
    }

    [Fact]
    public void Load_UnappliedBuildFieldsAreDiagnosed()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    build:\n      context: .\n      dockerfile_inline: FROM scratch\n");

        var diagnostic = Assert.Single(((ComposeProject)files.Load()).Diagnostics);

        Assert.Equal("services.api.build.dockerfile_inline", diagnostic.PropertyPath);
        Assert.Equal("unsupported-field", diagnostic.Code);
    }

    [Fact]
    public void Load_DependencyConditionsAccountForInheritedHealthchecks()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    depends_on:\n      healthy:\n        condition: service_healthy\n      started:\n        condition: service_started\n      disabled:\n        condition: service_started\n      completed:\n        condition: service_completed_successfully\n  healthy:\n    image: db\n    healthcheck:\n      test: [CMD, true]\n  started:\n    image: cache\n  disabled:\n    image: cache\n    healthcheck:\n      disable: true\n");

        var diagnostics = ((ComposeProject)files.Load()).Diagnostics;

        Assert.Equal(
            ["services.api.depends_on.started", "services.api.depends_on.completed"],
            diagnostics.Select(diagnostic => diagnostic.PropertyPath));
        Assert.All(diagnostics, diagnostic => Assert.Equal("partially-supported-field", diagnostic.Code));
    }

    [Theory]
    [InlineData("test: NONE", "service_started", true)]
    [InlineData("test: NONE", "service_healthy", false)]
    [InlineData("test: [NONE]", "service_started", false)]
    [InlineData("test: [NONE]", "service_healthy", true)]
    [InlineData("disable: true\n      test: NONE", "service_started", false)]
    [InlineData("disable: true\n      test: NONE", "service_healthy", true)]
    public void Load_DependencyConditionsDistinguishScalarNoneFromDisabledHealthchecks(
        string healthcheck, string condition, bool expectDiagnostic)
    {
        using var files = new ComposeFiles(
            $"services:\n  api:\n    image: app\n    depends_on:\n      db:\n        condition: {condition}\n  db:\n    image: database\n    healthcheck:\n      {healthcheck}\n");

        var project = (ComposeProject)files.Load();
        var dependency = Assert.Single(project.Services, service => service.Name == "db");
        string[] expectedTest = healthcheck.Contains("[NONE]", StringComparison.Ordinal)
            ? ["NONE"]
            : ["CMD-SHELL", "NONE"];
        Assert.Equal(expectedTest, dependency.Healthcheck!.Test);
        if (expectDiagnostic)
        {
            var diagnostic = Assert.Single(project.Diagnostics);
            Assert.Equal(files.PrimaryPath, diagnostic.SourceFile);
            Assert.Equal("services.api.depends_on.db", diagnostic.PropertyPath);
            Assert.Equal("api", diagnostic.ServiceName);
            Assert.Equal("partially-supported-field", diagnostic.Code);
        }
        else
        {
            Assert.Empty(project.Diagnostics);
        }
    }

    [Fact]
    public void Load_DefaultZeroValuesForUnsupportedFieldsAreQuiet()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    cpu_quota: 0\n    oom_score_adj: 0\n");

        Assert.Empty(((ComposeProject)files.Load()).Diagnostics);
    }

    [Fact]
    public void LoadMerged_DependencyNameUsesEscapedPathAndOverlayProvenance()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    depends_on:\n      db: {}\n  db:\n    image: database\n  db.prod:\n    image: database\n",
            "services:\n  api:\n    depends_on:\n      db.prod:\n        condition: service_completed_successfully\n");

        var diagnostic = Assert.Single(((ComposeProject)files.LoadMerged()).Diagnostics);

        Assert.Equal(Path.GetFullPath(files.OverlayPath!), diagnostic.SourceFile);
        Assert.Equal("services.api.depends_on[\"db.prod\"]", diagnostic.PropertyPath);
    }

    [Fact]
    public void LoadMerged_UnsupportedMergedMapDiagnosticUsesMeaningfulOverlayProvenance()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    logging: {}\n",
            "services:\n  api:\n    logging:\n      driver: json-file\n");

        var diagnostic = Assert.Single(((ComposeProject)files.LoadMerged()).Diagnostics);

        Assert.Equal(Path.GetFullPath(files.OverlayPath!), diagnostic.SourceFile);
        Assert.Equal("services.api.logging.driver", diagnostic.PropertyPath);
    }

    [Fact]
    public void LoadMerged_UnknownDottedServiceFieldUsesOverlayProvenance()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n",
            "services:\n  api:\n    access.token: hidden\n");

        var diagnostic = Assert.Single(((ComposeProject)files.LoadMerged()).Diagnostics);

        Assert.Equal(Path.GetFullPath(files.OverlayPath!), diagnostic.SourceFile);
        Assert.Equal("services.api[\"access.token\"]", diagnostic.PropertyPath);
        Assert.Equal("api", diagnostic.ServiceName);
    }

    [Fact]
    public void Load_ResourceDefinitionsWithOnlyDefaultValuesAreQuiet()
    {
        using var files = new ComposeFiles(
            "volumes:\n  data:\n    external: false\nnetworks:\n  back:\n    external: false\nservices:\n  api:\n    image: app\n");

        var project = (ComposeProject)files.Load();

        Assert.Empty(project.Diagnostics);
    }

    [Theory]
    [InlineData("networks", "driver: bridge", null)]
    [InlineData("networks", "driver: bridge\n    external: false\n    x-note: local", null)]
    [InlineData("networks", "driver: overlay", "partially-supported-field")]
    [InlineData("networks", "driver: bridge\n    external: true", "partially-supported-field")]
    [InlineData("networks", "driver: bridge\n    driver_opts:\n      custom: value", "partially-supported-field")]
    [InlineData("networks", "driver: bridge\n    internal: true", "partially-supported-field")]
    [InlineData("volumes", "driver: bridge", "partially-supported-field")]
    [InlineData("secrets", "driver: bridge", "unsupported-field")]
    [InlineData("configs", "driver: bridge", "unsupported-field")]
    public void Load_ResourceDriverDefaultsAreScopedToNetworks(
        string section, string definition, string? expectedCode)
    {
        using var files = new ComposeFiles(
            $"{section}:\n  front:\n    {definition}\nservices:\n  api:\n    image: app\n");
        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "resource-default-validation",
            WorkingDirectory = files.DirectoryPath,
            ComposeFileName = Path.GetFileName(files.PrimaryPath),
            ValidationMode = ComposeValidationMode.Strict
        };
        var diagnostics = ((ComposeProject)files.Load()).Diagnostics;

        if (expectedCode is null)
        {
            Assert.Empty(diagnostics);
            Assert.Empty(service.LoadProject(context).Diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(files.PrimaryPath, diagnostic.SourceFile);
            Assert.Equal($"{section}.front", diagnostic.PropertyPath);
            Assert.Null(diagnostic.ServiceName);
            Assert.Equal(expectedCode, diagnostic.Code);
            var exception = Assert.Throws<ComposeValidationException>(() => service.LoadProject(context));
            Assert.Equal(diagnostic.PropertyPath, exception.PropertyPath);
        }
    }

    [Fact]
    public void LoadMerged_ResourceDiagnosticEscapesNameAndUsesOverlayProvenance()
    {
        using var files = new ComposeFiles(
            "networks:\n  front: {}\nservices:\n  api:\n    image: app\n",
            "networks:\n  front.prod:\n    driver: overlay\n");

        var diagnostic = Assert.Single(((ComposeProject)files.LoadMerged()).Diagnostics);

        Assert.Equal(Path.GetFullPath(files.OverlayPath!), diagnostic.SourceFile);
        Assert.Equal("networks[\"front.prod\"]", diagnostic.PropertyPath);
    }

    [Theory]
    [InlineData("front.prod", "services.api.networks[\"front.prod\"]")]
    [InlineData("front[prod]", "services.api.networks[\"front[prod]\"]")]
    [InlineData("back", "services.api.networks.back")]
    public void LoadMerged_ServiceNetworkDiagnosticEscapesNameAndUsesOverlayProvenance(
        string networkName, string expectedPath)
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    networks:\n      front: {}\n",
            $"services:\n  api:\n    networks:\n      '{networkName}':\n        aliases: [private-alias]\n");

        var diagnostics = ((ComposeProject)files.LoadMerged()).Diagnostics;

        Assert.Equal(
            ["services.api.networks", expectedPath],
            diagnostics.Select(diagnostic => diagnostic.PropertyPath));
        var diagnostic = diagnostics[1];
        Assert.Equal(Path.GetFullPath(files.OverlayPath!), diagnostic.SourceFile);
        Assert.Equal("api", diagnostic.ServiceName);
        Assert.Equal("partially-supported-field", diagnostic.Code);
        Assert.DoesNotContain("private-alias", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectDockerOperations_StrictModeRejectsDiagnosticsBeforeDockerAccess()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    logging:\n      driver: json-file\n");
        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "strict-validation",
            WorkingDirectory = files.DirectoryPath,
            ComposeFileName = Path.GetFileName(files.PrimaryPath),
            SocketPath = "npipe://./pipe/compose-sharp-validation-test-missing",
            ValidationMode = ComposeValidationMode.Strict
        };
        var copyPath = Path.Combine(files.DirectoryPath, "copy");
        var exportPath = Path.Combine(files.DirectoryPath, "export.tar");
        Func<Task>[] operations =
        [
            () => service.ExecAsync(context, "api", new ComposeExecOptions { Command = ["true"] }),
            () => service.AttachAsync(context, "api"),
            () => service.CopyAsync(context, new ComposeCopyOptions { Source = "api:/tmp/file", Destination = copyPath }),
            () => service.PortAsync(context, "api", 80),
            () => service.ExportAsync(context, new ComposeExportOptions { Service = "api", OutputPath = exportPath }),
            () => service.CommitAsync(context, new ComposeCommitOptions { Service = "api" }),
            () => service.VolumesAsync(context)
        ];

        foreach (var operation in operations)
        {
            var exception = await Assert.ThrowsAsync<ComposeValidationException>(operation);
            Assert.Equal("services.api.logging", exception.PropertyPath);
        }
    }

    [Fact]
    public void Load_SecretDefinitionsNeverExposeSecretMaterialInDiagnostics()
    {
        using var files = new ComposeFiles(
            "secrets:\n  private_key:\n    file: private-key-material\nservices:\n  api:\n    image: app\n    secrets:\n      - private_key\n    environment:\n      API_TOKEN: private-environment-token\n");

        var project = (ComposeProject)files.Load();

        Assert.NotEmpty(project.Diagnostics);
        foreach (var diagnostic in project.Diagnostics)
        {
            Assert.DoesNotContain("private-key-material", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private-environment-token", diagnostic.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LoadProject_ExposesDiagnosticsInPublicConfiguration()
    {
        using var files = new ComposeFiles(
            "services:\n  api:\n    image: app\n    logging:\n      driver: json-file\n");
        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "diagnostic-summary",
            WorkingDirectory = files.DirectoryPath,
            ComposeFileName = Path.GetFileName(files.PrimaryPath)
        };

        var config = service.LoadProject(context);

        var diagnostic = Assert.Single(config.Diagnostics);
        Assert.Equal("services.api.logging", diagnostic.PropertyPath);
        Assert.Equal("unsupported-field", diagnostic.Code);
    }

    [Theory]
    [InlineData("access_token: hidden-service-value", "services.api.access_token")]
    [InlineData(@"volumes: ['C:\data:/data']", "services.api.volumes[0]")]
    [InlineData("volumes: ['d:/data:/data:ro']", "services.api.volumes[0]")]
    [InlineData("depends_on:\n      db:\n        condition: service_started\n  db:\n    image: database\n    healthcheck:\n      test: NONE", "services.api.depends_on.db")]
    public async Task Up_StrictModeRejectsUnsupportedFieldsBeforeDockerAccess(string properties, string expectedPath)
    {
        using var files = new ComposeFiles(
            $"services:\n  api:\n    image: app\n    {properties}\n");
        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "strict-validation",
            WorkingDirectory = files.DirectoryPath,
            ComposeFileName = Path.GetFileName(files.PrimaryPath),
            SocketPath = "npipe://./pipe/compose-sharp-validation-test-missing",
            ValidationMode = ComposeValidationMode.Strict
        };

        var exception = await Assert.ThrowsAsync<ComposeValidationException>(() => service.UpAsync(context));

        Assert.Equal(expectedPath, exception.PropertyPath);
        Assert.DoesNotContain("hidden-service-value", exception.Message, StringComparison.Ordinal);
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

    // YAML syntax errors keep the parser's line/column and surface the parser exception as the inner exception.
    [Fact]
    public void Load_MalformedYaml_RetainsParserLocationAndInnerException()
    {
        using var files = new ComposeFiles("services:\n  api: [\n");

        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());

        AssertProjectDiagnostic(exception, files.PrimaryPath, "$", "malformed");
        var parserException = Assert.IsAssignableFrom<YamlException>(exception.InnerException);
        Assert.Equal(parserException.Start.Line, exception.Line);
        Assert.Equal(parserException.Start.Column, exception.Column);
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

    // Provenance rule: a service whose name contains dots must not steal field-level source attribution
    // from a sibling field of a similarly named service.
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

    // Merged lists keep per-item provenance: an invalid base item still reports the base file even
    // after the overlay appends further items.
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

    // Appended overlay items report the overlay file and their index in the merged list.
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

    [Theory]
    [InlineData("on-failure:")]
    [InlineData("on-failure:-1")]
    [InlineData("on-failure:+1")]
    [InlineData("on-failure: 5")]
    [InlineData("on-failure:1.5")]
    [InlineData("on-failure:abc")]
    [InlineData("on-failure:2147483648")]
    [InlineData("on-failure:5:6")]
    [InlineData("always:5")]
    public void Load_InvalidRestartSuffix_ReportsFullContext(string restart)
    {
        using var files = new ComposeFiles($"services:\n  api:\n    image: app\n    restart: '{restart}'\n");
        var exception = Assert.Throws<ComposeValidationException>(() => files.Load());
        AssertServiceDiagnostic(exception, files.PrimaryPath, "api", "services.api.restart", "on-failure:N");
    }

    [Theory]
    [InlineData("on-failure")]
    [InlineData("on-failure:0")]
    [InlineData("on-failure:5")]
    [InlineData("on-failure:2147483647")]
    public void Load_ValidRestartSuffix_PreservesValue(string restart)
    {
        using var files = new ComposeFiles($"services:\n  api:\n    image: app\n    restart: '{restart}'\n");
        var project = new ComposeFileLoader().Load(files.DirectoryPath, "compose.yaml");
        Assert.Equal(restart, Assert.Single(project.Services).Restart);
    }

    // Asserts project-level diagnostics carry the source file and root property path but no service name.
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

    // Asserts service-level diagnostics carry source file, service name, property path, and message text.
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

    // Disposable temp compose project with an optional override file for merge scenarios.
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

        public void Dispose()
        {
            try { Directory.Delete(DirectoryPath, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Temporary-directory cleanup must not hide the test result.
            }
        }
    }
}
// ReSharper restore ConvertClosureToMethodGroup
