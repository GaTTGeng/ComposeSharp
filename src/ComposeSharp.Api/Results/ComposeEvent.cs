// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: These public names mirror Docker identifiers and are retained to avoid breaking the API.
// ReSharper disable InconsistentNaming
namespace ComposeSharp.Api;

/// <summary>One observed event about a project container.</summary>
public sealed record ComposeEvent
{
    /// <summary>Time the event was observed.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Event subject type, for example <c>container</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Action or state transition the event reports.</summary>
    public required string Action { get; init; }

    /// <summary>Identifier of the Docker resource the event refers to.</summary>
    public required string ID { get; init; }

    /// <summary>Compose service name the event belongs to, when known.</summary>
    public string? Service { get; init; }

    /// <summary>Container ID the event refers to, when the event is container-scoped.</summary>
    public string? Container { get; init; }

    /// <summary>Additional event attributes, typically the resource's labels.</summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }

    /// <summary>Formats the event as a single human-readable line.</summary>
    public override string ToString()
    {
        var ts = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
        var attrs = Attributes is { Count: > 0 }
            ? $" ({string.Join(", ", Attributes.Select(kv => $"{kv.Key}={kv.Value}"))})"
            : "";
        return $"{ts} {Type} {Action} {ID}{attrs}";
    }
}
// ReSharper restore InconsistentNaming
// ReSharper restore CheckNamespace
