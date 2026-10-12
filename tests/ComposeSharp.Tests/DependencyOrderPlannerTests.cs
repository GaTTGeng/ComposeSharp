using ComposeSharp.Engine.Internal;
using ComposeSharp.Loader.Models;

namespace ComposeSharp.Tests;

public sealed class DependencyOrderPlannerTests
{
    [Fact]
    public void Plan_OrdersDependenciesBeforeServicesAndPreservesPeerOrder()
    {
        var input = new[]
        {
            CreateService("web", ["database"]),
            CreateService("cache"),
            CreateService("database"),
            CreateService("metrics", ["database"])
        };

        var result = DependencyOrderPlanner.Plan(input);

        Assert.Equal(["cache", "database", "web", "metrics"], result.Select(service => service.Name));
    }

    [Fact]
    public void Plan_IncludesTransitiveDependenciesForRequestedServiceCaseInsensitively()
    {
        var input = new[]
        {
            CreateService("leaf", ["BASE"]),
            CreateService("unrelated"),
            CreateService("base"),
            CreateService("app", ["leaf"])
        };

        var result = DependencyOrderPlanner.Plan(input, ["APP"]);

        Assert.Equal(["base", "leaf", "app"], result.Select(service => service.Name));
    }

    [Fact]
    public void Plan_EmptyRequestReturnsNoServices()
    {
        Assert.Empty(DependencyOrderPlanner.Plan([CreateService("app")], []));
    }

    [Fact]
    public void Plan_ReportsMissingRequestedService()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DependencyOrderPlanner.Plan([CreateService("app")], ["missing"]));

        Assert.Contains("missing", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_ReportsMissingDependencyWithOwnerAndName()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DependencyOrderPlanner.Plan([CreateService("app", ["db"]) ]));

        Assert.Contains("app", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("db", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_ReportsCyclePath()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DependencyOrderPlanner.Plan([CreateService("web", ["db"]), CreateService("db", ["WEB"])]));

        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("web", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("db", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_ReportsDuplicateNamesCaseInsensitively()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            DependencyOrderPlanner.Plan([CreateService("app"), CreateService("APP")]));

        Assert.Contains("app", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceDefinition CreateService(string name, IReadOnlyList<string>? dependencies = null) => new(
        Name: name, Image: null, Build: null, ContainerName: null, Command: [], Entrypoint: [], Environment: [], Ports: [], Volumes: [],
        Restart: null, Healthcheck: null, DependsOn: dependencies ?? [], Networks: [], ExtraHosts: [], Privileged: false, NetworkMode: null, Ipc: null,
        ShmSize: null, Profiles: [], Deploy: null, Secrets: [], Configs: [], Labels: new Dictionary<string, string>(), Logging: null,
        Hostname: null, Domainname: null, User: null, WorkingDir: null, Tty: false, StdinOpen: false, StopSignal: null, StopGracePeriod: null,
        ReadOnly: false, Tmpfs: [], CapAdd: [], CapDrop: [], Devices: [], Sysctls: new Dictionary<string, string>(), SecurityOpt: [], Init: null,
        Platform: null, PullPolicy: null, Dns: [], DnsSearch: [], Pid: null, MacAddress: null, CgroupParent: null, ExtendsService: null,
        ExtendsFile: null, Develop: null, EnvFile: [], Links: [], CpuShares: null, CpuQuota: null, Cpuset: null, Memory: null,
        MemorySwap: null, MemoryReservation: null, OomKillDisable: null, OomScoreAdj: null, GroupAdd: [], RestartMaxRetries: null,
        Annotations: new Dictionary<string, string>());
}
