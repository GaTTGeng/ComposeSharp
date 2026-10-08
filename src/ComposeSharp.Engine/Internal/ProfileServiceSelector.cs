using ComposeSharp.Loader.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Chooses which services are active for a run based on enabled profiles and explicit service lists.
/// </summary>
internal static class ProfileServiceSelector
{
    public static IReadOnlyList<ServiceDefinition> Select(
        ComposeProject project,
        IReadOnlyList<string>? profiles,
        IReadOnlyList<string>? explicitServices = null)
    {
        // Selection rules, in order:
        // 1. An explicit service list wins and bypasses profile filtering entirely.
        // 2. With no active profiles, only services without a profiles section are selected.
        // 3. Otherwise a service is selected when it has no profiles or matches any active profile.
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
