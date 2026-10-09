# Contributing

Thanks for considering a contribution to ComposeSharp.

## Development Setup

Install the .NET 8, .NET 9, and .NET 10 SDKs, then run:

```powershell
dotnet restore ComposeSharp.sln
dotnet build ComposeSharp.sln --configuration Release --no-restore
dotnet test ComposeSharp.sln --configuration Release --no-build --no-restore
```

Docker integration tests run only when a reachable Docker daemon is available.

## Pull Request Guidelines

- Keep changes focused and include tests for behavior changes.
- Do not commit `bin/`, `obj/`, IDE metadata, generated packages, or local secrets.
- Preserve cancellation-token behavior and avoid introducing Docker CLI process calls into the managed SDK.
- Update XML comments, `README.md` / `README.zh-CN.md`, relevant `docs/*` guides, and `CHANGELOG.md` `[Unreleased]` in the same PR when public behavior or compatibility claims change. See `AGENTS.md` for the documentation sync matrix.
- Include a minimal Compose file or Docker reproduction when fixing compatibility behavior.

## Release Process

Maintainers publish a version by pushing a plain SemVer tag:

```powershell
git tag 2.0.0
git push origin 2.0.0
```

NuGet Trusted Publishing configuration and release details are in [docs/maintainer-release.md](docs/maintainer-release.md).
