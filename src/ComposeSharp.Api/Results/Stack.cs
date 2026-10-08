namespace ComposeSharp.Api;

/// <summary>One Compose project discovered on the Docker daemon.</summary>
public sealed record Stack
{
    /// <summary>Project identifier.</summary>
    public required string ID { get; init; }

    /// <summary>Project name as recorded in the project label.</summary>
    public required string Name { get; init; }

    /// <summary>Aggregate status of the project.</summary>
    public required string Status { get; init; }

    /// <summary>Compose files associated with the project, when known.</summary>
    public string? ConfigFiles { get; init; }

    /// <summary>Reason for the current status, when one is reported.</summary>
    public string? Reason { get; init; }
}
