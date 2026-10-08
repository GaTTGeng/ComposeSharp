using System.Runtime.InteropServices;
using Docker.DotNet;

namespace ComposeSharp.Engine;

/// <summary>Creates Docker API clients for the platform's default or a caller-supplied daemon endpoint.</summary>
internal sealed class DockerClientFactory
{
    /// <summary>Creates a client connected to the given socket path, or the platform default when null.</summary>
    public DockerClient CreateClient(string? socketPath = null)
    {
        // Resolve the daemon endpoint, then hand back a fresh client for the caller to dispose.
        var endpoint = GetDockerSocketEndpoint(socketPath);
        return new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
    }

    private static string GetDockerSocketEndpoint(string? socketPath)
    {
        // No path given: fall back to the platform's standard daemon endpoint.
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            // Windows uses a named pipe; Unix-likes use a domain socket.
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "npipe://./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
        }

        // Already a Docker.DotNet-compatible URI scheme: use it verbatim.
        if (socketPath.StartsWith("unix://", StringComparison.OrdinalIgnoreCase) ||
            socketPath.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
            return socketPath;

        // Windows accepts native named-pipe paths; everything else must be an npipe:// URI.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Accept native \\\\.\\pipe\\ paths by rewriting them to the npipe:// URI form Docker.DotNet expects.
            const string namedPipePrefix = @"\\.\pipe\";
            if (socketPath.StartsWith(namedPipePrefix, StringComparison.OrdinalIgnoreCase))
                return "npipe://./pipe/" + socketPath[namedPipePrefix.Length..];

            throw new ArgumentException(
                "On Windows, SocketPath must be an npipe:// URI or a \\\\.\\pipe\\ named-pipe path.",
                nameof(socketPath));
        }

        // Unix-like bare paths become unix:// URIs.
        return $"unix://{socketPath}";
    }
}
