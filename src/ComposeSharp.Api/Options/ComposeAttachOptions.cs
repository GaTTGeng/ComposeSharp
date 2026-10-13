// Public SDK contracts are consumed outside this solution, so local usage analysis cannot
// determine whether their types, accessors, or init setters are needed by callers.
// ReSharper disable CheckNamespace
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for attaching to a service container's output streams.</summary>
public sealed record ComposeAttachOptions
{
    /// <summary>Service to attach to; null selects the project's single running service container.</summary>
    public string? ServiceName { get; init; }

    /// <summary>Replica index within the service; null picks the first matching container.</summary>
    public int? Index { get; init; }

    /// <summary>Also forward standard input to the container.</summary>
    public bool Stdin { get; init; }

    /// <summary>Include the container's standard output stream.</summary>
    public bool Stdout { get; init; } = true;

    /// <summary>Include the container's standard error stream.</summary>
    public bool Stderr { get; init; } = true;

    /// <summary>Key sequence that detaches the client, in Docker's detach-keys format.</summary>
    public string? DetachKeys { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore AutoPropertyCanBeMadeGetOnly.Global
// ReSharper restore CheckNamespace
