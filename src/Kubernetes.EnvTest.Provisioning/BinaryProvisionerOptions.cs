namespace Kubernetes.EnvTest.Provisioning;

/// <summary>Configuration for <see cref="BinaryProvisioner"/>.</summary>
public sealed class BinaryProvisionerOptions
{
    /// <summary>
    /// Gets or sets the requested Kubernetes version: an exact version
    /// (<c>1.31.0</c>), a release series (<c>1.31</c>), or <see langword="null"/> /
    /// <c>latest</c> for the latest stable release in the index.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the URL of the <c>envtest-releases.yaml</c> release index.
    /// Defaults to <see cref="ReleaseIndex.DefaultIndexUrl"/>.
    /// </summary>
    public string IndexUrl { get; set; } = ReleaseIndex.DefaultIndexUrl;

    /// <summary>
    /// Gets or sets the store directory holding downloaded binaries. Defaults to
    /// the setup-envtest-compatible per-user store
    /// (<see cref="BinaryStore.GetDefaultDirectory"/>), so binaries are shared
    /// with the Go tooling.
    /// </summary>
    public string? StoreDirectory { get; set; }

    /// <summary>
    /// Gets or sets the target platform. Defaults to the current process's
    /// platform; set explicitly to pre-fetch binaries for another OS/architecture.
    /// </summary>
    public ReleasePlatform? Platform { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether phase durations (index fetch,
    /// download, extraction) are reported in log messages.
    /// </summary>
    public bool LogTimings { get; set; } = true;
}
