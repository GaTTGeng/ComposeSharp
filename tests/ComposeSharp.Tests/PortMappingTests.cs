using ComposeSharp.Engine.Internal;
using ComposeSharp.Engine;
using ComposeSharp.Api;
using ComposeSharp.Loader;

namespace ComposeSharp.Tests;

public sealed class PortMappingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public PortMappingTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Load_MapsShortAndLongPortsToExactDockerBindings()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "port-bindings");
        var ports = Assert.Single(new ComposeFileLoader().Load(fixture, "compose.yaml").Services).Ports;
        Assert.Equal("telemetry", ports[5].Name);
        var bindings = ContainerLifecycle.BuildPortBindings(ports)!;
        Assert.Equal(["8000", "8080"], bindings["80/tcp"].Select(binding => binding.HostPort));
        Assert.Equal("8001", Assert.Single(bindings["81/tcp"]).HostPort);
        Assert.Equal("::1", Assert.Single(bindings["53/udp"]).HostIP);
        Assert.Equal("", Assert.Single(bindings["82/tcp"]).HostPort);
        var dynamicBinding = Assert.Single(bindings["84/tcp"]);
        Assert.Equal("", dynamicBinding.HostPort);
        Assert.Equal("127.0.0.1", dynamicBinding.HostIP);
        var ranged = Assert.Single(bindings["83/udp"]);
        Assert.Equal("9000-9010", ranged.HostPort);
        Assert.Equal("127.0.0.1", ranged.HostIP);
    }

    [Theory]
    [InlineData("'8000-8002:80-81'", "services.app.ports[0]")]
    [InlineData("'65536:80'", "services.app.ports[0]")]
    [InlineData("{target: 80, host_ip: invalid}", "services.app.ports[0].host_ip")]
    [InlineData("{target: 80, protocol: invalid}", "services.app.ports[0].protocol")]
    [InlineData("{target: 80, app_protocol: http}", "services.app.ports[0].app_protocol")]
    [InlineData("{target: 80, mode: host}", "services.app.ports[0].mode")]
    public void Load_InvalidOrUnsupportedPort_ReportsContext(string yaml, string path)
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"), $"services:\n  app:\n    image: example\n    ports: [{yaml}]\n");
        var error = Assert.Throws<ComposeValidationException>(() => new ComposeFileLoader().Load(_directory, "compose.yaml"));
        Assert.Equal(path, error.PropertyPath);
        Assert.Equal("app", error.ServiceName);
        Assert.Equal(Path.Combine(_directory, "compose.yaml"), error.SourceFile);
    }

    [Fact]
    public void Load_ConflictingFixedBindings_AreRejected()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"), "services:\n  app:\n    image: example\n    ports: ['8080:80', '127.0.0.1:8080:81']\n");
        var error = Assert.Throws<ComposeValidationException>(() => new ComposeFileLoader().Load(_directory, "compose.yaml"));
        Assert.Equal("services.app.ports[1]", error.PropertyPath);
    }

    [Fact]
    public void Load_HostNetworkWithPublishedPorts_IsRejected()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    network_mode: host\n    ports: ['80']\n");
        var error = Assert.Throws<ComposeValidationException>(() => new ComposeFileLoader().Load(_directory, "compose.yaml"));
        Assert.Equal("services.app.ports", error.PropertyPath);
    }

    [Fact]
    public async Task Up_RejectsUnsupportedPortOptionsBeforeDockerAccess()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    ports: [{target: 80, mode: ingress}]\n");
        var context = new ComposeProjectContext
        {
            ProjectName = "port-validation",
            WorkingDirectory = _directory,
            ComposeFileName = "compose.yaml",
            SocketPath = "npipe://./pipe/port-validation-missing"
        };
        var error = await Assert.ThrowsAsync<ComposeValidationException>(() => new ComposeService().UpAsync(context));
        Assert.Equal("services.app.ports[0].mode", error.PropertyPath);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
