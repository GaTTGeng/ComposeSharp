using ComposeSharp.Loader.Models;

namespace ComposeSharp.Engine.Internal;

internal static class ProfileServiceSelector
{
    public static IReadOnlyList<ServiceDefinition> Select(
        ComposeProject project,
        IReadOnlyList<string>? profiles,
        IReadOnlyList<string>? explicitServices = null)
    {
        if (explicitServices is { Count: > 0 })
        {
            return project.Services
                .Where(service => explicitServices.Contains(service.Name))
                .ToList();
        }

        if (profiles is not { Count: > 0 })
        {
            return project.Services
                .Where(service => service.Profiles.Count == 0)
                .ToList();
        }

        var activeProfiles = new HashSet<string>(profiles, StringComparer.Ordinal);
        return project.Services
            .Where(service => service.Profiles.Count == 0 || service.Profiles.Any(activeProfiles.Contains))
            .ToList();
    }
}
