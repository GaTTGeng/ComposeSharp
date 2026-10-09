using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ComposeSharp.Api;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Streams Docker Engine events for a Compose project and maps them onto <see cref="ComposeEvent"/>.
/// Ownership is checked client-side: Docker's event label filter only covers container/image
/// labels, while network and volume events expose neither labels nor a matching label filter.
/// </summary>
internal sealed class EventStreamer
{
    /// <summary>
    /// Subscribes to the Docker event stream and yields project events until cancellation.
    /// <paramref name="services"/> filters events that carry a Compose service label (profile or
    /// explicit selection). When <paramref name="keepProjectLevelEvents"/> is true, events without
    /// a service label (network, volume) are also reported.
    /// </summary>
    public async IAsyncEnumerable<ComposeEvent> StreamEventsAsync(
        DockerClient client,
        string projectName,
        IReadOnlySet<string>? services,
        bool keepProjectLevelEvents,
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
                await client.System.MonitorEventsAsync(
                    parameters,
                    new EventProgress(channel.Writer, projectName, services, keepProjectLevelEvents),
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
        IReadOnlySet<string>? services,
        bool keepProjectLevelEvents)
    {
        var attributes = message.Actor?.Attributes is { } attrs
            ? new Dictionary<string, string>(attrs, StringComparer.Ordinal)
            : null;

        if (!BelongsToProject(message, projectName, attributes))
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
    /// project label; network and volume events expose only a Compose-prefixed name
    /// (<c>projectName_*</c>), which is how those resources are named at creation.
    /// </summary>
    internal static bool BelongsToProject(Message message, string projectName, IDictionary<string, string>? attributes)
    {
        if (attributes is not null &&
            attributes.TryGetValue(ComposeConstants.ProjectLabel, out var owner) &&
            owner is not null)
        {
            return string.Equals(owner, projectName, StringComparison.Ordinal);
        }

        // Network/volume events do not include labels, so fall back to the Compose name prefix.
        if (string.Equals(message.Type, "network", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Type, "volume", StringComparison.OrdinalIgnoreCase))
        {
            // Networks put the name in the "name" attribute; volumes put it in Actor.ID
            // (attributes only carry fields such as "driver").
            var name = attributes is not null && attributes.TryGetValue("name", out var value)
                ? value
                : message.Actor?.ID;
            return name is not null &&
                   name.StartsWith(projectName + "_", StringComparison.Ordinal);
        }

        // Any other event without a project label is not ours.
        return false;
    }

    private sealed class EventProgress(
        ChannelWriter<ComposeEvent> writer,
        string projectName,
        IReadOnlySet<string>? services,
        bool keepProjectLevelEvents) : IProgress<Message>
    {
        public void Report(Message value)
        {
            var composeEvent = Map(value, projectName, services, keepProjectLevelEvents);
            if (composeEvent is not null)
                writer.TryWrite(composeEvent);
        }
    }
}
