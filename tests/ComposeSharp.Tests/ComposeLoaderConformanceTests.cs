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
}
