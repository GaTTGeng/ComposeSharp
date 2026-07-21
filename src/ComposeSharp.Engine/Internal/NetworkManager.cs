using ComposeSharp.Loader.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

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
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { }
    }

    public async Task EnsureProjectInfrastructureAsync(DockerClient client, string projectName, ComposeProject project, CancellationToken ct)
    {
        var networks = project.Networks.Count > 0 ? project.Networks : (IReadOnlyList<string>)["default"];
        foreach (var network in networks)
            await EnsureNetworkAsync(client, GetNetworkName(projectName, network), projectName, ct);

        foreach (var volume in project.Volumes)
            await EnsureVolumeAsync(client, GetVolumeName(projectName, volume), projectName, ct);
    }

    public async Task CleanupNetworksAsync(DockerClient client, string projectName, CancellationToken ct)
    {
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
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { }
    }

    public async Task CleanupVolumesAsync(DockerClient client, string projectName, CancellationToken ct)
    {
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

    public static string GetNetworkName(string projectName, string network)
        => network.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? $"{projectName}_default"
            : $"{projectName}_{network}";

    public static string GetVolumeName(string projectName, string volume)
        => $"{projectName}_{volume}";

    public static string GetPrimaryNetworkName(string projectName, ComposeProject project, ServiceDefinition service)
    {
        var network = service.Networks.FirstOrDefault()
                      ?? project.Networks.FirstOrDefault()
                      ?? "default";
        return GetNetworkName(projectName, network);
    }
}
