namespace Kubernetes.EnvTest.Provisioning;

/// <summary>
/// Resolves and, when necessary, downloads the Kubernetes control-plane
/// binaries (<c>kube-apiserver</c>, <c>etcd</c>, <c>kubectl</c>) used by envtest.
/// </summary>
public interface IBinaryProvisioner
{
    /// <summary>
    /// Ensures the binaries for the configured version and platform exist in the
    /// store, downloading and verifying them when missing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the resolution and download.</param>
    /// <returns>The resolved binary paths.</returns>
    /// <exception cref="ReleaseIndexException">The release index could not be fetched or parsed.</exception>
    /// <exception cref="ReleaseVersionNotFoundException">The requested version does not exist in the index.</exception>
    /// <exception cref="PlatformBinariesNotFoundException">The requested version has no binaries for the target OS/architecture.</exception>
    /// <exception cref="BinaryDownloadException">The archive download failed.</exception>
    /// <exception cref="ChecksumMismatchException">The downloaded archive failed SHA-512 verification.</exception>
    Task<EnvTestBinaries> EnsureBinariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and parses the release index configured for this provisioner.
    /// </summary>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The parsed release index.</returns>
    /// <exception cref="ReleaseIndexException">The release index could not be fetched or parsed.</exception>
    Task<ReleaseIndex> GetReleaseIndexAsync(CancellationToken cancellationToken = default);
}
