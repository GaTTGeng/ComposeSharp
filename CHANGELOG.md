# Changelog

All notable changes to ComposeSharp are documented in this file.

This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `GenerateAsync` returns a normalized YAML snapshot of selected loaded services alongside the project summary, with service selection and an output project-name override.
- `TopAsync` returns real process listings for running project service containers through the Docker Engine process-list API.
- `ComposeEventsOptions.OnSubscribed` signals when the Docker event subscription has been issued so callers can avoid racing lifecycle operations against the stream.

### Changed

- `WatchAsync` continuously monitors selected build contexts together, fans out shared-context changes, and reports missing contexts and watcher failures; unsupported pruning and container-based generation selections now fail explicitly.
- `EventsAsync` now consumes the Docker event stream filtered by the project label instead of polling container state on an interval.

### Fixed

- Canceling `WatchAsync` propagates cancellation without emitting a false rebuild notification.

## [3.0.0] - 2026-10-09

### Added

- Build services through Docker Engine with a generated build-context archive, `.dockerignore` handling, build feedback, and cancellation-aware archive creation.
- Copy files to and from service containers and export container filesystems through Docker Engine archive endpoints, including replica selection and archive metadata handling.
- Load common Compose project and service configuration, interpolate variables from the process environment and project `.env`, load service `env_file` values into container environments, merge multiple Compose files using the documented incremental rules, and report contextual validation errors.
- Apply Compose profiles consistently when selecting services for supported project operations, and document parsed versus applied Compose fields in the compatibility matrix.
- Commit service containers as images and publish tagged service images to registries with per-service outcomes.

### Changed

- `StopAsync` waits for containers to reach a terminal state and reports polling failures and timeouts.
- Loader merge behavior, supported Compose fields, and current engine limitations are now documented in dedicated compatibility and merge guides.

### Breaking changes

- `IComposeService.PublishAsync` now returns `Task<IReadOnlyList<PublishResult>>` instead of `Task`. Source call sites that await it or accept a `Task` return remain compatible; consumers compiled against the earlier binary signature must rebuild to use the new assembly.

## [2.0.0] - 2026-07-20

### Added

- Published the open-source SDK repository foundation: NuGet metadata, source-link and symbol packages, CI, trusted-publishing release workflow, contribution guidance, and security/support policies.
- Documented the Api, Loader, Engine, and DependencyInjection packages with .NET and Docker Engine requirements.

[Unreleased]: https://github.com/GaTTGeng/ComposeSharp/compare/3.0.0...HEAD
[3.0.0]: https://github.com/GaTTGeng/ComposeSharp/compare/2.0.0...3.0.0
[2.0.0]: https://github.com/GaTTGeng/ComposeSharp/releases/tag/2.0.0
