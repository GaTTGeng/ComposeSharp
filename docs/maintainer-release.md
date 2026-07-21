# Maintainer Release Guide

ComposeSharp publishes NuGet packages from GitHub Actions through NuGet Trusted Publishing. Use trusted publishing rather than storing a long-lived NuGet API key in repository secrets.

## One-time NuGet.org setup

Create the following package IDs on NuGet.org under the intended owner account before the first publish:

- `ComposeSharp.Api`
- `ComposeSharp.Loader`
- `ComposeSharp.Engine`
- `ComposeSharp.DependencyInjection`

For each package, create a trusted-publishing policy with these settings:

- Repository owner: `GaTTGeng`
- Repository: `ComposeSharp`
- Workflow file: `release-nuget.yml`
- Environment: leave empty unless the workflow is later changed to use a GitHub environment.

The release workflow uses `NuGet/login@v1`, so it must retain the `id-token: write` permission. If the NuGet.org account name differs from the GitHub repository owner, add the repository variable `NUGET_USER` with the NuGet.org account name.

## GitHub setup

- Set the default branch to `master` or update the release workflow branch validation to your chosen default branch.
- Protect the default branch and require the `CI / Build, test, and pack` check before merging.
- Do not add a `NUGET_API_KEY` secret for normal releases.

## Release a version

Push a plain SemVer tag without a `v` prefix. The tag must point to a commit reachable from `master`.

```powershell
git tag 2.0.0
git push origin 2.0.0
```

The workflow restores, builds, tests, packs, validates package READMEs, uploads package artifacts, and then publishes `.nupkg` and `.snupkg` files to NuGet.org. A failed publish can be re-run after correcting the trusted-publishing policy; duplicate package versions are skipped.

For a prerelease or an intentional manual publish, run **Release NuGet** from GitHub Actions, enter a version such as `2.0.1-preview.1`, and enable publishing.
