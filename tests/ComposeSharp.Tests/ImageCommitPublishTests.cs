using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using Docker.DotNet.Models;

namespace ComposeSharp.Tests;

/// <summary>
/// Covers managed commit parameter mapping and publish outcome reporting helpers, including
/// registry auth construction, failure shaping, and cancellation/argument validation that runs
/// before any Docker connection is opened.
/// </summary>
public sealed class ImageCommitPublishTests
{
    [Fact]
    public void Create_SplitsReferenceAndMapsCommitOptions()
    {
        var parameters = ImageCommitParametersFactory.Create("registry.example.com:5000/team/app:1.2", new ComposeCommitOptions
        {
            Service = "app",
            Author = "Ada Lovelace <ada@example.com>",
            Message = "snapshot after migration",
            Pause = false,
            Changes = new Dictionary<string, string>
            {
                ["CMD"] = "/bin/sh",
                ["ENV"] = "MODE=debug"
            }
        });

        Assert.Equal("registry.example.com:5000/team/app", parameters.RepositoryName);
        Assert.Equal("1.2", parameters.Tag);
        Assert.Equal("Ada Lovelace <ada@example.com>", parameters.Author);
        Assert.Equal("snapshot after migration", parameters.Comment);
        Assert.False(parameters.Pause);
        Assert.Equal(["CMD /bin/sh", "ENV MODE=debug"], parameters.Changes);
    }

    [Fact]
    public void Create_DefaultsPauseToTrueAndOmitsEmptyChanges()
    {
        var parameters = ImageCommitParametersFactory.Create("project-app:latest", new ComposeCommitOptions
        {
            Service = "app"
        });

        Assert.Equal("project-app", parameters.RepositoryName);
        Assert.Equal("latest", parameters.Tag);
        Assert.True(parameters.Pause);
        Assert.Null(parameters.Changes);
        Assert.Null(parameters.Author);
        Assert.Null(parameters.Comment);
    }

    [Fact]
    public void ToDockerfileChanges_KeepsInstructionOnlyWhenValueIsEmpty()
    {
        var changes = ImageCommitParametersFactory.ToDockerfileChanges(new Dictionary<string, string>
        {
            ["EXPOSE"] = "8080",
            ["LABEL"] = ""
        });

        Assert.Equal(["EXPOSE 8080", "LABEL"], changes);
    }

    [Fact]
    public void ToDockerfileChanges_RejectsBlankInstructionKeys()
    {
        Assert.Throws<ArgumentException>(() => ImageCommitParametersFactory.ToDockerfileChanges(new Dictionary<string, string>
        {
            ["  "] = "value"
        }));
    }

    [Fact]
    public void SplitImage_TreatsRegistryPortAsRepositoryNotTag()
    {
        Assert.Equal(("localhost:5000/app", "1.0"), ImageManager.SplitImage("localhost:5000/app:1.0"));
        Assert.Equal(("localhost:5000/app", "latest"), ImageManager.SplitImage("localhost:5000/app"));
        Assert.Equal(("app", "latest"), ImageManager.SplitImage("app"));
    }

    [Fact]
    public void CreateAuthConfig_PassesRegistryCredentialsThrough()
    {
        var auth = ImageManager.CreateAuthConfig(new DockerRegistryAuth
        {
            Username = "publisher",
            Password = "secret",
            Email = "publisher@example.com",
            ServerAddress = "registry.example.com"
        });

        Assert.Equal("publisher", auth.Username);
        Assert.Equal("secret", auth.Password);
        Assert.Equal("publisher@example.com", auth.Email);
        Assert.Equal("registry.example.com", auth.ServerAddress);
    }

    [Fact]
    public void CreateAuthConfig_UsesEmptyCredentials_WhenAuthIsMissing()
    {
        var auth = ImageManager.CreateAuthConfig(null);

        Assert.Null(auth.Username);
        Assert.Null(auth.Password);
        Assert.Null(auth.ServerAddress);
    }

    [Fact]
    public void PublishResult_SucceedsOnlyWhenBothTagAndPushSucceed()
    {
        var succeeded = new PublishResult { Service = "app", Tagged = true, Pushed = true };
        var tagOnly = new PublishResult { Service = "app", Tagged = true, Error = "Push failed: denied" };
        var missingImage = new PublishResult { Service = "app", Error = "Service does not define an image reference." };

        Assert.True(succeeded.Succeeded);
        Assert.False(tagOnly.Succeeded);
        Assert.False(missingImage.Succeeded);
    }

    [Fact]
    public async Task CommitAsync_Throws_WhenArgumentsAreMissingOrCancellationIsRequested()
    {
        var service = new Engine.ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "commit-validation",
            WorkingDirectory = Path.GetTempPath()
        };

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CommitAsync(context, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CommitAsync(context, new ComposeCommitOptions { Service = " " }));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CommitAsync(
            context,
            new ComposeCommitOptions { Service = "app" },
            cancellation.Token));
    }

    [Fact]
    public async Task PublishAsync_Throws_WhenRepositoryIsBlankOrCancellationIsRequested()
    {
        var service = new Engine.ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "publish-validation",
            WorkingDirectory = Path.GetTempPath()
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.PublishAsync(context, " "));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PublishAsync(
            context,
            "example/app",
            cancellationToken: cancellation.Token));
    }
}
