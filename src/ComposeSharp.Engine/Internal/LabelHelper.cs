using ComposeSharp.Api;
using ComposeSharp.Loader.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Builds and queries the Compose labels that mark Docker resources as owned by a project.
/// Resource ownership is constrained to these labels: the engine only lists and mutates resources
/// carrying its project/service labels and never scans or modifies arbitrary Docker resources.
/// </summary>
internal sealed class LabelHelper
{
    public Dictionary<string, string> CreateServiceLabels(string projectName, ServiceDefinition service, int index, bool oneOff = false)
    {
        var labels = new Dictionary<string, string>
        {
            [ComposeConstants.ProjectLabel] = projectName,
            [ComposeConstants.ServiceLabel] = service.Name,
            // ContainerNumber is the 1-based replica index; one-off run containers use 0 and
            // OneOff=true so service reconciliation can ignore them when scaling or recreating.
            [ComposeConstants.ContainerNumberLabel] = index.ToString(),
            [ComposeConstants.OneOffLabel] = oneOff.ToString(),
            // Fingerprint of the fields that imply a container recreate; kept as a label so the
            // configuration drift is visible on the container itself.
            [ComposeConstants.ConfigHashLabel] = CreateConfigHash(service)
        };

        foreach (var (key, value) in service.Labels)
            labels[key] = value;

        return labels;
    }

    public Dictionary<string, string> CreateProjectLabels(string projectName, string? configFiles = null)
    {
        var labels = new Dictionary<string, string>
        {
            [ComposeConstants.ProjectLabel] = projectName,
            [ComposeConstants.VersionLabel] = "2.0.0"
        };
        if (configFiles is not null)
            labels[ComposeConstants.ConfigFilesLabel] = configFiles;
        return labels;
    }

    public static Dictionary<string, IDictionary<string, bool>> LabelFilter(string key, string value)
        => new() { ["label"] = new Dictionary<string, bool> { [$"{key}={value}"] = true } };

    public static Dictionary<string, IDictionary<string, bool>> ProjectLabelFilter(string projectName)
        => LabelFilter(ComposeConstants.ProjectLabel, projectName);

    /// <summary>Reads the Compose service label from a resource's labels, or null when absent.</summary>
    public static string? GetServiceName(IDictionary<string, string>? labels)
        => labels != null && labels.TryGetValue(ComposeConstants.ServiceLabel, out var service) ? service : null;

    // Both labels must match so a service name reused in another project is never touched.
    public static Dictionary<string, IDictionary<string, bool>> ServiceLabelFilter(string projectName, string serviceName)
        => new()
        {
            ["label"] = new Dictionary<string, bool>
            {
                [$"{ComposeConstants.ProjectLabel}={projectName}"] = true,
                [$"{ComposeConstants.ServiceLabel}={serviceName}"] = true
            }
        };

    private static string CreateConfigHash(ServiceDefinition service)
    {
        var input = $"{service.Name}|{service.Image}|{string.Join(",", service.Environment)}|{string.Join(",", service.Volumes)}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
