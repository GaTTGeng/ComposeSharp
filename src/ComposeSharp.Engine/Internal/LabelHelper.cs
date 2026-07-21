using ComposeSharp.Api;
using ComposeSharp.Loader.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

internal sealed class LabelHelper
{
    public Dictionary<string, string> CreateServiceLabels(string projectName, ServiceDefinition service, int index, bool oneOff = false)
    {
        var labels = new Dictionary<string, string>
        {
            [ComposeConstants.ProjectLabel] = projectName,
            [ComposeConstants.ServiceLabel] = service.Name,
            [ComposeConstants.ContainerNumberLabel] = index.ToString(),
            [ComposeConstants.OneOffLabel] = oneOff.ToString(),
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
