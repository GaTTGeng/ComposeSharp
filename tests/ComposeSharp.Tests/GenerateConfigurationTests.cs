using System.Globalization;
using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Loader;

namespace ComposeSharp.Tests;

public sealed class GenerateConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "compose-generate-tests", Guid.NewGuid().ToString("N"));

    public GenerateConfigurationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Generate_RendersRetainedConfigurationThatLoadsAgain()
    {
        File.WriteAllText(Path.Combine(_directory, "worker.env"), "FROM_FILE=present\nOVERRIDE=file\n");
        File.WriteAllText(Path.Combine(_directory, "docker-compose.yml"), """
            services:
              worker:
                image: "example/worker:latest"
                build:
                  context_directory: ./ctx
                command: ["echo", "a: b # c"]
                env_file: [worker.env]
                environment:
                  - "TOKEN=$$CASH"
                  - "OVERRIDE=inline"
                ports:
                  - "5353:53/udp"
                restart: "on-failure:3"
                labels:
                  "cost$$code": "charge"
                healthcheck:
                  test: ["CMD-SHELL", "echo ready"]
                  interval: 1500ms
                deploy:
                  replicas: 2
                  restart_policy:
                    max_retries: 4
                  resources:
                    limits:
                      nano_cpus: "250000000"
            networks:
              private:
                driver: bridge
            volumes:
              state:
            """);

        var context = Context();
        var generated = await new ComposeService().GenerateAsync(context);

        Assert.Equal("sample", generated.Name);
        Assert.Equal(["worker"], generated.Services);
        Assert.Contains("services:", generated.RenderedYaml);
        Assert.Contains("networks:", generated.RenderedYaml);
        Assert.Contains("volumes:", generated.RenderedYaml);
        Assert.Contains("$$CASH", generated.RenderedYaml);
        Assert.Contains("context_directory: ./ctx", generated.RenderedYaml);
        Assert.Contains("cost$$code", generated.RenderedYaml);
        Assert.DoesNotContain("env_file", generated.RenderedYaml);
        Assert.DoesNotContain("driver: bridge", generated.RenderedYaml);
        Assert.Null(new ComposeService().LoadProject(context).RenderedYaml);

        File.WriteAllText(Path.Combine(_directory, "generated.yml"), generated.RenderedYaml);
        var reloaded = new ComposeFileLoader().Load(_directory, "generated.yml");
        var service = Assert.Single(reloaded.Services);
        Assert.Equal(["echo", "a: b # c"], service.Command);
        Assert.Equal(["FROM_FILE=present", "OVERRIDE=file", "TOKEN=$CASH", "OVERRIDE=inline"], service.Environment);
        Assert.Equal("5353", Assert.Single(service.Ports).HostPort);
        Assert.Equal("53/udp", Assert.Single(service.Ports).ContainerPort);
        Assert.Equal("on-failure:3", service.Restart);
        Assert.Equal("./ctx", service.Build?.ContextDirectory);
        Assert.Equal("charge", service.Labels["cost$code"]);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), service.Healthcheck?.Interval);
        Assert.Equal(2, service.Deploy?.Replicas);
        Assert.Equal(4, service.Deploy?.RestartPolicy?.MaxRetries);
        Assert.Equal("250000000", service.Deploy?.Resources?.Limits?.NanoCpus);
    }

    [Fact]
    public async Task Generate_ServiceSelectionAndLegacyContainerOptionAreExplicit()
    {
        File.WriteAllText(Path.Combine(_directory, "docker-compose.yml"), """
            services:
              default:
                image: busybox
              gated:
                image: alpine
                profiles: [extra]
            """);
        var service = new ComposeService();
        var context = Context();

        var defaultResult = await service.GenerateAsync(context);
        Assert.Equal(["default"], defaultResult.Services);
        var activeProfile = await service.GenerateAsync(context with { Profiles = ["extra"] });
        Assert.Equal(["default", "gated"], activeProfile.Services);

        var empty = await service.GenerateAsync(context, new ComposeGenerateOptions { Services = [] });
        Assert.Empty(empty.Services);
        Assert.DoesNotContain("default:", empty.RenderedYaml);

        var selected = await service.GenerateAsync(context, new ComposeGenerateOptions
        {
            ProjectName = "renamed",
            Services = ["gated"]
        });
        Assert.Equal("renamed", selected.Name);
        Assert.Equal(["gated"], selected.Services);
        Assert.Contains("name: renamed", selected.RenderedYaml);
        Assert.DoesNotContain("default:", selected.RenderedYaml);

        Assert.Throws<ArgumentException>(() => service.GenerateAsync(context,
            new ComposeGenerateOptions { Services = ["missing"] }).GetAwaiter().GetResult());
        Assert.Throws<NotSupportedException>(() => service.GenerateAsync(context,
            new ComposeGenerateOptions { Containers = ["container-id"] }).GetAwaiter().GetResult());
        Assert.Throws<ArgumentException>(() => service.GenerateAsync(context,
            new ComposeGenerateOptions { ProjectName = " " }).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Generate_IsCultureIndependentAndHonorsCancellation()
    {
        File.WriteAllText(Path.Combine(_directory, "docker-compose.yml"), """
            services:
              web:
                image: nginx
                healthcheck:
                  test: ["CMD", "true"]
                  interval: 1250ms
            """);
        var service = new ComposeService();
        var context = Context();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var french = (await service.GenerateAsync(context)).RenderedYaml;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var english = (await service.GenerateAsync(context)).RenderedYaml;
            Assert.Equal(english, french);
            Assert.Contains("1250ms", french);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GenerateAsync(context,
            cancellationToken: canceled.Token));
    }

    private ComposeProjectContext Context() => new()
    {
        ProjectName = "sample",
        WorkingDirectory = _directory,
        ComposeFileName = "docker-compose.yml"
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
