// Rider inspection rationale: The existing collection construction is retained to make its concrete type and initialization clear.
// ReSharper disable UseCollectionExpression
using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Net;

namespace ComposeSharp.Tests;

public class ContainerLifecycleTests
{
    [Fact]
    public void FindByContainerNumber_UsesLabelInsteadOfListPosition()
    {
        var replicaTwo = new ContainerListResponse
        {
            ID = "replica-two",
            Labels = new Dictionary<string, string> { [ComposeConstants.ContainerNumberLabel] = "2" }
        };
        var replicaOne = new ContainerListResponse
        {
            ID = "replica-one",
            Labels = new Dictionary<string, string> { [ComposeConstants.ContainerNumberLabel] = "1" }
        };

        Assert.Equal(replicaOne, ContainerLifecycle.FindByContainerNumber([replicaTwo, replicaOne], 1));
        Assert.Equal(replicaTwo, ContainerLifecycle.FindByContainerNumber([replicaTwo, replicaOne], 2));
        Assert.Null(ContainerLifecycle.FindByContainerNumber([replicaTwo, replicaOne], 3));
    }

    [Fact]
    public void FilterOneOffContainers_RequiresOptIn()
    {
        var serviceContainer = new ContainerListResponse
        {
            ID = "service",
            Labels = new Dictionary<string, string>
            {
                [ComposeConstants.ContainerNumberLabel] = "1",
                [ComposeConstants.OneOffLabel] = "False"
            }
        };
        var oneOffContainer = new ContainerListResponse
        {
            ID = "one-off",
            Labels = new Dictionary<string, string>
            {
                [ComposeConstants.ContainerNumberLabel] = "0",
                [ComposeConstants.OneOffLabel] = "True"
            }
        };

        Assert.Equal("service", Assert.Single(ContainerLifecycle.FilterOneOffContainers([oneOffContainer, serviceContainer], false)).ID);
        Assert.Equal(2, ContainerLifecycle.FilterOneOffContainers([oneOffContainer, serviceContainer], true).Count());
    }

    [Fact]
    public void FilterEligibleContainers_IncludesStoppedContainersOnlyForArchiveOperations()
    {
        var running = new ContainerListResponse { ID = "running", State = "running" };
        var stopped = new ContainerListResponse { ID = "stopped", State = "exited" };
        var oneOff = new ContainerListResponse
        {
            ID = "one-off",
            State = "created",
            Labels = new Dictionary<string, string> { [ComposeConstants.OneOffLabel] = "True" }
        };

        Assert.Equal(new[] { running }, ContainerLifecycle.FilterEligibleContainers([running, stopped, oneOff], false, false));
        Assert.Equal(new[] { running, stopped }, ContainerLifecycle.FilterEligibleContainers([running, stopped, oneOff], true, false));
        Assert.Equal(new[] { running, stopped, oneOff }, ContainerLifecycle.FilterEligibleContainers([running, stopped, oneOff], true, true));
    }

    [Fact]
    public void SelectContainerForArchive_UsesRequestedReplicaNumber()
    {
        var replicaTwo = new ContainerListResponse
        {
            ID = "replica-two",
            Labels = new Dictionary<string, string> { [ComposeConstants.ContainerNumberLabel] = "2" }
        };
        var replicaOne = new ContainerListResponse
        {
            ID = "replica-one",
            Labels = new Dictionary<string, string> { [ComposeConstants.ContainerNumberLabel] = "1" }
        };

        Assert.Equal(replicaTwo, ContainerLifecycle.SelectContainerForArchive([replicaTwo, replicaOne], "app", 2));
    }

    [Fact]
    public void RequiresHealthy_DetectsImageInheritedHealthcheck()
    {
        var inspect = new ContainerInspectResponse
        {
            Config = new Config { Healthcheck = new HealthConfig { Test = ["CMD", "check"] } }
        };

        Assert.True(ContainerLifecycle.RequiresHealthy(inspect));
    }

    [Fact]
    public void RequiresHealthy_UsesHealthStateAndHonorsDisabledComposeOverride()
    {
        var healthState = new ContainerInspectResponse
        {
            State = new ContainerState { Health = new Health { Status = "starting" } }
        };
        var disabled = new ContainerInspectResponse
        {
            Config = new Config { Healthcheck = new HealthConfig { Test = ["NONE"] } }
        };

        Assert.True(ContainerLifecycle.RequiresHealthy(healthState));
        Assert.False(ContainerLifecycle.RequiresHealthy(disabled));
    }

    [Fact]
    public void RequiresHealthy_IgnoresHealthcheckAddedAfterExistingContainerWasCreated()
    {
        Assert.False(ContainerLifecycle.RequiresHealthy(new ContainerInspectResponse()));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    public void IsRunningNormally_RejectsPausedAndRestartingStates(bool running, bool paused, bool restarting, bool expected)
    {
        Assert.Equal(expected, ContainerLifecycle.IsRunningNormally(new ContainerState
        {
            Running = running,
            Paused = paused,
            Restarting = restarting
        }));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotModified, true)]
    [InlineData(HttpStatusCode.Conflict, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public void IsAlreadyStartedResponse_IgnoresOnlyDockerAlreadyStartedStatus(HttpStatusCode statusCode, bool expected)
    {
        Assert.Equal(expected, ContainerLifecycle.IsAlreadyStartedResponse(new DockerApiException(statusCode, "start failed")));
    }
}
// ReSharper restore UseCollectionExpression
