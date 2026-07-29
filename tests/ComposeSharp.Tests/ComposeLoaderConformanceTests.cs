using ComposeSharp.Loader;
using ComposeSharp.Loader.Models;

namespace ComposeSharp.Tests;

public sealed class ComposeLoaderConformanceTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return ["loader-conformance"];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Load_Fixture_MapsExpectedComposeFields(string scenario)
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", scenario);
        var composeFile = Path.Combine(fixtureDirectory, "compose.yaml");
        var dotEnvTemplate = Path.Combine(fixtureDirectory, ".env.example");
        var dotEnv = Path.Combine(fixtureDirectory, ".env");

        Assert.True(File.Exists(composeFile), $"Fixture source file was not copied: {composeFile}");
        Assert.True(File.Exists(dotEnvTemplate), $"Fixture environment template was not copied: {dotEnvTemplate}");
        File.Copy(dotEnvTemplate, dotEnv, overwrite: true);
        ComposeProject? loadedProject = null;
        var exception = Record.Exception(() => loadedProject = new ComposeFileLoader().Load(fixtureDirectory, "compose.yaml"));
        Assert.True(exception is null, $"Fixture '{scenario}' from '{composeFile}' failed to load: {exception}");

        var project = Assert.IsType<ComposeProject>(loadedProject);
        Assert.Equal(scenario, Path.GetFileName(project.WorkingDirectory));
        Assert.Equal(["app-data"], project.Volumes);
        Assert.Equal(["frontend"], project.Networks);
        Assert.Equal(["app-secret"], project.Secrets);
        Assert.Equal(["app-config"], project.Configs);

        var app = Assert.Single(project.Services);
        Assert.Equal("app", app.Name);
        Assert.Equal("example/app:1.2.3", app.Image);
        Assert.Equal(["dotnet", "ComposeSharp.dll"], app.Command);
        Assert.Equal(["/bin/sh", "-c"], app.Entrypoint);
        Assert.Contains("CONNECTION_STRING=Server=database;Database=app", app.Environment);
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Testing", app.Environment);
        Assert.Contains("FEATURE_FLAG=enabled", app.Environment);
        Assert.Collection(app.Ports,
            port => Assert.Equal(new ComposePort("8080", "8081/tcp", "tcp"), port),
            port => Assert.Equal(new ComposePort("9090", "9091/udp", "udp"), port));
        Assert.Equal(["app-data:/var/lib/app"], app.Volumes);
        Assert.Equal(["frontend"], app.Networks);
        Assert.Equal("on-failure:4", app.Restart);
        Assert.Equal("4", app.RestartMaxRetries);
        Assert.Equal(["debug", "tests"], app.Profiles);
        Assert.Equal(["app-secret"], app.Secrets);
        Assert.Equal(["app-config"], app.Configs);
        Assert.Equal("api", app.Labels["com.example.component"]);

        Assert.NotNull(app.Build);
        Assert.Equal("./app", app.Build!.Context);
        Assert.Equal("Dockerfile.test", app.Build.Dockerfile);
        Assert.Equal("test", app.Build.Args!["BUILD_MODE"]);
        Assert.Equal("runtime", app.Build.Target);
        Assert.Equal(["example/app:test"], app.Build.Tags);
        Assert.Equal("enabled", app.Build.Labels!["build.label"]);
        Assert.Equal(["linux/amd64"], app.Build.Platforms);
        Assert.True(app.Build.Pull);
        Assert.True(app.Build.NoCache);

        Assert.NotNull(app.Healthcheck);
        Assert.False(app.Healthcheck!.Disabled);
        Assert.Equal(["CMD-SHELL", "curl --fail http://localhost:8081/health || exit 1"], app.Healthcheck.Test);
        Assert.Equal(TimeSpan.FromSeconds(30), app.Healthcheck.Interval);
        Assert.Equal(TimeSpan.FromSeconds(5), app.Healthcheck.Timeout);
        Assert.Equal(3, app.Healthcheck.Retries);
        Assert.Equal(TimeSpan.FromSeconds(10), app.Healthcheck.StartPeriod);

        Assert.NotNull(app.Deploy?.Resources);
        Assert.Equal(2, app.Deploy!.Replicas);
        Assert.Equal(512L * 1024 * 1024, app.Deploy.Resources!.Limits!.Memory);
        Assert.Equal(2, app.Deploy.Resources.Limits.CpuCount);
        Assert.Equal(128L * 1024 * 1024, app.Deploy.Resources.Reservations!.Memory);
    }

    [Fact]
    public void LoadMerged_Fixture_AppliesDocumentedFieldRules()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "merged-loader");
        var project = new ComposeFileLoader().LoadMerged(fixtureDirectory,
            ["compose.yaml", "compose.override.yaml"]);

        var app = project.Services.Single(service => service.Name == "app");
        Assert.Equal("example/app:override", app.Image);
        Assert.Equal(["run", "--verbose"], app.Command);
        Assert.Equal(["/bin/app"], app.Entrypoint);
        Assert.Equal(["BASE_ONLY=base", "SHARED=override", "OVERLAY_ONLY=overlay"], app.Environment);
        Assert.Equal("frontend", app.Labels["com.example.tier"]);
        Assert.Equal("api", app.Labels["com.example.component"]);
        Assert.Equal("override", app.Labels["com.example.shared"]);
        Assert.Equal(
            [new ComposePort("8080", "80/tcp", "tcp"), new ComposePort("9090", "81/tcp", "tcp")],
            app.Ports);
        Assert.Equal(["override-data:/var/lib/app", "cache:/cache", "logs:/var/log/app"], app.Volumes);
        Assert.Equal(["frontend", "backend"], app.Networks);
        Assert.Equal(["app-config", "app-config-override"], app.Configs);
        Assert.Equal(["app-secret", "app-secret-override"], app.Secrets);

        Assert.Contains(project.Services, service => service.Name == "worker");
        Assert.Equal(["app-data", "cache", "override-data", "logs"], project.Volumes);
        Assert.Equal(["frontend", "backend"], project.Networks);
        Assert.Equal(["app-config", "app-config-override"], project.Configs);
        Assert.Equal(["app-secret", "app-secret-override"], project.Secrets);
    }

    [Fact]
    public void LoadMerged_RejectsUnsupportedMergeTagWithSourceFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"compose-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "compose.yaml"), "services:\n  app:\n    image: example/app\n");
            var overridePath = Path.Combine(directory, "compose.override.yaml");
            File.WriteAllText(overridePath, "services:\n  app:\n    ports: !reset []\n");

            var exception = Assert.Throws<NotSupportedException>(() => new ComposeFileLoader().LoadMerged(directory,
                ["compose.yaml", "compose.override.yaml"]));

            Assert.Contains("!reset", exception.Message);
            Assert.Contains(overridePath, exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadMerged_ReplacesWindowsBindMountByContainerTarget()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"compose-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
                services:
                  app:
                    image: example/app
                    volumes:
                      - 'C:\base:/data:ro'
                """);
            File.WriteAllText(Path.Combine(directory, "compose.override.yaml"), """
                services:
                  app:
                    volumes:
                      - 'D:\override:/data:rw'
                """);

            var project = new ComposeFileLoader().LoadMerged(directory, ["compose.yaml", "compose.override.yaml"]);

            Assert.Equal(["D:\\override:/data:rw"], Assert.Single(project.Services).Volumes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_AllowsMergeTagTextInCommentsAndBlockScalars()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"compose-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
                # ports: !reset []
                services:
                  app:
                    image: example/app
                    command: |
                      echo !override
                """);

            var project = new ComposeFileLoader().Load(directory, "compose.yaml");

            Assert.Single(project.Services);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
