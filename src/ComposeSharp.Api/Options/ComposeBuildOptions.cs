namespace ComposeSharp.Api;

/// <summary>Options controlling image builds for services with a <c>build</c> section.</summary>
public sealed record ComposeBuildOptions
{
    /// <summary>Restricts the build to these service names; null builds every service that defines a build section.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Disables the layer cache so every instruction runs again.</summary>
    public bool NoCache { get; init; }

    /// <summary>Always pull base images before building.</summary>
    public bool Pull { get; init; }

    /// <summary>Suppresses build progress output.</summary>
    public bool Quiet { get; init; }

    /// <summary>Build arguments passed to the Dockerfile, overriding those declared in the Compose file.</summary>
    public IReadOnlyDictionary<string, string>? BuildArgs { get; init; }

    /// <summary>Extra labels applied to the built image.</summary>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>Target platform for the build, for example <c>linux/amd64</c>.</summary>
    public string? Platform { get; init; }

    /// <summary>Progress output format requested from the builder.</summary>
    public string? Progress { get; init; }

    /// <summary>Multi-stage build target to stop at.</summary>
    public string? Target { get; init; }

    /// <summary>Memory limit for the build, as a size string understood by the builder.</summary>
    public string? Memory { get; init; }

    /// <summary>Named builder instance to use instead of the default.</summary>
    public string? Builder { get; init; }

    /// <summary>Receives build progress lines and completion status; null discards progress output.</summary>
    public ILogConsumer? LogConsumer { get; init; }
}
