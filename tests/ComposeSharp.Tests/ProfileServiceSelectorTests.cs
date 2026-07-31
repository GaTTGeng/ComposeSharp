using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Engine.Internal;
using ComposeSharp.Loader.Models;

namespace ComposeSharp.Tests;

public sealed class ProfileServiceSelectorTests
{
    [Fact]
    public void LoadProject_AppliesProfilesFromContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"compose-profiles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
                services:
                  app:
                    image: example/app
                  debug:
                    image: example/debug
                    profiles: [debug]
                  tests:
                    image: example/tests
                    profiles: [tests]
                """);

            var service = new ComposeService();
            var defaultConfig = service.LoadProject(new ComposeProjectContext
            {
                ProjectName = "profiles",
                WorkingDirectory = directory,
                ComposeFileName = "compose.yaml"
            });
            var selectedConfig = service.LoadProject(new ComposeProjectContext
            {
                ProjectName = "profiles",
                WorkingDirectory = directory,
                ComposeFileName = "compose.yaml",
                Profiles = ["debug", "tests"]
            });

            Assert.Equal(["app"], defaultConfig.Services);
            Assert.Equal(["app", "debug", "tests"], selectedConfig.Services);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Select_ExcludesProfiledServices_WhenNoProfilesAreActive()
    {
        var services = ProfileServiceSelector.Select(CreateProject(), profiles: null);

        Assert.Equal(["app"], services.Select(service => service.Name));
    }

    [Fact]
    public void Select_IncludesServicesMatchingAnyActiveProfile()
    {
        var services = ProfileServiceSelector.Select(CreateProject(), ["debug", "metrics"]);

        Assert.Equal(["app", "debug", "metrics"], services.Select(service => service.Name));
    }

    [Fact]
    public void Select_ExplicitServiceBypassesProfileFiltering()
    {
        var services = ProfileServiceSelector.Select(CreateProject(), profiles: null, explicitServices: ["debug"]);

        Assert.Equal(["debug"], services.Select(service => service.Name));
    }

    private static ComposeProject CreateProject() => new(
        WorkingDirectory: ".",
        Services: [
            CreateService("app", []),
            CreateService("debug", ["debug"]),
            CreateService("metrics", ["metrics", "debug"]),
            CreateService("tests", ["tests"])
        ],
        Volumes: [],
        Networks: [],
        Secrets: [],
        Configs: [],
        Extensions: new Dictionary<string, string>());

    private static ServiceDefinition CreateService(string name, IReadOnlyList<string> profiles) => new(
        Name: name, Image: null, Build: null, ContainerName: null, Command: [], Entrypoint: [], Environment: [], Ports: [], Volumes: [],
        Restart: null, Healthcheck: null, DependsOn: [], Networks: [], ExtraHosts: [], Privileged: false, NetworkMode: null, Ipc: null,
        ShmSize: null, Profiles: profiles, Deploy: null, Secrets: [], Configs: [], Labels: new Dictionary<string, string>(), Logging: null,
        Hostname: null, Domainname: null, User: null, WorkingDir: null, Tty: false, StdinOpen: false, StopSignal: null, StopGracePeriod: null,
        ReadOnly: false, Tmpfs: [], CapAdd: [], CapDrop: [], Devices: [], Sysctls: new Dictionary<string, string>(), SecurityOpt: [], Init: null,
        Platform: null, PullPolicy: null, Dns: [], DnsSearch: [], Pid: null, MacAddress: null, CgroupParent: null, ExtendsService: null,
        ExtendsFile: null, Develop: null, EnvFile: [], Links: [], CpuShares: null, CpuQuota: null, Cpuset: null, Memory: null,
        MemorySwap: null, MemoryReservation: null, OomKillDisable: null, OomScoreAdj: null, GroupAdd: [], RestartMaxRetries: null,
        Annotations: new Dictionary<string, string>());
}
