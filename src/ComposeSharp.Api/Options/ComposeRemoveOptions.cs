namespace ComposeSharp.Api;

/// <summary>Options for removing the project's containers.</summary>
public sealed record ComposeRemoveOptions
{
    /// <summary>Restricts the removal to these service names; null removes every project service's containers.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Remove containers even if they cannot be stopped cleanly.</summary>
    public bool Force { get; init; }

    /// <summary>Stop running containers before removing them.</summary>
    public bool Stop { get; init; }

    /// <summary>Also remove anonymous volumes attached to the containers.</summary>
    public bool Volumes { get; init; }
}
