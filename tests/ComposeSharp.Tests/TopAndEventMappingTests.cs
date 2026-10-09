using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using Docker.DotNet.Models;

namespace ComposeSharp.Tests;

/// <summary>
/// Covers Docker event mapping and service filtering without a Docker daemon, plus process-listing
/// mapping used by <c>TopAsync</c>.
/// </summary>
public sealed class TopAndEventMappingTests
{
    [Fact]
    public void MapEvent_CopiesTypeActionAndActorIdentity()
    {
        var message = new Message
        {
            Type = "container",
            Action = "start",
            ID = "ignored-fallback-id",
            Time = 1_700_000_000,
            Actor = new Actor
            {
                ID = "container-123",
                Attributes = new Dictionary<string, string>
                {
                    [ComposeConstants.ProjectLabel] = "demo",
                    [ComposeConstants.ServiceLabel] = "web"
                }
            }
        };

        var composeEvent = EventStreamer.Map(message, services: null);

        Assert.NotNull(composeEvent);
        Assert.Equal("container", composeEvent.Type);
        Assert.Equal("start", composeEvent.Action);
        Assert.Equal("container-123", composeEvent.ID);
        Assert.Equal("container-123", composeEvent.Container);
        Assert.Equal("web", composeEvent.Service);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, composeEvent.Timestamp);
        Assert.Equal("demo", composeEvent.Attributes![ComposeConstants.ProjectLabel]);
    }

    [Fact]
    public void MapEvent_PrefersTimeNanoOverSeconds()
    {
        // 1_700_000_000 seconds plus 500_000_000 nanoseconds.
        var message = new Message
        {
            Type = "container",
            Action = "die",
            Time = 1_700_000_000,
            TimeNano = 1_700_000_000L * 1_000_000_000L + 500_000_000L,
            Actor = new Actor { ID = "container-123" }
        };

        var composeEvent = EventStreamer.Map(message, services: null);

        Assert.NotNull(composeEvent);
        var expected = DateTime.UnixEpoch.AddSeconds(1_700_000_000).AddTicks(500_000_000L / 100);
        Assert.Equal(expected, composeEvent.Timestamp);
    }

    [Fact]
    public void MapEvent_LeavesContainerNull_ForNonContainerEvents()
    {
        var message = new Message
        {
            Type = "network",
            Action = "create",
            Actor = new Actor
            {
                ID = "network-9",
                Attributes = new Dictionary<string, string> { ["name"] = "demo_default" }
            }
        };

        var composeEvent = EventStreamer.Map(message, services: null);

        Assert.NotNull(composeEvent);
        Assert.Equal("network-9", composeEvent.ID);
        Assert.Null(composeEvent.Container);
        Assert.Null(composeEvent.Service);
    }

    [Fact]
    public void MapEvent_FiltersByService_AndDropsEventsWithoutServiceLabel()
    {
        var selected = new HashSet<string>(StringComparer.Ordinal) { "web" };
        var webEvent = new Message
        {
            Type = "container",
            Action = "start",
            Actor = new Actor
            {
                ID = "web-1",
                Attributes = new Dictionary<string, string> { [ComposeConstants.ServiceLabel] = "web" }
            }
        };
        var apiEvent = new Message
        {
            Type = "container",
            Action = "start",
            Actor = new Actor
            {
                ID = "api-1",
                Attributes = new Dictionary<string, string> { [ComposeConstants.ServiceLabel] = "api" }
            }
        };
        var networkEvent = new Message
        {
            Type = "network",
            Action = "create",
            Actor = new Actor { ID = "network-9", Attributes = new Dictionary<string, string>() }
        };

        Assert.Equal("web-1", EventStreamer.Map(webEvent, selected)?.ID);
        Assert.Null(EventStreamer.Map(apiEvent, selected));
        Assert.Null(EventStreamer.Map(networkEvent, selected));
    }

    [Fact]
    public void MapEvent_UsesStatusAction_WhenActionMissing()
    {
        var message = new Message
        {
            Type = "container",
            Status = "die",
            Actor = new Actor { ID = "container-123" }
        };

        var composeEvent = EventStreamer.Map(message, services: null);

        Assert.NotNull(composeEvent);
        Assert.Equal("die", composeEvent.Action);
    }

    [Fact]
    public void MapProcessListing_AlignsTitlesAndRows_AndReadsReplicaLabel()
    {
        var container = new ContainerListResponse
        {
            ID = "container-123",
            Names = ["/demo-web-1"],
            Labels = new Dictionary<string, string>
            {
                [ComposeConstants.ServiceLabel] = "web",
                [ComposeConstants.ContainerNumberLabel] = "1"
            }
        };
        var processes = new ContainerProcessesResponse
        {
            Titles = ["PID", "CMD"],
            Processes =
            [
                ["1", "nginx: master"],
                ["2", "nginx: worker"]
            ]
        };

        var summary = ProcessInspector.Map(container, processes);

        Assert.Equal("container-123", summary.ID);
        Assert.Equal("demo-web-1", summary.Name);
        Assert.Equal("web", summary.Service);
        Assert.Equal("1", summary.Replica);
        Assert.Equal(["PID", "CMD"], summary.Titles);
        Assert.Equal(2, summary.Processes.Count);
        Assert.Equal(["1", "nginx: master"], summary.Processes[0]);
        Assert.Equal(["2", "nginx: worker"], summary.Processes[1]);
    }

    [Fact]
    public void MapProcessListing_ToleratesMissingNameAndEmptyRows()
    {
        var container = new ContainerListResponse
        {
            ID = "container-abc",
            Names = null,
            Labels = null
        };
        var processes = new ContainerProcessesResponse
        {
            Titles = null,
            Processes = null
        };

        var summary = ProcessInspector.Map(container, processes);

        Assert.Equal("container-abc", summary.ID);
        Assert.Equal("container-abc", summary.Name);
        Assert.Equal(string.Empty, summary.Service);
        Assert.Null(summary.Replica);
        Assert.Empty(summary.Titles);
        Assert.Empty(summary.Processes);
    }
}
