using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ComposeSharp.Api;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Exact Compose resource names owned by a project, split by type. Docker allows a network and a
/// volume to share a name, so ownership must never be checked against a combined name set.
/// </summary>
internal sealed record ProjectResourceNames(
    IReadOnlySet<string> Networks,
    IReadOnlySet<string> Volumes);

/// <summary>
/// Streams Docker Engine events for a Compose project and maps them onto <see cref="ComposeEvent"/>.
/// Ownership is checked client-side: Docker's event label filter only covers container/image
/// labels, while network and volume events expose neither labels nor a matching label filter.
/// Network and volume ownership is matched by exact resource name within the matching type.
/// </summary>
internal sealed class EventStreamer
{
    /// <summary>
    /// Subscribes to the Docker event stream and yields project events until cancellation.
    /// <paramref name="resources"/> holds the exact network/volume names owned by the project.
    /// <paramref name="services"/> filters events that carry a Compose service label (profile or
    /// explicit selection). When <paramref name="keepProjectLevelEvents"/> is true, events without
    /// a service label (network, volume) are also reported. <paramref name="onSubscribed"/> runs
    /// once the Docker event subscription has been issued.
    /// </summary>
    public async IAsyncEnumerable<ComposeEvent> StreamEventsAsync(
        DockerClient client,
        string projectName,
        ProjectResourceNames resources,
        IReadOnlySet<string>? services,
        bool keepProjectLevelEvents,
        Action? onSubscribed,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Single-reader channel bridges the IProgress callback of MonitorEventsAsync to IAsyncEnumerable.
        var channel = Channel.CreateUnbounded<ComposeEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Subscribe by type rather than project label: Docker's label filter only matches
        // container/image events, so network and volume events would never arrive.
        var parameters = new ContainerEventsParameters
        {
            Since = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["type"] = new Dictionary<string, bool>
                {
                    ["container"] = true,
                    ["network"] = true,
                    ["volume"] = true
                }
            }
        };

        var monitor = Task.Run(async () =>
        {
            try
            {
                // Subscription is issued here; callers that race a lifecycle operation wait on onSubscribed.
                onSubscribed?.Invoke();
                await client.System.MonitorEventsAsync(
                    parameters,
                    new EventProgress(channel.Writer, projectName, resources, services, keepProjectLevelEvents),
                    linkedCts.Token);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var composeEvent in channel.Reader.ReadAllAsync(ct))
                yield return composeEvent;
        }
        finally
        {
            // Stop the daemon subscription when the consumer stops enumerating, even without cancellation.
            linkedCts.Cancel();
            channel.Writer.TryComplete();
            await monitor;
        }
    }

    /// <summary>
    /// Maps one Docker event onto <see cref="ComposeEvent"/>, or returns null when the event is
    /// outside the project or the service/project-level filters.
    /// </summary>
    internal static ComposeEvent? Map(
        Message message,
        string projectName,
        ProjectResourceNames resources,
        IReadOnlySet<string>? services,
        bool keepProjectLevelEvents)
    {
        var attributes = message.Actor?.Attributes is { } attrs
            ? new Dictionary<string, string>(attrs, StringComparer.Ordinal)
            : null;

        if (!BelongsToProject(message, projectName, resources, attributes))
            return null;

        var service = attributes is not null && LabelHelper.GetServiceName(attributes) is { } name
            ? name
            : null;

        if (service is not null)
        {
            // Service-labeled events honor the profile or explicit service selection.
            if (services is not null && !services.Contains(service))
                return null;
        }
        else if (!keepProjectLevelEvents)
        {
            // An explicit service selection drops project-level events that carry no service label.
            return null;
        }

        var id = message.Actor?.ID ?? message.ID ?? string.Empty;
        var isContainer = string.Equals(message.Type, "container", StringComparison.OrdinalIgnoreCase);

        return new ComposeEvent
        {
            // TimeNano is nanoseconds since the epoch; DateTime ticks are 100ns units.
            Timestamp = message.TimeNano > 0
                ? DateTime.UnixEpoch.AddTicks(message.TimeNano / 100)
                : DateTimeOffset.FromUnixTimeSeconds(message.Time).UtcDateTime,
            Type = message.Type ?? string.Empty,
            Action = message.Action ?? message.Status ?? string.Empty,
            ID = id,
            Service = service,
            Container = isContainer ? id : null,
            Attributes = attributes
        };
    }

    /// <summary>
    /// True when the event is owned by <paramref name="projectName"/>. Container events carry the
    /// project label. Network and volume events expose only a name and are matched against the
    /// exact names of that resource type (Docker allows a network and a volume to share a name,
    /// and prefix matching would collide across projects named <c>app</c> and <c>app_test</c>).
    /// </summary>
    internal static bool BelongsToProject(
        Message message,
        string projectName,
        ProjectResourceNames resources,
        IDictionary<string, string>? attributes)
    {
        if (attributes is not null &&
            attributes.TryGetValue(ComposeConstants.ProjectLabel, out var owner) &&
            owner is not null)
        {
            return string.Equals(owner, projectName, StringComparison.Ordinal);
        }

        if (string.Equals(message.Type, "network", StringComparison.OrdinalIgnoreCase))
        {
            var name = attributes is not null && attributes.TryGetValue("name", out var value)
                ? value
                : message.Actor?.ID;
            return name is not null && resources.Networks.Contains(name);
        }

        if (string.Equals(message.Type, "volume", StringComparison.OrdinalIgnoreCase))
        {
            // Volume events put the name in Actor.ID; attributes only carry fields such as "driver".
            var name = message.Actor?.ID
                ?? (attributes is not null && attributes.TryGetValue("name", out var value) ? value : null);
            return name is not null && resources.Volumes.Contains(name);
        }

        // Any other event without a project label is not ours.
        return false;
    }

    private sealed class EventProgress(
        ChannelWriter<ComposeEvent> writer,
        string projectName,
        ProjectResourceNames resources,
        IReadOnlySet<string>? services,
        bool keepProjectLevelEvents) : IProgress<Message>
    {
        public void Report(Message value)
        {
            var composeEvent = Map(value, projectName, resources, services, keepProjectLevelEvents);
            if (composeEvent is not null)
                writer.TryWrite(composeEvent);
        }
    }
}
