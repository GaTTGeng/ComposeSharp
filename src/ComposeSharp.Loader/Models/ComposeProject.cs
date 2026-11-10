namespace ComposeSharp.Loader.Models;

/// <summary>
/// A loaded Compose project: parsed services plus the top-level resource names and extensions.
/// </summary>
/// <param name="WorkingDirectory">Directory of the primary Compose file; all relative paths resolve from here.</param>
/// <param name="Services">Service definitions in document order.</param>
/// <param name="Volumes">Top-level volume names only; driver options and external flags are not modeled.</param>
/// <param name="Networks">Top-level network names only; driver, IPAM, and external flags are not modeled.</param>
/// <param name="Secrets">Top-level secret names; parsed for inspection but not provisioned.</param>
/// <param name="Configs">Top-level config names; parsed for inspection but not provisioned.</param>
/// <param name="Extensions">String-valued <c>x-*</c> extensions; parsed only, with no engine behavior.</param>
public sealed record ComposeProject(
    string WorkingDirectory,
    IReadOnlyList<ServiceDefinition> Services,
    IReadOnlyList<string> Volumes,
    IReadOnlyList<string> Networks,
    IReadOnlyList<string> Secrets,
    IReadOnlyList<string> Configs,
    IReadOnlyDictionary<string, string> Extensions)
{
    /// <summary>Unsupported or partially supported fields found in the loaded Compose document.</summary>
    public IReadOnlyList<ComposeProjectDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>Describes a Compose field the loader or engine cannot fully apply.</summary>
/// <param name="SourceFile">The Compose file that contributed the field.</param>
/// <param name="PropertyPath">The YAML path of the field.</param>
/// <param name="ServiceName">The service name for a service-level field, or null for project-level fields.</param>
/// <param name="Code">Stable identifier for the diagnostic category.</param>
/// <param name="Message">A value-free description of the unsupported or partial behavior.</param>
public sealed record ComposeProjectDiagnostic(
    string SourceFile,
    string PropertyPath,
    string? ServiceName,
    string Code,
    string Message);
