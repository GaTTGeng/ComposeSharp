using System.Runtime.InteropServices;
using Docker.DotNet;

namespace ComposeSharp.Engine;

internal sealed class DockerClientFactory
{
    public DockerClient CreateClient(string? socketPath = null)
    {
        var endpoint = GetDockerSocketEndpoint(socketPath);
        return new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
    }

    private static string GetDockerSocketEndpoint(string? socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "npipe://./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
        }

        if (socketPath.StartsWith("unix://", StringComparison.OrdinalIgnoreCase) ||
            socketPath.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
            return socketPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            const string namedPipePrefix = @"\\.\pipe\";
            if (socketPath.StartsWith(namedPipePrefix, StringComparison.OrdinalIgnoreCase))
                return "npipe://./pipe/" + socketPath[namedPipePrefix.Length..];

            throw new ArgumentException(
                "On Windows, SocketPath must be an npipe:// URI or a \\\\.\\pipe\\ named-pipe path.",
                nameof(socketPath));
        }

        return $"unix://{socketPath}";
    }
}
