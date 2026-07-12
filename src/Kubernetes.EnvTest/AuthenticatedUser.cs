using System.Security.Cryptography.X509Certificates;

using k8s;

using Kubernetes.EnvTest.Internal;

namespace Kubernetes.EnvTest;

/// <summary>
/// Access information for a provisioned user: client configuration, kubeconfig
/// bytes, and a preconfigured <see cref="Kubectl"/> wrapper. Connection details
/// are only valid after the API server has been started.
/// </summary>
public sealed class AuthenticatedUser
{
    private readonly KubernetesClientConfiguration _configuration;
    private readonly ControlPlane _controlPlane;
    private bool _configurationIsComplete;
    private Kubectl? _kubectl;

    internal AuthenticatedUser(KubernetesClientConfiguration configuration, ControlPlane controlPlane)
    {
        _configuration = configuration;
        _controlPlane = controlPlane;
    }

    /// <summary>
    /// Gets the client configuration for connecting to the API server as this
    /// user, completing the host and CA data on first access.
    /// </summary>
    /// <exception cref="InvalidOperationException">The API server has not been started yet.</exception>
    public KubernetesClientConfiguration Configuration
    {
        get
        {
            if (_configurationIsComplete)
            {
                return _configuration;
            }

            SecureServing serving = _controlPlane.GetApiServer().SecureServing;
            if (serving.CaCertificatePem.Length == 0)
            {
                throw new InvalidOperationException(
                    "The API server has not been started yet; start it before accessing connection details.");
            }

            _configuration.Host = serving.BuildUri("/").AbsoluteUri.TrimEnd('/');
            _configuration.SslCaCerts = new X509Certificate2Collection(
                X509CertificateLoader.LoadCertificate(serving.CaCertificatePem));
            _configurationIsComplete = true;
            return _configuration;
        }
    }

    /// <summary>Creates a Kubernetes API client authenticated as this user.</summary>
    public k8s.Kubernetes CreateKubernetesClient() => new(Configuration);

    /// <summary>Returns kubeconfig file contents equivalent to <see cref="Configuration"/>.</summary>
    public byte[] GetKubeConfig() => KubeConfigFactory.ToKubeConfigBytes(Configuration);

    /// <summary>
    /// Returns a <see cref="Kubectl"/> wrapper for talking to the API server as
    /// this user, backed by a kubeconfig written next to the server's
    /// certificates (cleaned up with them).
    /// </summary>
    /// <param name="cancellationToken">Cancels writing the kubeconfig.</param>
    public async Task<Kubectl> GetKubectlAsync(CancellationToken cancellationToken = default)
    {
        if (_kubectl is not null)
        {
            return _kubectl;
        }

        string? certDir = _controlPlane.GetApiServer().CertDir;
        if (string.IsNullOrEmpty(certDir))
        {
            throw new InvalidOperationException(
                "The API server has not been started yet; start it before accessing connection details.");
        }

        string kubeConfigPath = Path.Combine(certDir, $"{Guid.NewGuid():N}.kubecfg");
        await File.WriteAllBytesAsync(kubeConfigPath, GetKubeConfig(), cancellationToken).ConfigureAwait(false);

        var kubectl = new Kubectl { Path = _controlPlane.KubectlPath };
        kubectl.Options.Add($"--kubeconfig={kubeConfigPath}");
        _kubectl = kubectl;
        return kubectl;
    }
}
