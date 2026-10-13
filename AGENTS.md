# ComposeSharp Contributor Guide

## Instruction authority

This file is the repository's contributor contract. Read it before multi-step work and apply its relevant requirements as acceptance criteria; do not treat it as background-only guidance.

## Purpose and architecture

ComposeSharp is an in-process .NET SDK for loading Compose-shaped projects and managing their Docker Engine resources. It is not a `docker compose` process wrapper and it does not claim complete Docker Compose CLI compatibility.

The solution is split intentionally:

| Project | Responsibility |
| --- | --- |
| `ComposeSharp.Api` | Public contracts: `IComposeService`, project context, options, callbacks, and result types. Keep this package free of Docker.DotNet and YAML parsing implementation details. |
| `ComposeSharp.Loader` | YAML loading, `.env`/`env_file` handling, interpolation, merge behavior, and typed Compose models. |
| `ComposeSharp.Engine` | Docker.DotNet-backed project lifecycle and Docker resource operations. |
| `ComposeSharp.DependencyInjection` | `IServiceCollection` registration only; do not put orchestration behavior here. |
| `tests/ComposeSharp.Tests` | Loader and public-contract tests that do not require Docker. |
| `tests/ComposeSharp.IntegrationTests` | Docker-backed smoke tests. |

Use `ComposeProjectContext` as the project boundary. Engine-created resources must remain queryable and removable by their Compose project labels; do not scan or modify arbitrary Docker resources.

## Current implementation boundaries

Documented limitations are part of the project contract. Do not conceal a partial implementation behind a CLI-shaped API name.

- `BuildAsync`, `CopyAsync`, `ExportAsync`, and `CommitAsync` use managed Docker Engine APIs (`BuildImageFromDockerfileAsync` with a generated build-context archive, container archive endpoints, container export, and container commit). `PublishAsync` tags and pushes service images and returns per-service outcomes.
- `TopAsync` lists processes of running service containers through the Docker Engine process-list API; non-running containers are omitted.
- `ListAsync` derives stack status from all project-labeled containers (`running`, `exited`, `partial`, or `unknown`); projects without running containers are omitted unless `ComposeListOptions.All` is set.
- `EventsAsync` consumes the Docker event stream filtered by the project label. Events without a Compose service label are reported with a null service and dropped when specific services are selected.
- `WatchAsync` continuously reports selected build-context changes for all associated services; it does not start, rebuild, synchronize, prune, or interpret develop.watch. Cancellation and watcher errors propagate.
- `GenerateAsync` returns the summary plus a normalized YAML snapshot of loader-retained fields; it does not reconstruct unmodeled source fields, inspect containers, or write files.
- Multi-file loading implements an incremental subset of Compose merge semantics ([docs/merge-semantics.md](docs/merge-semantics.md)): mappings and same-named services merge recursively/field-by-field, `command`/`entrypoint`/`healthcheck.test` replace wholesale, and `!reset`/`!override` tags are rejected. It is not the full Compose Specification.
- `depends_on` starts selected services with their transitive dependencies in stable topological order. Missing dependencies and cycles fail with diagnostics. The engine waits for a dependency to be running when it has no enabled health check, and healthy when it does; long-form dependency conditions are not modeled.

Before expanding a public behavior, check [docs/roadmap.md](docs/roadmap.md) and the matching GitHub milestone/issue. Add a focused compatibility fixture or Docker integration test with every behavior change.

## Development workflow

The repository targets .NET 8, .NET 9, and .NET 10. Run all three target frameworks unless the change is strictly documentation-only:

```powershell
dotnet restore ComposeSharp.sln
dotnet build ComposeSharp.sln --configuration Release --no-restore
dotnet test ComposeSharp.sln --configuration Release --no-build --no-restore -p:TestTfmsInParallel=false
dotnet pack ComposeSharp.sln --configuration Release --no-build --no-restore --output artifacts/packages
./scripts/VerifyPackageReadme.ps1
```

The integration project checks `docker info` before exercising Docker. When Docker is unavailable it returns early, so an apparently green integration run is not proof of Docker behavior. Run Docker integration work against a reachable daemon and state that environment in the PR.

The test command disables parallel target-framework runs because Docker integration tests share a daemon. Keep that setting unless the tests are isolated from one another.

## Rider code inspection

