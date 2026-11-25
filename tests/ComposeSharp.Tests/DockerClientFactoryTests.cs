using System.Runtime.InteropServices;
using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Engine.Internal;

namespace ComposeSharp.Tests;

public class DockerClientFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDockerSocketEndpoint_UsesPlatformDefaultWhenUnset(string? socketPath)
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";

        Assert.Equal(expected, DockerClientFactory.GetDockerSocketEndpoint(socketPath));
    }

    [Theory]
    [InlineData("npipe://./pipe/custom_docker")]
    [InlineData("unix:///tmp/custom-docker.sock")]
    public void GetDockerSocketEndpoint_PreservesExplicitSupportedUris(string uri)
    {
        Assert.Equal(uri, DockerClientFactory.GetDockerSocketEndpoint(uri));
    }

    [Fact]
    public void GetDockerSocketEndpoint_NormalizesBarePlatformPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal(
                "npipe://./pipe/custom_engine",
                DockerClientFactory.GetDockerSocketEndpoint(@"\\.\pipe\custom_engine"));
            Assert.Throws<ArgumentException>(() => DockerClientFactory.GetDockerSocketEndpoint("/var/run/docker.sock"));
            return;
        }

        Assert.Equal(
            "unix:///var/run/custom.sock",
            DockerClientFactory.GetDockerSocketEndpoint("/var/run/custom.sock"));
    }

    [Fact]
    public void BuildNetworkCreateParameters_LeavesDriverUnsetForDaemonDefault()
    {
        var parameters = NetworkManager.BuildNetworkCreateParameters("demo_default", "demo");

        Assert.Equal("demo_default", parameters.Name);
        Assert.Null(parameters.Driver);
        Assert.True(parameters.CheckDuplicate);
        Assert.NotNull(parameters.Labels);
        Assert.Equal("demo", parameters.Labels[ComposeConstants.ProjectLabel]);
    }
}
