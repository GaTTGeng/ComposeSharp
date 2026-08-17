# ComposeSharp

[![CI](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml)
[![Engine on NuGet](https://img.shields.io/nuget/v/ComposeSharp.Engine.svg)](https://www.nuget.org/packages/ComposeSharp.Engine)
[![License](https://img.shields.io/github/license/GaTTGeng/ComposeSharp.svg)](LICENSE)

[简体中文](README.zh-CN.md)

ComposeSharp is for .NET applications that need to *own* a Docker Compose project instead of starting `docker compose` as a child process. Give it a working directory and a Compose file; it reads the file, turns services into Docker Engine requests, and keeps the resources grouped with familiar `com.docker.compose.*` labels.

It is deliberately an SDK, not a command-line wrapper and not a promise of Docker Compose CLI parity. That distinction matters: the project is useful today for embedding a small Compose-shaped environment in an application, test harness, developer tool, or local control plane, while its compatibility work remains visible rather than hidden behind CLI-looking method names.

## The shape of the project

```
compose.yml + .env + env_file
            │
            ▼
   ComposeSharp.Loader
   typed services, ports, volumes, networks, deploy hints
            │
            ▼
   ComposeSharp.Engine
   Docker Engine containers, networks, volumes, labels
            │
            ▼
 IComposeService in your application
```

Project ownership is label-based. Containers created for `inventory` are named and queried as part of that project, so `PsAsync`, `DownAsync`, `ScaleAsync`, `LogsAsync`, and volume/network cleanup operate within a project boundary instead of scanning arbitrary Docker resources.

## Start here

Install the engine package when you want to load and run a project:

```powershell
dotnet add package ComposeSharp.Engine
```

```csharp
using ComposeSharp.Api;
using ComposeSharp.Engine;

var compose = new ComposeService();
var project = new ComposeProjectContext
{
    ProjectName = "inventory-dev",
    WorkingDirectory = @"C:\src\inventory"
};

// Resolves docker-compose.yml, compose.yml, compose.yaml, or docker-compose.yaml.
var config = compose.LoadProject(project);
Console.WriteLine(string.Join(", ", config.Services));

await compose.UpAsync(project, new ComposeUpOptions
{
    Pull = "always",
    Scale = new Dictionary<string, int> { ["api"] = 2 },
    LogConsumer = new ConsoleStatus()
});

var containers = await compose.PsAsync(project);
foreach (var container in containers)
    Console.WriteLine($"{container.Service}: {container.State}");

sealed class ConsoleStatus : ILogConsumer
{
    public void OnStatus(string serviceName, string message) =>
        Console.WriteLine($"[{serviceName}] {message}");
    public void OnLog(string serviceName, string message, bool isStdErr) =>
        Console.WriteLine($"[{serviceName}] {message}");
    public void OnLogComplete(string serviceName) { }
}
```

For an application that already uses `IServiceCollection`:

```powershell
dotnet add package ComposeSharp.DependencyInjection
```

```csharp
builder.Services.AddComposeSharp();
```

Resolve `IComposeService` from the container. `ComposeProjectContext` stays explicit at the call site, which keeps project names, working directories, registry credentials, and Docker endpoints visible in multi-project applications.

## What the loader understands

The loader is intentionally useful even when you do not start containers. It reads `.env` and per-service `env_file` files, expands variables in YAML, and returns strongly typed services. It recognizes common service configuration including images, build definitions, commands, entrypoints, environment, ports, volumes, networks, health checks, restart policy, profiles, labels, logging, Linux capabilities, resource hints, secrets, configs, and selected `deploy` fields.

### Variable interpolation

When expanding YAML, ComposeSharp uses the process environment first and then the project `.env` file. A service's `env_file` contributes only to that container's environment; it is not an interpolation source. Unset variables expand to an empty value unless a `-`/`:-` default or `?`/`:?` required-value form is used. `$$` produces a literal `$`. Required-variable errors name both the variable and the Compose file.

`ComposeFileLoader.LoadMerged` accepts several files and applies an incremental, field-level merge: later scalars replace earlier values, mappings merge recursively, and lists append with focused replacement rules for service resources, `command`, and `entrypoint`. It is not Docker Compose's complete merge algorithm; see [the merge semantics](docs/merge-semantics.md) for the exact supported rules and unsupported YAML tags.

`ComposeProjectContext.Profiles` selects services consistently for project loading and operations that load the Compose file. Services without `profiles` are always selected; a profiled service is selected when any of its profiles is active. An operation that explicitly names a service can select it even when its profile is not active.

The loader can retain fields that the engine does not yet apply. Consult the [Compose field support matrix](docs/compose-field-matrix.md) before relying on a Compose property at runtime.

## What the engine does today

| Area | Current behavior |
| --- | --- |
| Project lifecycle | Creates project networks, creates/starts/removes labeled containers, lists project containers, removes project networks and optional volumes, and builds selected services through Docker Engine. |
| Service operations | Supports start, stop, restart, pause, unpause, kill, remove, run, exec, attach, pull, push, scale, wait, and port lookup. |
| Inspection | Provides project list, containers, images, volumes, logs, and a DOT graph via `VizAsync`. |
| Streaming | `LogsAsync` streams Docker logs. `EventsAsync` polls project containers every two seconds; it is not a Docker event-stream subscription. |
| File change signal | `WatchAsync` observes build-context changes and yields a `rebuild` notification. It does not rebuild or synchronize files itself. |

### Important implementation boundaries

These boundaries are part of the public contract today:

- `BuildAsync` sends a tarred local build context to the Docker Engine API. It applies `.dockerignore` or the selected Dockerfile's `.dockerignore`, preserves symbolic links and Unix file modes, skips ignored directory trees unless a negated rule can restore a descendant, stages an external Dockerfile safely, observes cancellation while creating the archive, and spools that archive to a temporary file rather than process memory. Valueless build arguments inherit their process-environment value when set. It supports a Dockerfile, tags, target, build arguments, labels, `cache_from`, network mode, extra hosts, shared-memory and memory limits, one platform, pull, and no-cache. `ComposeBuildOptions.LogConsumer` receives Docker build status messages, and build-stream errors fail the operation. BuildKit-specific `cache_to`, multiple platforms, `privileged`, `builder`, and progress-mode selection are not applied.
- `CopyAsync`, `ExportAsync`, and `CommitAsync` invoke the `docker` executable. The rest of the core lifecycle uses Docker.DotNet.
- `TopAsync` currently returns an empty list.
- `GenerateAsync` reports the loaded project configuration; it does not emit a rendered Compose file.
- `PublishAsync` tags service images for a repository; it does not push them.
- `depends_on` is represented in the model and graph, but it is not yet a complete Compose dependency and health-readiness scheduler.
- The default Docker endpoints are `npipe://./pipe/docker_engine` on Windows and `unix:///var/run/docker.sock` on Unix. A custom `SocketPath` can use an `npipe://` or `unix://` URI.

If you need exact CLI behavior, BuildKit breadth, Compose watch synchronization, or a full Compose Specification merge/interpolation implementation, use Docker Compose CLI directly for now. Issues that demonstrate a minimal compatibility gap are especially valuable: [open one](https://github.com/GaTTGeng/ComposeSharp/issues/new?template=compose_compatibility_gap.yml).

## Packages

| Package | Use it when you need… |
| --- | --- |
| [`ComposeSharp.Api`](https://www.nuget.org/packages/ComposeSharp.Api) | Contracts, options, results, callbacks, and `ComposeProjectContext`. |
| [`ComposeSharp.Loader`](https://www.nuget.org/packages/ComposeSharp.Loader) | Typed Compose-file loading without Docker access. |
| [`ComposeSharp.Engine`](https://www.nuget.org/packages/ComposeSharp.Engine) | The Docker Engine implementation of `IComposeService`. |
| [`ComposeSharp.DependencyInjection`](https://www.nuget.org/packages/ComposeSharp.DependencyInjection) | `AddComposeSharp()` for Microsoft dependency injection. |

All packages target .NET 8, .NET 9, and .NET 10. The engine needs permission to reach Docker Engine; ComposeSharp does not manage Docker Desktop, daemon permissions, registry login state, or remote TLS configuration for you.

## Roadmap

The roadmap is organized around implementation honesty rather than pretending every CLI-shaped API is complete:

1. **2.1 — Compose model correctness.** Test-backed YAML interpolation and merge semantics, profile selection, service configuration mapping, and clear validation errors.
2. **2.2 — Docker Engine coverage.** Replace process-backed build/copy/export/commit paths, implement real `top`, Docker event streaming, and meaningful project generation/publishing behavior.
3. **3.0 — dependable orchestration.** Dependency ordering and readiness, safer reconciliation, richer diagnostics, and integration coverage across Linux and Windows Docker environments.

Details, acceptance criteria, and non-goals live in [docs/roadmap.md](docs/roadmap.md). The [Compose field support matrix](docs/compose-field-matrix.md) records parsed versus applied behavior. Work is tracked in [GitHub milestones](https://github.com/GaTTGeng/ComposeSharp/milestones).

## Build the repository

```powershell
dotnet restore ComposeSharp.sln
dotnet build ComposeSharp.sln --configuration Release --no-restore
dotnet test ComposeSharp.sln --configuration Release --no-build --no-restore
```

Integration tests exercise Docker only when `docker info` succeeds. Package artifacts can be produced with:

```powershell
dotnet pack ComposeSharp.sln --configuration Release --no-build --output artifacts/packages
```

## Participate

- [Contributing guide](CONTRIBUTING.md)
- [Support and design discussion](SUPPORT.md)
- [Security policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

ComposeSharp is available under the [MIT License](LICENSE).
