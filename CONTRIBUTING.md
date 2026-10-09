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
- Update XML comments, `README.md` / `README.zh-CN.md`, relevant `docs/*` guides, and `CHANGELOG.md` `[Unreleased]` in the same PR when public behavior or compatibility claims change. Follow the matrix below to identify the affected documents.
- Include a minimal Compose file or Docker reproduction when fixing compatibility behavior.

## Documentation and CHANGELOG

Documentation is part of the change. A PR that changes public behavior, supported scope, compatibility claims, or documented limitations must update the affected documentation in the same PR.

| Change | Update in the same PR |
| --- | --- |
| Public API, options, results, callbacks, or call semantics | Matching XML summaries and remarks; `README.md`; `README.zh-CN.md` |
| User-visible behavior or capability claims | `README.md` and `README.zh-CN.md` |
| Parsed versus applied Compose fields or partial support | `docs/compose-field-matrix.md` |
| Multi-file loading or merge behavior | `docs/merge-semantics.md` |
| Implementation boundaries or planned work | Current implementation guidance and `docs/roadmap.md` |
| Release, tag, or publishing process | `docs/maintainer-release.md` |
| User-visible feature, fix, breaking change, or security change | `CHANGELOG.md` under `[Unreleased]` |

Keep the English and Chinese README content in sync, and distinguish parsed, applied, partial, and planned behavior. Changelog entries describe user-visible outcomes, not tests or review steps. Pure test, comment, and internal-only changes may omit an entry. When preparing a release, move `[Unreleased]` entries into a dated version section and update the compare links at the bottom of the changelog.

## Release Process

Maintainers publish a version by pushing a plain SemVer tag:

```powershell
git tag 2.0.0
git push origin 2.0.0
```

NuGet Trusted Publishing configuration and release details are in [docs/maintainer-release.md](docs/maintainer-release.md).
