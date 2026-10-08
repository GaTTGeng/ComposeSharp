namespace ComposeSharp.Api;

/// <summary>Options controlling which container log lines are streamed.</summary>
public sealed record ComposeLogsOptions
{
    /// <summary>Restricts log streaming to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Keep streaming new lines after the existing backlog is delivered.</summary>
    public bool Follow { get; init; }

    /// <summary>Only include lines at or after this timestamp or duration (for example <c>2024-01-01</c> or <c>10m</c>).</summary>
    public string? Since { get; init; }

    /// <summary>Only include lines at or before this timestamp or duration.</summary>
    public string? Until { get; init; }

    /// <summary>Prefix each line with its timestamp.</summary>
    public bool Timestamps { get; init; }

    /// <summary>Number of trailing lines to deliver, or <c>all</c> for the full backlog.</summary>
    public string Tail { get; init; } = "all";

    /// <summary>Strip ANSI color codes from the streamed lines.</summary>
    public bool NoColor { get; init; }

    /// <summary>Drop the per-line service-name prefix.</summary>
    public bool NoLogPrefix { get; init; }
}
