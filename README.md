# Kubernetes.EnvTest

A .NET port of [controller-runtime's `envtest`](https://github.com/kubernetes-sigs/controller-runtime/tree/main/pkg/envtest):
spin up a **real Kubernetes control plane** (etcd + kube-apiserver, no nodes) for integration
tests, install CRDs and admission/conversion webhooks into it, and tear it down again — all
from .NET, with no Go toolchain required.

Works on Windows, macOS and Linux (x64 and arm64), targets .NET 10+, and stays interoperable
with the upstream Go tooling: it consumes the same `envtest-releases.yaml` release index and
shares the same on-disk binary store as `setup-envtest`, so caches populated by either
ecosystem are reused by the other.

## Packages

| Package                           | What it is                                                                                                                                             |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Kubernetes.EnvTest`              | The core library: `TestEnvironment` (the port of upstream's `Environment`), control plane management, CRD & webhook installation, certificates, users. |
| `Kubernetes.EnvTest.Provisioning` | Binary provisioning: version resolution, download with SHA-512 verification, setup-envtest-compatible store. Used by the core library and the CLI.     |
| `Kubernetes.EnvTest.Tool`         | The `setup-envtest` dotnet tool — a Native AOT CLI to pre-fetch and inspect control-plane binaries outside of test code (CI pre-warm, local dev).      |

```sh
dotnet add package Kubernetes.EnvTest
dotnet tool install -g Kubernetes.EnvTest.Tool   # provides the `setup-envtest` command
```

## Quick start

```csharp
using k8s;
using Kubernetes.EnvTest;

// Arrange: start a control plane (binaries are downloaded on first use).
var environment = new TestEnvironment
{
    DownloadBinaryAssets = true,          // omit if you pre-fetch binaries (see below)
    DownloadBinaryAssetsVersion = "1.31", // exact "1.31.0", series "1.31", or null for latest
};
environment.CrdDirectoryPaths.Add("config/crd/bases");

KubernetesClientConfiguration config = await environment.StartAsync();

// Act: talk to a real API server with the official client.
using var client = new k8s.Kubernetes(config);
var namespaces = await client.CoreV1.ListNamespaceAsync();

// ... or hand environment.KubeConfig (kubeconfig bytes) to anything else.

// Teardown (also available via `await using`).
await environment.StopAsync();
```

`TestEnvironment` maps 1:1 to upstream's `envtest.Environment` — the type is renamed only
because `Environment` collides with `System.Environment` in C#.

### xUnit fixture example

```csharp
public sealed class ClusterFixture : IAsyncLifetime
{
    public TestEnvironment Environment { get; } = new() { DownloadBinaryAssets = true };
    public KubernetesClientConfiguration Config { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Config = await Environment.StartAsync();
    public async ValueTask DisposeAsync() => await Environment.StopAsync();
}
```

Multiple environments can run concurrently (parallel test assemblies each with their own
etcd/apiserver pair); ports are allocated dynamically and coordinated across processes.

## Getting the control-plane binaries

Binaries are resolved in this order (same semantics as the Go library):

1. `TEST_ASSET_KUBE_APISERVER`, `TEST_ASSET_ETCD`, `TEST_ASSET_KUBECTL` — per-binary overrides.
2. `KUBEBUILDER_ASSETS` — a directory containing all three.
3. `BinaryAssetsDirectory` / explicit `ApiServer.Path` & `Etcd.Path` on the environment.
4. `/usr/local/kubebuilder/bin` as the legacy fallback.

Alternatively set `DownloadBinaryAssets = true` and the environment fetches them at start,
verified against the index's SHA-512 hashes, into the shared per-user store:

- Linux: `${XDG_DATA_HOME:-~/.local/share}/kubebuilder-envtest/k8s`
- macOS: `~/Library/Application Support/io.kubebuilder.envtest/k8s`
- Windows: `%LocalAppData%\kubebuilder-envtest\k8s`

That store uses the same `<version>-<os>-<arch>` layout as the Go `setup-envtest`, so an
existing CI cache keeps working no matter which tool populated it.

### Pre-fetching with the CLI (CI pre-warm)

```sh
# Download (if needed) and print where the binaries live:
setup-envtest use 1.31.0

# Print only the path, e.g. to export it:
export KUBEBUILDER_ASSETS="$(setup-envtest use 1.31.0 -p path -q)"

# Pre-fetch for another platform (e.g. warm a cache used by Windows agents):
setup-envtest use 1.31.0 --os windows --arch amd64
```

The CLI is a thin wrapper over `Kubernetes.EnvTest.Provisioning` — the exact same code the
library runs when `DownloadBinaryAssets` is enabled.

## Supported platforms

`win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`.

Note: upstream publishes **no Windows/arm64 control-plane binaries before Kubernetes
v1.35.0-alpha.3**. Requesting an unavailable version/platform combination fails fast with a
`PlatformBinariesNotFoundException` naming the requested version, OS and architecture.

## Upgrading the Kubernetes version

The set of usable Kubernetes versions is defined entirely by the upstream release index
(`envtest-releases.yaml`, maintained in `kubernetes-sigs/controller-tools`) — this library has
**no hard-coded version list**, so new Kubernetes releases work without a library update:

1. Pick a version: `setup-envtest use 1.32` (or set `DownloadBinaryAssetsVersion = "1.32"`).
   Use a `major.minor` series to float on patch releases, or pin an exact version for
   reproducibility.
2. Run your test suite. Version-specific apiserver/etcd flag differences are detected at
   runtime by probing the binaries (`--help`), the same way upstream does — e.g.
   `--insecure-port` (removed in 1.24) and etcd's `--unsafe-no-fsync` (added in 3.5).
3. Old versions stay cached side by side in the store; remove unused
   `<version>-<os>-<arch>` directories manually if disk space matters.

To pin a custom or air-gapped index, set `DownloadBinaryAssetsIndexUrl` (library) or
`--index` (CLI) to your own `envtest-releases.yaml` URL.

## Staying in sync with the upstream Go project

This library tracks `controller-runtime/pkg/envtest` behaviorally, not line by line:

- **Release index & store**: consumed from upstream directly (see above), so no action is
  needed when Kubernetes versions are added upstream.
- **Behavioral changes** (new `Environment` fields, changed defaults, new flags): watch
  [controller-runtime release notes](https://github.com/kubernetes-sigs/controller-runtime/releases)
  for changes under `pkg/envtest` and `pkg/internal/testing`, then port them here. The type
  mapping is intentionally 1:1 (`Environment` → `TestEnvironment`, `process.Arguments` →
  `ArgumentSet`, `WebhookInstallOptions` → `WebhookInstallOptions`, ...), so diffs against the
  Go source translate mechanically. See [CONTRIBUTING.md](CONTRIBUTING.md) for the porting
  checklist.

## Configuration

Everything configurable on `TestEnvironment` (all optional):

| Property                                               | Purpose                                                                                                                                              |
| ------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DownloadBinaryAssets` / `...Version` / `...IndexUrl`  | Download control-plane binaries at start.                                                                                                            |
| `BinaryAssetsDirectory`                                | Where binaries live / are downloaded to.                                                                                                             |
| `CrdDirectoryPaths`, `Crds`, `CrdInstallOptions`       | CRDs to install (files, directories, or objects).                                                                                                    |
| `WebhookInstallOptions`                                | Admission webhook configs + local serving cert generation.                                                                                           |
| `CrdInstallOptions.ConversionWebhookTypes`             | Group/kinds whose CRD conversion webhooks are patched to the local serving address (the .NET replacement for Go's scheme-based Hub/Spoke detection). |
| `UseExistingCluster`                                   | Skip the local control plane and use your current kubeconfig.                                                                                        |
| `ControlPlaneStartTimeout` / `ControlPlaneStopTimeout` | Component start/stop deadlines.                                                                                                                      |
| `AttachControlPlaneOutput`                             | Stream apiserver/etcd output to the console.                                                                                                         |
| `ControlPlane.GetApiServer().Configure()`              | Add/override/remove any `kube-apiserver` flag (`ArgumentSet`).                                                                                       |
| `ControlPlane.GetEtcd().Configure()`                   | Same for etcd.                                                                                                                                       |
| `LogTimings`                                           | Include per-phase durations in log messages.                                                                                                         |

Environment variables honored for Go compatibility: `USE_EXISTING_CLUSTER`,
`KUBEBUILDER_ASSETS`, `TEST_ASSET_*`, `KUBEBUILDER_CONTROLPLANE_START_TIMEOUT` /
`..._STOP_TIMEOUT` (Go duration syntax like `20s`, `1m30s`), and
`KUBEBUILDER_ATTACH_CONTROL_PLANE_OUTPUT`.

### Dependency injection

```csharp
services.AddKubernetesEnvTest(env =>
{
    env.DownloadBinaryAssets = true;
    env.CrdDirectoryPaths.Add("config/crd/bases");
});

// Provisioning only (e.g. for tooling):
services.AddEnvTestBinaryProvisioning(options => options.Version = "1.31");
```

### Logging

Uses `Microsoft.Extensions.Logging` (`ILogger<T>`) — no third-party logging dependency. Pass
an `ILoggerFactory` to the `TestEnvironment` constructor. Log lines carry the environment
instance id, phase (`StartingControlPlane`, `InstallingCRDs`, ...) and Kubernetes version both
as scope values _and_ in the message text, so output from concurrent environments stays
attributable even without `IncludeScopes = true`.

## Errors

All library failures derive from `EnvTestException` and carry structured context:
`ProcessStartException` (which binary, path, why), `ChecksumMismatchException` (expected vs
actual hash), `PlatformBinariesNotFoundException` (version + OS + arch),
`ReleaseVersionNotFoundException`, `CrdInstallationException`, `WebhookInstallationException`,
`ControlPlaneStartException`, and friends.

## License

[Apache-2.0](LICENSE), the same license as the upstream
[controller-runtime](https://github.com/kubernetes-sigs/controller-runtime) project this
library ports. Not affiliated with the Kubernetes project; the control-plane binaries are
governed by their own upstream licenses.
