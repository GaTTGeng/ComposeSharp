using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
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
}
