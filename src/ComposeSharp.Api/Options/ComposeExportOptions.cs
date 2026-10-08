namespace ComposeSharp.Api;

/// <summary>Options for exporting a service container's filesystem to a local archive.</summary>
public sealed record ComposeExportOptions
{
    /// <summary>Service whose container filesystem is exported.</summary>
    public required string Service { get; init; }

    /// <summary>Host path the archive is written to.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Replica index within the service; null exports the first matching container.</summary>
    public int? Index { get; init; }
}
