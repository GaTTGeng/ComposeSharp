using ComposeSharp.Engine.Internal;

namespace ComposeSharp.Tests;

public sealed class StackStatusTests
{
    [Fact]
    public void FromContainerStates_ReportsRunningWhenAllContainersRun() =>
        Assert.Equal("running", StackStatus.FromContainerStates(["running", "running"]));

    [Fact]
    public void FromContainerStates_ReportsPartialWhenRunningAndStoppedContainersAreMixed() =>
        Assert.Equal("partial", StackStatus.FromContainerStates(["running", "exited"]));

    [Fact]
    public void FromContainerStates_ReportsExitedWhenAllContainersAreStopped() =>
        Assert.Equal("exited", StackStatus.FromContainerStates(["exited", "dead"]));

    [Fact]
    public void FromContainerStates_ReportsUnknownWhenEngineStateIsMissingOrUnsupported()
    {
        Assert.Equal("unknown", StackStatus.FromContainerStates(["running", null]));
        Assert.Equal("unknown", StackStatus.FromContainerStates(["paused"]));
    }
}
