using ComposeSharp.Api;
using ComposeSharp.Loader.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

internal sealed class ImageManager
{
    private readonly DockerClientFactory _factory = new();

    public async Task PullImageAsync(DockerClient client, DockerRegistryAuth? auth, string image, CancellationToken ct)
    {
        var (fromImage, tag) = SplitImage(image);
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = fromImage, Tag = tag },
            CreateAuthConfig(auth),
            new Progress<JSONMessage>(),
            ct);
    }

    public async Task PullImagesAsync(DockerClient client, ComposeProject project, DockerRegistryAuth? auth, IReadOnlyList<string>? services, CancellationToken ct)
    {
        var targetServices = services is { Count: > 0 }
            ? project.Services.Where(s => services.Contains(s.Name)).ToList()
            : project.Services;

        foreach (var service in targetServices)
        {
            if (service.Image is not null)
                await PullImageAsync(client, auth, service.Image, ct);
        }
    }

    public async Task PushImageAsync(DockerClient client, DockerRegistryAuth? auth, string image, CancellationToken ct)
    {
        var (fromImage, tag) = SplitImage(image);
        await client.Images.PushImageAsync(fromImage, new ImagePushParameters { Tag = tag }, CreateAuthConfig(auth), new Progress<JSONMessage>(), ct);
    }

    public async Task<IReadOnlyList<Api.ImageSummary>> ListImagesAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, CancellationToken ct)
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = LabelHelper.ProjectLabelFilter(projectName)
        }, ct);

        var result = new List<Api.ImageSummary>();
        var seen = new HashSet<string>();

        foreach (var container in containers)
        {
            if (services is { Count: > 0 })
            {
                var labels = container.Labels ?? new Dictionary<string, string>();
                labels.TryGetValue(ComposeConstants.ServiceLabel, out var svc);
                if (svc is null || !services.Contains(svc)) continue;
            }

            if (!seen.Add(container.ImageID)) continue;

            try
            {
                var inspect = await client.Images.InspectImageAsync(container.ImageID, ct);
                result.Add(new Api.ImageSummary
                {
                    ID = inspect.ID,
                    Repository = container.Image.Split(':').FirstOrDefault() ?? container.Image,
                    Tag = container.Image.Split(':').LastOrDefault() ?? "latest",
                    Size = inspect.Size,
                    Created = inspect.Created
                });
            }
            catch (DockerApiException) { }
        }

        return result;
    }

    public static AuthConfig CreateAuthConfig(DockerRegistryAuth? auth)
        => new() { Username = auth?.Username, Password = auth?.Password, Email = auth?.Email, ServerAddress = auth?.ServerAddress };

    public static (string FromImage, string Tag) SplitImage(string image)
    {
        var slash = image.LastIndexOf('/');
        var colon = image.LastIndexOf(':');
        if (colon > slash)
            return (image[..colon], image[(colon + 1)..]);
        return (image, "latest");
    }
}
