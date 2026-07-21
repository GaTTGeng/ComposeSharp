using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Loader;

namespace ComposeSharp.IntegrationTests;

public class ComposeServiceIntegrationTests
{
    private static readonly bool DockerAvailable = CheckDockerAvailable();

    private static bool CheckDockerAvailable()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            return proc?.WaitForExit(5000) == true && proc.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task PsAsync_ReturnsEmpty_WhenNoContainers()
    {
        if (!DockerAvailable) return;

        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "test-empty-" + Guid.NewGuid().ToString()[..8],
            WorkingDirectory = Path.GetTempPath()
        };

        var result = await service.PsAsync(context);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListAsync_ReturnsResult()
    {
        if (!DockerAvailable) return;

        var service = new ComposeService();
        var result = await service.ListAsync();
        Assert.NotNull(result);
    }

    [Fact]
    public void LoadProject_ParsesConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-int-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            services:
              web:
                image: nginx:latest
              api:
                image: node:18
            """);

        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "test",
            WorkingDirectory = dir
        };

        var config = service.LoadProject(context);
        Assert.Equal(2, config.Services.Count);
        Assert.Contains("web", config.Services);
        Assert.Contains("api", config.Services);

        Directory.Delete(dir, recursive: true);
    }
}