Use Rider's **Code | Inspect Code** for C# changes. Inspect the changed files with the solution's inspection settings, review findings at Suggestion severity or higher, and fix actionable findings in the changed files before committing. Ignore spelling, proofreading, and grammar findings. If a suggestion is unsafe or inapplicable, use a narrowly scoped suppression with a reason or record why it is a false positive; do not suppress inspections globally.

For a repeatable command-line report using Rider's inspection engine, install JetBrains ReSharper command-line tools once with `dotnet tool install --global JetBrains.ReSharper.GlobalTools`, then run from PowerShell:

```powershell
$reportPath = Join-Path $env:TEMP "composesharp-inspectcode.sarif"
jb inspectcode ComposeSharp.sln --severity=SUGGESTION --include="src/ComposeSharp.Engine/ComposeService.cs;tests/ComposeSharp.Tests/StackStatusTests.cs" --output="$reportPath"
```

Replace the example paths with every changed C# file, keeping the solution as the analysis context so project references resolve. Review the report against the changed-file list; solution-context analysis may also report unrelated findings. Before a release is signed off or tagged, inspect the whole solution at Suggestion severity or higher and resolve all actionable code findings, excluding spelling, proofreading, and grammar.

On Windows the default endpoint is `npipe://./pipe/docker_engine`; on Unix it is `unix:///var/run/docker.sock`. Keep custom socket handling platform-aware.

## Code and API expectations

- Keep `CancellationToken` flow intact across public async operations and Docker calls.
- Prefer typed result objects and callbacks over console output. Do not add new command-line parsing behavior to the public SDK.
- Avoid breaking changes to `ComposeSharp.Api`; add focused options or results only when their semantics are testable and documented.
- Validate source file, service name, and property in loader errors whenever that context is available.
- Keep Docker resource ownership constrained to Compose labels and project names.
- Treat Docker credentials, `.env` files, local Compose overrides, and daemon endpoints as local configuration. Never commit them.
- 不得在项目的任何文件、分支名称、提交信息或拉取请求内容中使用该产品名称。
- Do not include any AI tool or assistant name, brand, or identifying marker in any project information — repository content, documentation, commit messages, branch names, or pull-request text.

## Comments and XML documentation

Comments and XML docs in source are written in English. Chinese belongs in `README.zh-CN.md` and other user-facing docs, not in code.

- Every public type and member in `ComposeSharp.Api` gets an XML `<summary>` describing the contract and the label/project scope. Use `<remarks>` for caller-visible limitations and non-obvious semantics (for example the `depends_on` gap, or a `StopAsync` that now waits for a terminal state). Use `<c>`, `<see cref="..."/>`, and `<paramref name="..."/>` where they help; do not add empty `<param>`/`<returns>` shells.
- Public entry points in `Loader`, `Engine`, and `DependencyInjection` follow the same rule. Internal helpers need docs only when the contract is non-obvious (`///` on a method whose name alone would mislead).
- Inline `//` comments explain **why**, not **what**: stage ordering in a multi-step path, label/ownership invariants, Docker API quirks (units, sync vs async, known races), replace-versus-merge decisions, and why a bounded wait or fallback exists. Do not narrate line by line or restate the identifier.
- Prefer one comment that names the invariant over several that rephrase the code. If a comment would still be true after deleting the line it sits on, delete the comment.
- Do not add banners, section rules, empty tags, author names, or AI-tool markers in comments or XML documentation.
- When behavior changes, update the matching `<summary>`/`<remarks>` in the same commit. Remarks that describe a former implementation are worse than none.

## Documentation and CHANGELOG

Documentation is part of the change, not a follow-up. Every pull request that alters public behavior, compatibility claims, supported scope, or documented limitations must leave code and docs in agreement **in the same PR**. A merge that leaves docs stale is incomplete; do not file a separate "docs later" issue as a substitute.

### Documentation sync matrix

Update every target that the change invalidates. When in doubt, prefer updating too many docs over leaving a stale claim.

