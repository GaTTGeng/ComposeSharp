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
    public void Load_DuplicateFixedBindingsAreCoalesced()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    ports: ['8080:80', '8080:80']\n");
        var ports = Assert.Single(new ComposeFileLoader().Load(_directory, "compose.yaml").Services).Ports;
        Assert.Single(ContainerLifecycle.BuildPortBindings(ports)!["80/tcp"]);
    }

    [Fact]
    public void Load_Ipv4WildcardDoesNotConflictWithIpv6Loopback()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    ports: ['0.0.0.0:8080:80', '[::1]:8080:81']\n");
        var ports = Assert.Single(new ComposeFileLoader().Load(_directory, "compose.yaml").Services).Ports;
        Assert.Equal(2, ports.Count);
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
    public void BuildPortBindings_DeduplicatesIdenticalDynamicBindings()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    ports: ['80', '80']\n");
        var ports = Assert.Single(new ComposeFileLoader().Load(_directory, "compose.yaml").Services).Ports;
        var binding = Assert.Single(ContainerLifecycle.BuildPortBindings(ports)!["80/tcp"]);
        Assert.Equal(string.Empty, binding.HostPort);
    }

    [Fact]
    public void LoadMerged_PreservesDistinctLongPortsAndReplacesEquivalentShortPort()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"), """
            services:
              app:
                image: example
                ports:
                  - target: 80
                    published: '8080'
                  - '127.0.0.1:5353:53/udp'
            """);
        File.WriteAllText(Path.Combine(_directory, "override.yaml"), """
            services:
              app:
                ports:
                  - target: 443
                    published: '8443'
                  - target: 53
                    published: '5353'
                    host_ip: 127.0.0.1
                    protocol: udp
                    name: dns
            """);

        var project = new ComposeFileLoader().LoadMerged(_directory, ["compose.yaml", "override.yaml"]);
        var ports = Assert.Single(project.Services).Ports;
        Assert.Equal(["80/tcp", "53/udp", "443/tcp"], ports.Select(port => port.ContainerPort));
        Assert.Equal("dns", ports[1].Name);
        Assert.Equal("8443", ports[2].HostPort);
    }

    [Fact]
    public void LoadMerged_InvalidLongPortRetainsOverlaySource()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    ports: [{target: 80, published: '8080'}]\n");
        var overlay = Path.Combine(_directory, "override.yaml");
        File.WriteAllText(overlay, "services:\n  app:\n    ports: [{target: 443, mode: ingress}]\n");

        var error = Assert.Throws<ComposeValidationException>(() =>
            new ComposeFileLoader().LoadMerged(_directory, ["compose.yaml", "override.yaml"]));
        Assert.Equal(overlay, error.SourceFile);
        Assert.Equal("services.app.ports[1].mode", error.PropertyPath);
    }

    [Fact]
    public void LoadMerged_OverlayUpdatesOneBindingWithinRange()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    ports: ['8080-8081:80-81']\n");
        File.WriteAllText(Path.Combine(_directory, "override.yaml"),
            "services:\n  app:\n    ports: [{target: 80, published: '8080', name: web}]\n");

        var project = new ComposeFileLoader().LoadMerged(_directory, ["compose.yaml", "override.yaml"]);
        var ports = Assert.Single(project.Services).Ports;
        Assert.Equal(["80/tcp", "81/tcp"], ports.Select(port => port.ContainerPort));
        Assert.Equal("web", ports[0].Name);
        Assert.Equal("8081", ports[1].HostPort);
    }

    [Fact]
    public void LoadMerged_InvalidOverlayAfterRangeRetainsSource()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"),
            "services:\n  app:\n    image: example\n    ports: ['8080-8081:80-81']\n");
        var overlay = Path.Combine(_directory, "override.yaml");
        File.WriteAllText(overlay, "services:\n  app:\n    ports: [{target: 443, mode: ingress}]\n");

        var error = Assert.Throws<ComposeValidationException>(() =>
            new ComposeFileLoader().LoadMerged(_directory, ["compose.yaml", "override.yaml"]));
        Assert.Equal(overlay, error.SourceFile);
        Assert.Equal("services.app.ports[2].mode", error.PropertyPath);
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
