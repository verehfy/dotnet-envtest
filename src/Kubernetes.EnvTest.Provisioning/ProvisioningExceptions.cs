namespace Kubernetes.EnvTest.Provisioning;

/// <summary>Thrown when the envtest release index cannot be downloaded or parsed.</summary>
public sealed class ReleaseIndexException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="ReleaseIndexException"/> class.</summary>
    /// <param name="indexUrl">The index location that failed.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public ReleaseIndexException(string indexUrl, string message, Exception? innerException = null)
        : base($"Failed to get envtest release index from '{indexUrl}': {message}", innerException!)
    {
        IndexUrl = indexUrl;
    }

    /// <summary>Gets the index location that failed.</summary>
    public string IndexUrl { get; }
}

/// <summary>Thrown when the requested Kubernetes version does not exist in the release index.</summary>
public sealed class ReleaseVersionNotFoundException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="ReleaseVersionNotFoundException"/> class.</summary>
    /// <param name="requestedVersion">The version request that could not be satisfied (exact, series, or "latest").</param>
    /// <param name="indexUrl">The index that was searched.</param>
    public ReleaseVersionNotFoundException(string requestedVersion, string indexUrl)
        : base($"No envtest release matching '{requestedVersion}' was found in the release index at '{indexUrl}'.")
    {
        RequestedVersion = requestedVersion;
        IndexUrl = indexUrl;
    }

    /// <summary>Gets the version request that could not be satisfied.</summary>
    public string RequestedVersion { get; }

    /// <summary>Gets the index that was searched.</summary>
    public string IndexUrl { get; }
}

/// <summary>
/// Thrown when a Kubernetes version exists in the release index but publishes no
/// binaries for the requested OS/architecture combination (for example
/// <c>windows/arm64</c> before Kubernetes v1.35.0-alpha.3).
/// </summary>
public sealed class PlatformBinariesNotFoundException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="PlatformBinariesNotFoundException"/> class.</summary>
    /// <param name="version">The Kubernetes version that was requested.</param>
    /// <param name="platform">The OS/architecture combination that has no published binaries.</param>
    public PlatformBinariesNotFoundException(KubernetesVersion version, ReleasePlatform platform)
        : base($"envtest release v{version} does not publish binaries for OS '{platform.OperatingSystem}' / architecture '{platform.Architecture}'. "
               + "Pick a Kubernetes version that supports this platform (for example, windows/arm64 binaries exist only from v1.35.0-alpha.3 onwards) or another platform.")
    {
        Version = version;
        Platform = platform;
    }

    /// <summary>Gets the Kubernetes version that was requested.</summary>
    public KubernetesVersion Version { get; }

    /// <summary>Gets the OS/architecture combination that has no published binaries.</summary>
    public ReleasePlatform Platform { get; }
}

/// <summary>Thrown when downloading an envtest binaries archive fails.</summary>
public sealed class BinaryDownloadException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="BinaryDownloadException"/> class.</summary>
    /// <param name="url">The archive URL that failed.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public BinaryDownloadException(string url, string message, Exception? innerException = null)
        : base($"Failed to download envtest binaries from '{url}': {message}", innerException!)
    {
        Url = url;
    }

    /// <summary>Gets the archive URL that failed.</summary>
    public string Url { get; }
}

/// <summary>Thrown when a downloaded archive does not match its published SHA-512 checksum.</summary>
public sealed class ChecksumMismatchException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="ChecksumMismatchException"/> class.</summary>
    /// <param name="archiveName">The archive file name.</param>
    /// <param name="expectedHash">The SHA-512 hash published in the release index.</param>
    /// <param name="actualHash">The SHA-512 hash computed from the downloaded bytes.</param>
    public ChecksumMismatchException(string archiveName, string expectedHash, string actualHash)
        : base($"Checksum mismatch for {archiveName}: {actualHash} (computed) != {expectedHash} (expected).")
    {
        ArchiveName = archiveName;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }

    /// <summary>Gets the archive file name.</summary>
    public string ArchiveName { get; }

    /// <summary>Gets the SHA-512 hash published in the release index.</summary>
    public string ExpectedHash { get; }

    /// <summary>Gets the SHA-512 hash computed from the downloaded bytes.</summary>
    public string ActualHash { get; }
}