| If the change touches… | Also update in the same PR |
| --- | --- |
| Public API surface, options, results, callbacks, or call semantics | Matching XML `<summary>`/`<remarks>`; `README.md`; `README.zh-CN.md` |
| User-visible behavior or capability claims | `README.md` and `README.zh-CN.md` |
| Parsed vs applied Compose fields, partial support | [docs/compose-field-matrix.md](docs/compose-field-matrix.md) |
| Multi-file load / merge behavior | [docs/merge-semantics.md](docs/merge-semantics.md) |
| Implementation boundaries (placeholder shipped, limitation lifted, new gap) | **Current implementation boundaries** in this file |
| Milestone scope, planned vs shipped work | [docs/roadmap.md](docs/roadmap.md) |
| Release/tag/publish process | [docs/maintainer-release.md](docs/maintainer-release.md) |
| User-visible change (feature, fix, breaking, security) | `CHANGELOG.md` under `[Unreleased]` |

Rules that apply across the matrix:

- The root `README.md` is packed into every NuGet package. Its claims must match the code that ships in that version; never document an API that the package does not expose.
- Keep `README.md` and `README.zh-CN.md` parallel. When English user-facing text changes, mirror it in the Chinese README in the same PR (capability lists, tables, version notes, examples). Do not let one language lag.
- Use the compatibility matrix and roadmap to distinguish parsed fields, applied fields, partial behavior, and planned work. Do not blur those categories to make a feature look complete.
- XML `<remarks>` and prose docs that describe a former implementation are worse than none — rewrite or delete them when behavior changes.
- Prefer repository-relative Markdown links. Validate local link targets before merge.
- Comment-only and test-only PRs usually need no README or CHANGELOG entry; say so in the PR description so reviewers do not hunt for missing docs.

### CHANGELOG

`CHANGELOG.md` follows a Keep a Changelog layout under Semantic Versioning. Categories already used in this repository: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`, and `Breaking changes` (when the public contract of `ComposeSharp.Api` or another package changes).

- Every PR that changes user-visible behavior, public API, compatibility claims, or fixes a user-facing bug adds a one-line entry under `## [Unreleased]` in the same PR. Internal-only refactors, pure tests, and comment-only work may omit an entry.
- Entries are user-facing and outcome-oriented: describe behavior, reliability, compatibility, or security impact — not test mechanics, refactor narration, or review history.
- Security fixes belong under `Security`, with the advisory identifier when it is public.
- Write the entry as if it will be read in the release notes without the PR title: one line, past tense or factual present, concrete nouns (`StopAsync` waits for a terminal state), no issue numbers as the only content.
- Keep `[Unreleased]` current on every behavior PR so a release is a section move, not an archaeology exercise. Reconstructing entries from `git log <last-tag>..master` is the exception, not the norm.
- Preserve category grouping when moving `[Unreleased]` into a dated section. Match the established layout exactly: `## [x.y.z] - YYYY-MM-DD`, ordered categories, one blank line between release sections.
- Maintain the compare-link block at the bottom. Advance `[Unreleased]` to compare `<current>...HEAD` and add `[x.y.z]: …/compare/<previous>...<current>` when cutting a release. Inspect the rendered diff of the release section and the link definitions before tagging.

## Issues, pull requests, and releases

`master` is protected. Use a focused branch and pull request; do not bypass required CI, discussion resolution, or branch protection. The required GitHub check is **Build, test, and pack**.

Pull requests must describe what changed, why it is needed, and how it was tested. Link relevant issues with closing keywords in the PR **body** (`Fixes #N` / `Closes #N`) so GitHub auto-closes them on merge — closing keywords only in commit messages do not work. Complete the repository PR checklist, including the documentation and CHANGELOG items above, and avoid unrelated generated files, IDE metadata, packages, or secrets.

After a pull request is confirmed merged, promptly remove its local and remote branches and any associated worktree. Before removing a worktree, inspect its status and branch; preserve and report worktrees with uncommitted changes or commits not included in the merged target instead of deleting them. Keep the current working branch and worktree when they still contain active work.

When you address a PR review comment, reply on that thread **and resolve the thread** (set `isResolved` / close the review comment) in the same turn. Do not leave threads open after the fix is pushed.

Release tags use plain SemVer, for example `2.1.0` rather than `v2.1.0`. Before tagging, move `[Unreleased]` entries into a dated `CHANGELOG.md` section (or confirm they are already filed), bump the package version, and keep package documentation and compatibility claims aligned with the code being released. The release workflow publishes through NuGet Trusted Publishing. Package ownership and the repository's NuGet trust policy are external prerequisites; follow [docs/maintainer-release.md](docs/maintainer-release.md).
