using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using ComposeSharp.Loader.Models;
using Docker.DotNet.Models;

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

        Assert.True(ContainerLifecycle.RequiresHealthy(null, inspect));
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

        Assert.True(ContainerLifecycle.RequiresHealthy(null, healthState));
        Assert.False(ContainerLifecycle.RequiresHealthy(
            new ComposeHealthcheck(true, [], null, null, null, null), disabled));
    }
}
