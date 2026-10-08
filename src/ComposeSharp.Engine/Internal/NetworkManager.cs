using ComposeSharp.Loader.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Creates and removes the networks and named volumes of a Compose project.
/// Everything is labeled with the project name so create/cleanup stays scoped to that project.
/// </summary>
internal sealed class NetworkManager
{
    public async Task EnsureNetworkAsync(DockerClient client, string name, string projectName, CancellationToken ct)
    {
        try
        {
            await client.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = name,
                Driver = "bridge",
                CheckDuplicate = true,
                Labels = new Dictionary<string, string> { [ComposeSharp.Api.ComposeConstants.ProjectLabel] = projectName }
            }, ct);
        }
        // An existing network is not an error: creation is idempotent by design.
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { }
    }

    public async Task EnsureProjectInfrastructureAsync(DockerClient client, string projectName, ComposeProject project, CancellationToken ct)
    {
        // Even a project that declares no networks gets a "default" so services always have a network.
        var networks = project.Networks.Count > 0 ? project.Networks : (IReadOnlyList<string>)["default"];
        // Networks are ensured first, then named volumes; both creates are idempotent.
        foreach (var network in networks)
            await EnsureNetworkAsync(client, GetNetworkName(projectName, network), projectName, ct);

        foreach (var volume in project.Volumes)
            await EnsureVolumeAsync(client, GetVolumeName(projectName, volume), projectName, ct);
    }

    public async Task CleanupNetworksAsync(DockerClient client, string projectName, CancellationToken ct)
    {
        // Label filter keeps cleanup on project-owned networks only.
        var networks = await client.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = LabelHelper.ProjectLabelFilter(projectName)
        }, ct);

        foreach (var network in networks)
        {
            try { await client.Networks.DeleteNetworkAsync(network.ID, ct); }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }
    }

    private static async Task EnsureVolumeAsync(DockerClient client, string name, string projectName, CancellationToken ct)
    {
        try
        {
            await client.Volumes.CreateAsync(new VolumesCreateParameters
            {
                Name = name,
                Labels = new Dictionary<string, string> { [ComposeSharp.Api.ComposeConstants.ProjectLabel] = projectName }
            }, ct);
        }
        // An existing volume is not an error: creation is idempotent by design.
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { }
    }

    public async Task CleanupVolumesAsync(DockerClient client, string projectName, CancellationToken ct)
    {
        // Label filter keeps cleanup on project-owned volumes only.
        var volumes = await client.Volumes.ListAsync(new VolumesListParameters
        {
            Filters = LabelHelper.ProjectLabelFilter(projectName)
        }, ct);

        foreach (var volume in volumes.Volumes ?? [])
        {
            try { await client.Volumes.RemoveAsync(volume.Name, force: true, ct); }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }
    }

    // Names are project-prefixed (including "default") so two projects never share a network or volume.
    public static string GetNetworkName(string projectName, string network)
        => network.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? $"{projectName}_default"
            : $"{projectName}_{network}";

    public static string GetVolumeName(string projectName, string volume)
        => $"{projectName}_{volume}";

    public static string GetPrimaryNetworkName(string projectName, ComposeProject project, ServiceDefinition service)
    {
        // Preference order: first service network, then first project network, then "default".
        var network = service.Networks.FirstOrDefault()
                      ?? project.Networks.FirstOrDefault()
                      ?? "default";
        return GetNetworkName(projectName, network);
    }
}
