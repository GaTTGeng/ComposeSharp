// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: These public names mirror Docker identifiers and are retained to avoid breaking the API.
// ReSharper disable InconsistentNaming
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>One Compose project discovered on the Docker daemon.</summary>
public sealed record Stack
{
    /// <summary>Project identifier.</summary>
    public required string ID { get; init; }

    /// <summary>Project name as recorded in the project label.</summary>
    public required string Name { get; init; }

    /// <summary>Aggregate status: <c>running</c>, <c>exited</c>, <c>partial</c>, or <c>unknown</c>.</summary>
    /// <remarks>Running and stopped containers together produce <c>partial</c>; unsupported or missing container states produce <c>unknown</c>.</remarks>
    public required string Status { get; init; }

    /// <summary>Compose files associated with the project, when known.</summary>
    public string? ConfigFiles { get; init; }

    /// <summary>Reason for the current status, when one is reported.</summary>
    public string? Reason { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore InconsistentNaming
// ReSharper restore CheckNamespace
