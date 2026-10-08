namespace ComposeSharp.Api;

/// <summary>Options for generating a project configuration summary.</summary>
public sealed record ComposeGenerateOptions
{
    /// <summary>Project name to record in the generated summary; defaults to the context's project name.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Restricts the summary to containers with these names or IDs.</summary>
    public IReadOnlyList<string>? Containers { get; init; }
}
