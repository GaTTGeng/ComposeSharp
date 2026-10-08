namespace ComposeSharp.Api;

/// <summary>Process listing of one service container.</summary>
public sealed record ContainerProcSummary
{
    /// <summary>Container ID the processes belong to.</summary>
    public required string ID { get; init; }

    /// <summary>Container name.</summary>
    public required string Name { get; init; }

    /// <summary>Compose service name the container belongs to.</summary>
    public required string Service { get; init; }

    /// <summary>Replica index of the container within its service.</summary>
    public string? Replica { get; init; }

    /// <summary>Column titles of the process listing.</summary>
    public IReadOnlyList<string> Titles { get; init; } = [];

    /// <summary>One entry per process, with fields aligned to <see cref="Titles"/>.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Processes { get; init; } = [];
}
