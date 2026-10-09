using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ComposeSharp.Api;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Streams Docker Engine events for a Compose project and maps them onto <see cref="ComposeEvent"/>.
/// Events are constrained by the project label so only resources owned by the project are observed.
/// </summary>
internal sealed class EventStreamer
{
    /// <summary>
    /// Subscribes to the Docker event stream and yields project events until cancellation.
    /// Optional <paramref name="services"/> keeps only events whose container belongs to those services.
    /// </summary>
    public async IAsyncEnumerable<ComposeEvent> StreamEventsAsync(
        DockerClient client,
        string projectName,
        IReadOnlySet<string>? services,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Single-reader channel bridges the IProgress callback of MonitorEventsAsync to IAsyncEnumerable.
        var channel = Channel.CreateUnbounded<ComposeEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Start from "now" so the stream reports live changes rather than replaying the event buffer.
        var parameters = new ContainerEventsParameters
        {
            Since = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            Filters = LabelHelper.ProjectLabelFilter(projectName)
        };

        var monitor = Task.Run(async () =>
        {
            try
            {
                await client.System.MonitorEventsAsync(
                    parameters,
                    new EventProgress(channel.Writer, services),
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
    /// outside the optional service filter.
    /// </summary>
    internal static ComposeEvent? Map(Message message, IReadOnlySet<string>? services)
    {
        var attributes = message.Actor?.Attributes is { } attrs
            ? new Dictionary<string, string>(attrs, StringComparer.Ordinal)
            : null;

        var service = attributes is not null && LabelHelper.GetServiceName(attributes) is { } name
            ? name
            : null;

        if (services is not null && (service is null || !services.Contains(service)))
            return null;

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

    private sealed class EventProgress(ChannelWriter<ComposeEvent> writer, IReadOnlySet<string>? services) : IProgress<Message>
    {
        public void Report(Message value)
        {
            var composeEvent = Map(value, services);
            if (composeEvent is not null)
                writer.TryWrite(composeEvent);
        }
    }
}
