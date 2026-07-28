namespace Kubernetes.EnvTest.Provisioning;

/// <summary>A single downloadable envtest binaries archive published in the release index.</summary>
/// <param name="Name">The archive file name, for example <c>envtest-v1.31.0-linux-amd64.tar.gz</c>.</param>
/// <param name="Sha512Hash">The lowercase hex SHA-512 hash of the archive contents.</param>
/// <param name="DownloadUrl">The absolute download URL (the index's <c>selfLink</c>).</param>
public sealed record ReleaseArchive(string Name, string Sha512Hash, string DownloadUrl);
