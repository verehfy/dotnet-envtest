# Contributing

Thanks for helping improve Kubernetes.EnvTest!

## Prerequisites

- .NET SDK 10.0.100 or newer (`global.json` rolls forward within the 10.0 feature band)
- `make` (Linux/macOS) or PowerShell 7+ (Windows) for the convenience targets
- Network access for integration tests (they download real control-plane binaries on first run)

## Repository layout

```
src/
  Kubernetes.EnvTest/               Core library (TestEnvironment, control plane, CRDs, webhooks)
  Kubernetes.EnvTest.Provisioning/  Binary provisioning shared by the library and the CLI
  Kubernetes.EnvTest.Tool/          The `setup-envtest` dotnet tool (Native AOT)
test/
  Kubernetes.EnvTest.Tests/             Unit tests (xUnit)
  Kubernetes.EnvTest.IntegrationTests/  Aspire-driven end-to-end tests
  Kubernetes.EnvTest.AppHost/           Aspire AppHost provisioning a real control plane
  Kubernetes.EnvTest.SampleWorker/      Sample consumer app orchestrated by the AppHost
  Kubernetes.EnvTest.Benchmarks/        BenchmarkDotNet benchmarks
```

## Building and testing

```sh
make build              # dotnet build (Release)
make test               # unit tests — fast, no network
make test-integration   # end-to-end: downloads binaries, starts etcd + kube-apiserver
make bench              # BenchmarkDotNet suites
make format             # apply code style; `make format-check` is what CI runs
make pack               # produce the NuGet packages locally (current RID only)
```

On Windows, use `./scripts/make.ps1 <target>` with the same target names.

Unit tests must stay hermetic (no network, no real binaries). Anything that starts a real
control plane belongs in the integration test project. Prefer integration coverage for
behavior and reserve unit tests for genuinely intricate logic (version resolution, argument
rendering, parsing) — that trade-off keeps refactoring cheap.

## Coding standards

Microsoft style guidelines plus default ReSharper rules, enforced by `.editorconfig` and
Roslyn analyzers (`TreatWarningsAsErrors` is on; CI runs `dotnet format --verify-no-changes`).
Where the two conflict, Microsoft wins — every known conflict is annotated with a
`ReSharper conflict:` comment in `.editorconfig`. All public APIs need full XML docs (enforced
by the build for `src/`).

## Porting changes from upstream controller-runtime

The library mirrors `controller-runtime/pkg/envtest` behaviorally. When upstream changes:

1. Diff the relevant Go packages between the last-synced and current upstream version:
   `pkg/envtest`, `pkg/internal/testing/{controlplane,process,certs,addr}`, and (for the CLI)
   `tools/setup-envtest`.
2. Map the change through the type table: `Environment` → `TestEnvironment`,
   `process.Arguments` → `ArgumentSet`, `process.State` → `Internal/ProcessState`,
   `certs.TinyCA` → `TinyCa`, `controlplane.APIServer/Etcd` → `ApiServer`/`Etcd`,
   `binaries.go` → `Kubernetes.EnvTest.Provisioning`.
3. Behavioral parity is the bar: same env vars, same defaults, same store layout, same error
   conditions. Deviations must be deliberate, documented in the README, and ideally discussed
   in an issue first.
4. Do not add Go-side deprecated APIs (e.g. template-based `Args`); those are intentionally
   not ported.

## Pull requests

- Every PR runs build, format check, unit tests, integration tests, and a Native AOT publish
  smoke test (see `.github/workflows/pr.yml`).
- Keep `TreatWarningsAsErrors` green — no `#pragma` suppressions without a justification
  comment.

## Releases

Releases are cut by pushing a SemVer tag: `vX.Y.Z`, optionally with `-prerelease` and/or
`+metadata` (e.g. `v0.2.0-rc.1`). The release workflow validates the tag, packs
`Kubernetes.EnvTest`, `Kubernetes.EnvTest.Provisioning`, the tool manifest package, and one
Native AOT tool package per active RID, then pushes everything to NuGet and creates a GitHub
release. Only the Linux RIDs are active initially — the Windows/macOS matrix entries are
present but commented out in `.github/workflows/release.yml`; enable them together with the
matching `<RuntimeIdentifiers>` entries in `Kubernetes.EnvTest.Tool.csproj`.
