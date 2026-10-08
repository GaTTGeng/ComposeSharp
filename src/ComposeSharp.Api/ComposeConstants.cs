namespace ComposeSharp.Api;

/// <summary>
/// Compose label keys applied to engine-created resources.
/// These labels keep Docker resources scoped to a project so they stay queryable and removable
/// without touching unrelated containers, networks, or volumes.
/// </summary>
public static class ComposeConstants
{
    /// <summary>Label carrying the Compose project name; the primary ownership marker for a resource.</summary>
    public const string ProjectLabel = "com.docker.compose.project";

    /// <summary>Label carrying the service name a container belongs to within its project.</summary>
    public const string ServiceLabel = "com.docker.compose.service";

    /// <summary>Label carrying the replica index of a container within its service.</summary>
    public const string ContainerNumberLabel = "com.docker.compose.container-number";

    /// <summary>Label marking one-off containers created by <c>run</c>-style operations.</summary>
    public const string OneOffLabel = "com.docker.compose.oneoff";

    /// <summary>Label carrying the hash of the configuration a container was created from.</summary>
    public const string ConfigHashLabel = "com.docker.compose.config-hash";

    /// <summary>Label recording which Compose implementation version created the resource.</summary>
    public const string VersionLabel = "com.docker.compose.version";

    /// <summary>Label recording the project working directory used when the resource was created.</summary>
    public const string WorkingDirLabel = "com.docker.compose.project.working_dir";

    /// <summary>Label listing the Compose files that produced the resource.</summary>
    public const string ConfigFilesLabel = "com.docker.compose.project.config_files";
}
