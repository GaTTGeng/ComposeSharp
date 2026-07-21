using ComposeSharp.Api;
using ComposeSharp.Loader;
using ComposeSharp.Loader.Models;
using Xunit.Abstractions;

namespace ComposeSharp.Tests;

public class ComposeFileLoaderTests
{
    private readonly ITestOutputHelper _output;
    public ComposeFileLoaderTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Load_SimpleCompose()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            version: "3.8"
            services:
              web:
                image: nginx:latest
                ports:
                  - "8080:80"
              api:
                image: node:18
                command: ["npm", "start"]
            """);

        var loader = new ComposeFileLoader();
        var project = loader.Load(dir, "docker-compose.yml");

        _output.WriteLine($"Services: {project.Services.Count}");
        foreach (var svc in project.Services)
            _output.WriteLine($"  {svc.Name}: {svc.Image}");

        Assert.Equal(2, project.Services.Count);
        Assert.Contains(project.Services, s => s.Name == "web");
        Assert.Contains(project.Services, s => s.Name == "api");
        Assert.Equal("nginx:latest", project.Services.First(s => s.Name == "web").Image);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WithVolumes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            version: "3.8"
            services:
              db:
                image: postgres:15
                volumes:
                  - "pgdata:/var/lib/postgresql/data"
            volumes:
              pgdata:
            """);

        var loader = new ComposeFileLoader();
        var project = loader.Load(dir, "docker-compose.yml");

        Assert.Single(project.Services);
        Assert.Single(project.Volumes);
        Assert.Equal("pgdata", project.Volumes[0]);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WithEnvironment()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            version: "3.8"
            services:
              app:
                image: myapp:latest
                environment:
                  - DATABASE_URL=postgres://localhost/db
                  - DEBUG=true
            """);

        var loader = new ComposeFileLoader();
        var project = loader.Load(dir, "docker-compose.yml");

        Assert.Single(project.Services);
        Assert.Contains("DATABASE_URL=postgres://localhost/db", project.Services[0].Environment);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WithNetworks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            version: "3.8"
            services:
              web:
                image: nginx
                networks:
                  - frontend
            networks:
              frontend:
            """);

        var loader = new ComposeFileLoader();
        var project = loader.Load(dir, "docker-compose.yml");

        Assert.Single(project.Networks);
        Assert.Equal("frontend", project.Networks[0]);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WithBuildConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            version: "3.8"
            services:
              app:
                image: myapp:latest
                build:
                  context: .
                  dockerfile: Dockerfile
                  args:
                    - NODE_ENV=production
                  target: production
            """);

        var loader = new ComposeFileLoader();
        var project = loader.Load(dir, "docker-compose.yml");

        Assert.Single(project.Services);
        Assert.NotNull(project.Services[0].Build);
        Assert.Equal(".", project.Services[0].Build!.Context);
        Assert.Equal("Dockerfile", project.Services[0].Build!.Dockerfile);
        Assert.Equal("production", project.Services[0].Build!.Target);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WithProfiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            version: "3.8"
            services:
              web:
                image: nginx
              debug:
                image: busybox
                profiles:
                  - debug
            """);

        var loader = new ComposeFileLoader();
        var project = loader.Load(dir, "docker-compose.yml");

        Assert.Equal(2, project.Services.Count);
        Assert.Contains(project.Services, s => s.Name == "debug" && s.Profiles.Contains("debug"));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_WithDeploy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            version: "3.8"
            services:
              web:
                image: nginx
                deploy:
                  replicas: 3
                  resources:
                    limits:
                      memory: 512M
            """);

        var loader = new ComposeFileLoader();
        var project = loader.Load(dir, "docker-compose.yml");

        Assert.Single(project.Services);
        Assert.NotNull(project.Services[0].Deploy);
        Assert.Equal(3, project.Services[0].Deploy!.Replicas);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ComposeConstants_LabelsExist()
    {
        Assert.Equal("com.docker.compose.project", ComposeConstants.ProjectLabel);
        Assert.Equal("com.docker.compose.service", ComposeConstants.ServiceLabel);
    }

    [Fact]
    public void ExecResult_Succeeded()
    {
        var ok = new ExecResult { ExitCode = 0, StandardOutput = "hello" };
        Assert.True(ok.Succeeded);

        var fail = new ExecResult { ExitCode = 1, StandardError = "error" };
        Assert.False(fail.Succeeded);
    }

    [Fact]
    public void IComposeService_MethodsExist()
    {
        var type = typeof(IComposeService);
        Assert.NotNull(type.GetMethod("UpAsync"));
        Assert.NotNull(type.GetMethod("DownAsync"));
        Assert.NotNull(type.GetMethod("StartAsync"));
        Assert.NotNull(type.GetMethod("StopAsync"));
        Assert.NotNull(type.GetMethod("RestartAsync"));
        Assert.NotNull(type.GetMethod("CreateAsync"));
        Assert.NotNull(type.GetMethod("BuildAsync"));
        Assert.NotNull(type.GetMethod("PullAsync"));
        Assert.NotNull(type.GetMethod("PushAsync"));
        Assert.NotNull(type.GetMethod("KillAsync"));
        Assert.NotNull(type.GetMethod("RunAsync"));
        Assert.NotNull(type.GetMethod("RemoveAsync"));
        Assert.NotNull(type.GetMethod("ExecAsync"));
        Assert.NotNull(type.GetMethod("AttachAsync"));
        Assert.NotNull(type.GetMethod("CopyAsync"));
        Assert.NotNull(type.GetMethod("PauseAsync"));
        Assert.NotNull(type.GetMethod("UnPauseAsync"));
        Assert.NotNull(type.GetMethod("PsAsync"));
        Assert.NotNull(type.GetMethod("ListAsync"));
        Assert.NotNull(type.GetMethod("TopAsync"));
        Assert.NotNull(type.GetMethod("ImagesAsync"));
        Assert.NotNull(type.GetMethod("PortAsync"));
        Assert.NotNull(type.GetMethod("LogsAsync"));
        Assert.NotNull(type.GetMethod("EventsAsync"));
        Assert.NotNull(type.GetMethod("ScaleAsync"));
        Assert.NotNull(type.GetMethod("WaitAsync"));
        Assert.NotNull(type.GetMethod("WatchAsync"));
        Assert.NotNull(type.GetMethod("ExportAsync"));
        Assert.NotNull(type.GetMethod("CommitAsync"));
        Assert.NotNull(type.GetMethod("VizAsync"));
        Assert.NotNull(type.GetMethod("GenerateAsync"));
        Assert.NotNull(type.GetMethod("VolumesAsync"));
        Assert.NotNull(type.GetMethod("LoadProject"));
        Assert.NotNull(type.GetMethod("PublishAsync"));
    }

    [Fact]
    public void ContainerSummary_CanCreate()
    {
        var summary = new ContainerSummary
        {
            ID = "abc123",
            Name = "test-web-1",
            Image = "nginx:latest",
            Project = "test",
            Service = "web",
            State = "running"
        };
        Assert.Equal("abc123", summary.ID);
        Assert.Equal("running", summary.State);
    }
}
