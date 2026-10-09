using System.Net;
using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Tests;

/// <summary>
/// Covers Docker event mapping, project/service filtering without a Docker daemon, process-listing
/// mapping used by <c>TopAsync</c>, and the listing-race predicate for process queries.
/// </summary>
public sealed class TopAndEventMappingTests
{
    private const string Project = "demo";

    // Exact Compose resource names owned by the "demo" project, kept separate per type.
    // demo_data is a volume name only; a network named demo_data is not ours.
    private static readonly ProjectResourceNames ResourceNames = new(
        Networks: new HashSet<string>(StringComparer.Ordinal) { "demo_default" },
        Volumes: new HashSet<string>(StringComparer.Ordinal) { "demo_data" });

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
                    [ComposeConstants.ProjectLabel] = Project,
                    [ComposeConstants.ServiceLabel] = "web"
                }
            }
        };

        var composeEvent = EventStreamer.Map(message, Project, ResourceNames, null, keepProjectLevelEvents: true);

        Assert.NotNull(composeEvent);
        Assert.Equal("container", composeEvent.Type);
        Assert.Equal("start", composeEvent.Action);
        Assert.Equal("container-123", composeEvent.ID);
        Assert.Equal("container-123", composeEvent.Container);
        Assert.Equal("web", composeEvent.Service);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, composeEvent.Timestamp);
        Assert.Equal(Project, composeEvent.Attributes![ComposeConstants.ProjectLabel]);
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
            Actor = new Actor
            {
                ID = "container-123",
                Attributes = new Dictionary<string, string> { [ComposeConstants.ProjectLabel] = Project }
            }
        };

        var composeEvent = EventStreamer.Map(message, Project, ResourceNames, null, keepProjectLevelEvents: true);

        Assert.NotNull(composeEvent);
        var expected = DateTime.UnixEpoch.AddSeconds(1_700_000_000).AddTicks(500_000_000L / 100);
        Assert.Equal(expected, composeEvent.Timestamp);
    }

    [Fact]
    public void MapEvent_KeepsProjectNetworkEvents_WithNullService()
    {
        // Network events expose only name/type and no labels, so project matching uses the name prefix.
        var message = new Message
        {
            Type = "network",
            Action = "destroy",
            Actor = new Actor
            {
                ID = "network-9",
                Attributes = new Dictionary<string, string>
                {
                    ["name"] = "demo_default",
                    ["type"] = "bridge"
                }
            }
        };

        var composeEvent = EventStreamer.Map(message, Project, ResourceNames, null, keepProjectLevelEvents: true);

        Assert.NotNull(composeEvent);
        Assert.Equal("network-9", composeEvent.ID);
        Assert.Null(composeEvent.Container);
        Assert.Null(composeEvent.Service);
    }

    [Fact]
    public void MapEvent_KeepsProjectVolumeEvents_WhenNameIsOnlyInActorId()
    {
        // Volume events put the volume name in Actor.ID; attributes only carry "driver".
        var message = new Message
        {
            Type = "volume",
            Action = "create",
            Actor = new Actor
            {
                ID = "demo_data",
                Attributes = new Dictionary<string, string> { ["driver"] = "local" }
            }
        };

        var composeEvent = EventStreamer.Map(message, Project, ResourceNames, null, keepProjectLevelEvents: true);

        Assert.NotNull(composeEvent);
        Assert.Equal("demo_data", composeEvent.ID);
        Assert.Null(composeEvent.Container);
        Assert.Null(composeEvent.Service);
    }

    [Fact]
    public void MapEvent_DropsForeignAndUnattributedEvents()
    {
        var foreignNetwork = new Message
        {
            Type = "network",
            Action = "destroy",
            Actor = new Actor
            {
                ID = "network-9",
                Attributes = new Dictionary<string, string> { ["name"] = "other_default" }
            }
        };

        Assert.Null(EventStreamer.Map(foreignNetwork, Project, ResourceNames, null, keepProjectLevelEvents: true));
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
                Attributes = new Dictionary<string, string>
                {
                    [ComposeConstants.ProjectLabel] = Project,
                    [ComposeConstants.ServiceLabel] = "web"
                }
            }
        };
        var apiEvent = new Message
        {
            Type = "container",
            Action = "start",
            Actor = new Actor
            {
                ID = "api-1",
                Attributes = new Dictionary<string, string>
                {
                    [ComposeConstants.ProjectLabel] = Project,
                    [ComposeConstants.ServiceLabel] = "api"
                }
            }
        };
        var networkEvent = new Message
        {
            Type = "network",
            Action = "create",
            Actor = new Actor
            {
                ID = "network-9",
                Attributes = new Dictionary<string, string> { ["name"] = "demo_default" }
            }
        };

        Assert.Equal("web-1", EventStreamer.Map(webEvent, Project, ResourceNames, selected, keepProjectLevelEvents: false)?.ID);
        Assert.Null(EventStreamer.Map(apiEvent, Project, ResourceNames, selected, keepProjectLevelEvents: false));
        // An explicit service filter drops project-level events that carry no service label.
        Assert.Null(EventStreamer.Map(networkEvent, Project, ResourceNames, selected, keepProjectLevelEvents: false));
    }

    [Fact]
    public void MapEvent_ProfileFilter_KeepsProjectEvents_ButDropsInactiveServiceEvents()
    {
        // Profile selection (no explicit Services): service-labeled events are filtered, but
        // project-level network/volume events stay visible.
        var profileSelected = new HashSet<string>(StringComparer.Ordinal) { "web" };
        var debugEvent = new Message
        {
            Type = "container",
            Action = "start",
            Actor = new Actor
            {
                ID = "debug-1",
                Attributes = new Dictionary<string, string>
                {
                    [ComposeConstants.ProjectLabel] = Project,
                    [ComposeConstants.ServiceLabel] = "debug"
                }
            }
        };
        var webEvent = new Message
        {
            Type = "container",
            Action = "die",
            Actor = new Actor
            {
                ID = "web-1",
                Attributes = new Dictionary<string, string>
                {
                    [ComposeConstants.ProjectLabel] = Project,
                    [ComposeConstants.ServiceLabel] = "web"
                }
            }
        };
        var networkEvent = new Message
        {
            Type = "network",
            Action = "destroy",
            Actor = new Actor
            {
                ID = "network-9",
                Attributes = new Dictionary<string, string> { ["name"] = "demo_default" }
            }
        };

        Assert.Equal("web-1", EventStreamer.Map(webEvent, Project, ResourceNames, profileSelected, keepProjectLevelEvents: true)?.ID);
        Assert.Null(EventStreamer.Map(debugEvent, Project, ResourceNames, profileSelected, keepProjectLevelEvents: true));
        Assert.NotNull(EventStreamer.Map(networkEvent, Project, ResourceNames, profileSelected, keepProjectLevelEvents: true));
    }

    [Fact]
    public void MapEvent_UsesStatusAction_WhenActionMissing()
    {
        var message = new Message
        {
            Type = "container",
            Status = "die",
            Actor = new Actor
            {
                ID = "container-123",
                Attributes = new Dictionary<string, string> { [ComposeConstants.ProjectLabel] = Project }
            }
        };

        var composeEvent = EventStreamer.Map(message, Project, ResourceNames, null, keepProjectLevelEvents: true);

        Assert.NotNull(composeEvent);
        Assert.Equal("die", composeEvent.Action);
    }

    [Theory]
    [InlineData("container", "demo", null, null, true)]
    [InlineData("container", "other", null, null, false)]
    [InlineData("container", null, null, null, false)]
    [InlineData("network", null, "demo_default", "net-id", true)]
    [InlineData("network", null, "demo_net", "net-id", false)]
    [InlineData("network", null, "other_default", "net-id", false)]
    [InlineData("network", null, "demoo_default", "net-id", false)]
    // Prefix-related project names must not leak: app_test_default is not demo_default.
    [InlineData("network", null, "demo_test_default", "net-id", false)]
    // A network named like a project volume is not ours (types are tracked separately).
    [InlineData("network", null, "demo_data", "net-id", false)]
    [InlineData("volume", null, null, "demo_data", true)]
    [InlineData("volume", null, null, "demo_test_data", false)]
    [InlineData("volume", null, null, "other_data", false)]
    // Volume name comes from Actor.ID (Moby does not put it in a name attribute).
    [InlineData("volume", null, "demo_data", "ignored-id", false)]
    // A volume named like a project network is not ours.
    [InlineData("volume", null, null, "demo_default", false)]
    [InlineData("image", null, null, null, false)]
    public void BelongsToProject_MatchesLabelOrExactResourceName(string type, string? projectLabel, string? name, string? actorId, bool expected)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (projectLabel is not null)
            attributes[ComposeConstants.ProjectLabel] = projectLabel;
        if (name is not null)
            attributes["name"] = name;

        var message = new Message
        {
            Type = type,
            Action = "create",
            Actor = new Actor { ID = actorId ?? "id-1", Attributes = attributes }
        };

        Assert.Equal(expected, EventStreamer.BelongsToProject(message, Project, ResourceNames, attributes));
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

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void IsListingRace_SkipsOnlyNotFoundAndConflict(HttpStatusCode statusCode, bool expected)
    {
        var exception = new DockerApiException(statusCode, "listing failed");
        Assert.Equal(expected, ProcessInspector.IsListingRace(exception));
    }
}



