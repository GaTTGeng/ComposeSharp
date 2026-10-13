// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: These public names mirror Docker identifiers and are retained to avoid breaking the API.
// ReSharper disable InconsistentNaming
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Summary of one image used by the project.</summary>
public sealed record ImageSummary
{
    /// <summary>Image ID.</summary>
    public required string ID { get; init; }

    /// <summary>Repository part of the image reference.</summary>
    public required string Repository { get; init; }

    /// <summary>Tag part of the image reference.</summary>
    public required string Tag { get; init; }

    /// <summary>Platform the image targets, for example <c>linux/amd64</c>.</summary>
    public string? Platform { get; init; }

    /// <summary>Image size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Time the image was created.</summary>
    public DateTime? Created { get; init; }

    /// <summary>Time the image was last tagged.</summary>
    public DateTime? LastTagTime { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore InconsistentNaming
// ReSharper restore CheckNamespace
