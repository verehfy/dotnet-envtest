using k8s;

using Kubernetes.EnvTest.Internal;

namespace Kubernetes.EnvTest;

/// <summary>
/// Client-certificate based <see cref="IAuthenticationStrategy"/> (upstream's
/// <c>CertAuthn</c>): a private CA signs per-user client certificates, and the
/// API server is pointed at the CA via <c>--client-ca-file</c>.
/// </summary>
public sealed class CertificateAuthentication : IAuthenticationStrategy, IDisposable
{
    private const string CaCertFileName = "client-cert-auth-ca.crt";
    private readonly TinyCa _certificateAuthority = new();
    private string? _certificateDirectory;

    /// <inheritdoc/>
    public void Configure(string workingDirectory, ArgumentSet arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        _certificateDirectory = workingDirectory;
        arguments.Set("client-ca-file", CaCertificatePath);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_certificateDirectory is null)
        {
            throw new InvalidOperationException("StartAsync called before Configure.");
        }

        await File.WriteAllBytesAsync(
            CaCertificatePath,
            _certificateAuthority.CaCertificate.CertificatePemBytes(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<KubernetesClientConfiguration> AddUserAsync(
        User user,
        KubernetesClientConfiguration? baseConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        using CertPair clientCertificate = _certificateAuthority.CreateClientCertificate(user);
        KubernetesClientConfiguration configuration = KubeConfigFactory.Copy(baseConfiguration);
        configuration.ClientCertificateData = Convert.ToBase64String(clientCertificate.CertificatePemBytes());
        configuration.ClientCertificateKeyData = Convert.ToBase64String(clientCertificate.PrivateKeyPemBytes());
        return Task.FromResult(configuration);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // No-op: the working directory is cleaned up by the API server.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose() => _certificateAuthority.Dispose();

    private string CaCertificatePath =>
        Path.Combine(
            _certificateDirectory ?? throw new InvalidOperationException("Configure has not been called."),
            CaCertFileName);
}
