# Generation and watch contracts

## GenerateAsync

`GenerateAsync` reads the local Compose project and returns `ComposeProjectConfig` with both the existing summary and `RenderedYaml`. It requires no Docker connection and writes no files. `LoadProject` remains a summary operation and leaves `RenderedYaml` null.

```csharp
var generated = await service.GenerateAsync(context,
    new ComposeGenerateOptions
    {
        ProjectName = "preview",
        Services = ["web"]
    }, cancellationToken);
Console.WriteLine(generated.RenderedYaml);
```

Generation uses active profiles when `Services` is null. Explicit service names bypass profile filtering; an empty list selects no services, and unknown names throw. Selecting services does not add their dependencies. The output name override affects only the returned summary and YAML, and does not change resource ownership. Nonempty legacy `Containers` selections throw `NotSupportedException`; migrate to `Services` for configuration selection. Generation does not reconstruct configuration from containers.

The YAML is a normalized snapshot of the loader's typed model. Service fields, build and healthcheck settings, retained deploy values, and top-level resource names are rendered. Environment interpolation has already occurred, and `env_file` values are folded into `environment` rather than keeping an external file reference. Resolved environment values may contain secrets. Paths remain relative to the original Compose file directory; moving the output changes how relative paths resolve.

This snapshot inherits loader limitations from the [field matrix](compose-field-matrix.md). In particular, it cannot recover resource options beyond names, long-form dependency conditions, mapping-shaped `develop`, arbitrary unknown fields, or original scalar versus list command syntax. The unsupported `develop` field is omitted, including scalar values. Retained loader-specific build and deploy shapes are preserved rather than implying full Compose Specification support. Reduced secret/config definitions contain names only and cannot provision their original contents. Use the output for inspection of supported model data; it is not a compatibility guarantee for deployment through Docker Compose CLI.

## WatchAsync

`WatchAsync` is a local file notification stream. It registers every selected build context before awaiting changes and monitors recursively until cancellation, disposal, or failure. It requires no Docker connection and never calls `UpAsync`.

```csharp
await foreach (var change in service.WatchAsync(context,
    new ComposeWatchOptions { Services = ["web", "worker"] }, cancellationToken))
{
    Console.WriteLine($"{change.ServiceName}: {change.Action} {change.Path}");
}
```

Each create, write, delete, or rename notification yields `Action = "rebuild"` with the absolute changed path. Rename reports the new path. This action is a suggestion for the caller, which decides whether and how to rebuild. Shared contexts produce notifications for every associated service, in ordinal service-name order for each filesystem notification. Notifications may repeat; ordering across contexts depends on the filesystem. No debounce or exactly-once guarantee is provided.

Null `Services` uses active profiles. Explicit service names bypass profile filtering; an empty list observes nothing and unknown names throw. Services without a build definition are ignored. A build definition without an explicit context uses the Compose directory. Missing directories throw with service/path context before observation starts. No selected build contexts completes immediately.

The stream throws on watcher errors or overflow of its bounded 1,024-event queue, so callers can restart observation and reconcile rather than assume changes were delivered. Cancellation propagates an `OperationCanceledException` without a synthetic rebuild event. Disposing the iterator releases all watchers.

`NoUp` and `Quiet` are retained compatibility no-ops: the API always observes without starting services or printing progress. `Prune = true` throws `NotSupportedException` before observation. No rebuild, file synchronization, pruning, `develop.watch` actions, or `.dockerignore` filtering is implemented.

## Evidence

Focused tests cover generated model snapshots and filesystem observation without a Docker daemon:

- [Generation tests](../tests/ComposeSharp.Tests/GenerateConfigurationTests.cs).
- [Watch tests](../tests/ComposeSharp.Tests/BuildContextWatcherTests.cs).

These checks run on .NET 8, 9, and 10. Docker lifecycle behavior remains covered separately by the [integration suite](../tests/ComposeSharp.IntegrationTests/ComposeServiceIntegrationTests.cs).
