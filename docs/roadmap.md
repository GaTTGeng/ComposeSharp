# ComposeSharp Roadmap

This roadmap is a statement of engineering priorities, not a promise that every API shaped like a Docker Compose command has equivalent behavior today. Each milestone is represented on GitHub so that its individual issues, scope changes, and progress remain public.

## Completed: 2.0 — Public baseline

The first public release established the package boundary (`Api`, `Loader`, `Engine`, and `DependencyInjection`), .NET 8–10 targets, package documentation, CI, source packages, and the Trusted Publishing workflow and maintainer guidance. NuGet package ownership and each repository's Trusted Publishing policy remain release prerequisites; see [the maintainer release guide](maintainer-release.md). It also established the current label-based project model and Windows named-pipe default endpoint.

This is a usable baseline, not a compatibility certification.

## 2.1 — Compose model correctness

**Goal:** Make the transition from a Compose document to `ServiceDefinition` predictable, testable, and explicit about unsupported input.

### Exit criteria

- Fixture-based coverage for interpolation, `.env`, `env_file`, ports, health checks, profiles, resource values, and common YAML shapes.
- A documented and test-backed multi-file merge policy, with incremental movement toward Compose Specification semantics where practical.
- Profile selection applied consistently from `ComposeProjectContext`.
- Validation messages that name the relevant source file, service, and property.
- Documentation that distinguishes parsed fields from fields actually applied to Docker Engine; see the [Compose field support matrix](compose-field-matrix.md).

### Not a goal

Full Compose Specification parity in one release. Unsupported or partially applied fields should fail clearly or remain documented rather than silently claim parity.

## 2.2 — Docker Engine coverage

**Goal:** Replace command-shaped shortcuts with observable, cancellable Docker Engine behavior.

### Exit criteria

- Build, commit, copy, and export use Docker Engine APIs with focused coverage.
- `TopAsync` returns real process information.
- `EventsAsync` consumes Docker events instead of periodically polling containers.
- `WatchAsync` has a documented action model rather than only signalling the first change.
- `GenerateAsync` emits a rendered project configuration.
- Remaining publish options have explicit, tested behavior or are removed from the public contract.

### Implemented contracts

`GenerateAsync` now returns a normalized YAML snapshot of the loader's retained model alongside its summary. It supports explicit service selection and an output project name, without Docker inspection or file writes. This is a subset model snapshot, not complete Compose CLI configuration output; see the [generation and watch contract](generate-watch.md).

`WatchAsync` now continuously monitors selected build contexts together, reports changes for each associated service, and propagates cancellation and watcher failures. It remains a notification API: starting, rebuilding, synchronizing, pruning, and interpreting `develop.watch` are outside its contract.

### Not a goal

Reimplement every Docker Compose CLI UX feature. The public SDK should prioritize structured results, cancellation, and diagnostics over terminal output emulation.

## Reliable orchestration

**Goal:** Make repeated project reconciliation safe enough for applications that manage more than one development lifecycle.

### Exit criteria

- Dependency-aware create/start sequencing and health-readiness behavior.
- Reconciliation that can distinguish an unchanged service from one that needs recreation.
- Clear handling for partial failure, cleanup, and orphaned project resources.
- Docker integration coverage on both Windows named pipes and Linux Unix sockets.
- A compatibility matrix that names verified Compose constructs and their tested Docker Engine behavior.

### Not a goal

Replacing Docker Compose as the universal operational interface. ComposeSharp remains an embeddable .NET library.

## How to influence the roadmap

Open an issue with a small Compose file, expected behavior, actual behavior, Docker Engine version, and ComposeSharp version. Compatibility reports should use the repository's dedicated Issue form. Issues are triaged into the GitHub milestones when they have a reproducible scope and a clear acceptance test.
