using ComposeSharp.Api;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Lists processes inside the project's running service containers through the Docker Engine
/// process-list API and maps the response onto <see cref="ContainerProcSummary"/> rows.
/// </summary>
internal sealed class ProcessInspector
{
    /// <summary>
    /// Returns one process listing per running project container of the selected services.
    /// Non-running containers are skipped because the Docker process-list API only covers running ones.
    /// </summary>
    public async Task<IReadOnlyList<ContainerProcSummary>> ListProcessesAsync(
        DockerClient client,
        string projectName,
        IReadOnlySet<string>? services,
        CancellationToken ct)
    {
        // Project label keeps the listing inside this Compose project; the service filter narrows it further.
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = false,
            Filters = LabelHelper.ProjectLabelFilter(projectName)
        }, ct);

        var results = new List<ContainerProcSummary>();
        foreach (var container in containers
                     .Where(c => MatchesService(c, services))
                     .OrderBy(c => LabelHelper.GetServiceName(c.Labels) ?? c.ID, StringComparer.Ordinal)
                     .ThenBy(c => GetReplica(c), StringComparer.Ordinal))
        {
            // ListContainers with All=false already restricts to running containers; keep the guard
            // because a container can exit between the listing and the process query below.
            if (!string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase))
                continue;

            ContainerProcessesResponse processes;
            try
            {
                processes = await client.Containers.ListProcessesAsync(
                    container.ID, new ContainerListProcessesParameters(), ct);
            }
            catch (DockerApiException) when (ct.IsCancellationRequested == false)
            {
                // Exited or removed between listing and top; docker compose top also omits such rows.
                continue;
            }

            results.Add(Map(container, processes));
        }

        return results;
    }

    /// <summary>Maps one Docker process listing onto the public summary shape.</summary>
    internal static ContainerProcSummary Map(ContainerListResponse container, ContainerProcessesResponse processes)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        var name = container.Names?.FirstOrDefault() ?? container.ID;
        if (name.StartsWith('/'))
            name = name[1..];

        return new ContainerProcSummary
        {
            ID = container.ID,
            Name = name,
            Service = LabelHelper.GetServiceName(labels) ?? string.Empty,
            Replica = labels.TryGetValue(ComposeConstants.ContainerNumberLabel, out var replica) ? replica : null,
            Titles = (processes.Titles ?? []).Select(t => t ?? string.Empty).ToList(),
            Processes = (processes.Processes ?? [])
                .Select(row => (IReadOnlyList<string>)(row ?? []).Select(cell => cell ?? string.Empty).ToList())
                .ToList()
        };
    }

    // Service matching goes through the service label so foreign containers are never listed.
    private static bool MatchesService(ContainerListResponse container, IReadOnlySet<string>? services)
    {
        if (services is null)
            return true;

        var service = LabelHelper.GetServiceName(container.Labels);
        return service is not null && services.Contains(service);
    }

    private static string GetReplica(ContainerListResponse container)
        => container.Labels != null && container.Labels.TryGetValue(ComposeConstants.ContainerNumberLabel, out var replica)
            ? replica
            : string.Empty;
}
