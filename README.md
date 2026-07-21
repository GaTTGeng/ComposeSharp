# ComposeSharp

[![CI](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ComposeSharp.Engine.svg)](https://www.nuget.org/packages/ComposeSharp.Engine)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ComposeSharp.Engine.svg)](https://www.nuget.org/packages/ComposeSharp.Engine)
[![License](https://img.shields.io/github/license/GaTTGeng/ComposeSharp.svg)](LICENSE)

[简体中文](README.zh-CN.md)

ComposeSharp is a managed Docker Compose SDK for .NET. It loads Compose files and exposes Compose-style project, service, container, image, network, volume, log, event, and lifecycle operations, primarily through `Docker.DotNet`; it does not invoke the Docker Compose CLI.

## Packages

| Package | Purpose |
| --- | --- |
| [`ComposeSharp.Api`](https://www.nuget.org/packages/ComposeSharp.Api) | Public Compose service contract, request options, results, callbacks, and project context. |
| [`ComposeSharp.Loader`](https://www.nuget.org/packages/ComposeSharp.Loader) | Compose YAML loading, interpolation, merge support, and strongly typed Compose models. |
| [`ComposeSharp.Engine`](https://www.nuget.org/packages/ComposeSharp.Engine) | Docker.DotNet-backed implementation of `IComposeService`. |
| [`ComposeSharp.DependencyInjection`](https://www.nuget.org/packages/ComposeSharp.DependencyInjection) | `IServiceCollection` registration helpers for ASP.NET Core and hosted applications. |

Most applications should install `ComposeSharp.Engine`. For dependency injection, install `ComposeSharp.DependencyInjection` instead; it brings in the engine transitively.

## Requirements

- .NET 8, .NET 9, or .NET 10.
- A Docker Engine reachable through the Docker.DotNet default endpoint or your application's Docker client configuration.
- A Compose file for project-based operations.

The SDK does not shell out to `docker compose`. Docker Engine permissions, image credentials, and platform support remain the responsibility of the Docker daemon and its configuration.

## Installation

```powershell
dotnet add package ComposeSharp.Engine
```

For ASP.NET Core dependency injection:

```powershell
dotnet add package ComposeSharp.DependencyInjection
```

## Quick Start

Load a Compose project and inspect its services:

```csharp
using ComposeSharp.Api;
using ComposeSharp.Engine;

var compose = new ComposeService();
var project = new ComposeProjectContext
{
    ProjectName = "sample",
    WorkingDirectory = @"C:\src\sample"
};

var config = compose.LoadProject(project);
foreach (var (name, service) in config.Services)
{
    Console.WriteLine($"{name}: {service.Image}");
}

var containers = await compose.PsAsync(project);
foreach (var container in containers)
{
    Console.WriteLine($"{container.Name}: {container.State}");
}
```

Register ComposeSharp with the standard .NET service container:

```csharp
using ComposeSharp.Api;
using ComposeSharp.DependencyInjection;

builder.Services.AddComposeSharp();

// Later, resolve IComposeService from DI.
```

## Current Scope

- Compose file loading, environment interpolation, and multi-file merge support.
- Lifecycle operations including build, create, up, down, start, stop, restart, pause, remove, pull, push, and kill.
- Service execution, attach, copy, logs, events, status, top, images, ports, scale, wait, export, commit, and volume operations.
- Docker Compose v5-compatible intent where supported by Docker.DotNet and the current implementation.

Most operations use Docker.DotNet. The current build and copy implementations invoke the `docker` executable; this is an explicit scope boundary to keep visible until those paths receive managed Docker API implementations.

ComposeSharp is a managed SDK, not a byte-for-byte replacement for the Docker Compose CLI. Validate Docker-specific behavior such as BuildKit features, watch synchronization, registry authentication, and platform-specific socket configuration in your environment.

## Build and Test

```powershell
dotnet restore ComposeSharp.sln
dotnet build ComposeSharp.sln --configuration Release --no-restore
dotnet test ComposeSharp.sln --configuration Release --no-build --no-restore
```

The integration tests automatically return without exercising Docker when no reachable Docker daemon is available.

## Pack

```powershell
dotnet pack ComposeSharp.sln --configuration Release --no-build --output artifacts/packages
```

Package metadata is defined in `src/Directory.Build.props`. Each package includes this README, XML documentation, SourceLink data, and a symbol package.

## Continuous Integration and Release

- `.github/workflows/ci.yml` restores, builds, tests, packs, validates package READMEs, and uploads artifacts for pushes and pull requests.
- `.github/workflows/release-nuget.yml` builds tagged releases and publishes packages to NuGet.org through NuGet Trusted Publishing.

Before the first release, create the NuGet.org packages and configure the trusted publishing policy as described in [the maintainer release guide](docs/maintainer-release.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## Support

See [SUPPORT.md](SUPPORT.md) for usage questions and bug reports.

## License

ComposeSharp is licensed under the [MIT License](LICENSE).
