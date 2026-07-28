using System.Security.Cryptography.X509Certificates;

using k8s;
using k8s.Models;

namespace Kubernetes.EnvTest.Internal;

/// <summary>
/// A minimal Kubernetes discovery client (GET <c>/apis/&lt;group&gt;/&lt;version&gt;</c>)
/// used to wait for CRD-served resources to appear, since the generated client
/// only exposes discovery for built-in group-versions.
/// </summary>
internal sealed class DiscoveryClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    internal DiscoveryClient(KubernetesClientConfiguration configuration)
    {
        var handler = new SocketsHttpHandler();

        if (!string.IsNullOrEmpty(configuration.ClientCertificateData) && !string.IsNullOrEmpty(configuration.ClientCertificateKeyData))
        {
            char[] certPem = System.Text.Encoding.ASCII.GetChars(Convert.FromBase64String(configuration.ClientCertificateData));
            char[] keyPem = System.Text.Encoding.ASCII.GetChars(Convert.FromBase64String(configuration.ClientCertificateKeyData));
            var clientCertificate = X509Certificate2.CreateFromPem(certPem, keyPem);
            handler.SslOptions.ClientCertificates = new X509Certificate2Collection(clientCertificate);
        }

        // The control plane serves with a per-run self-signed CA that the
        // caller's configuration already trusts for the typed client; this
        // poll-only helper skips verification like upstream's health checks.
#pragma warning disable CA5359 // Intentional for local-only discovery polling.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359

        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _baseUri = new Uri(configuration.Host.EndsWith('/') ? configuration.Host : configuration.Host + "/");
        if (configuration.AccessToken is { Length: > 0 } token)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }
    }

    /// <summary>
    /// Returns the resources served under a group/version, or
    /// <see langword="null"/> when the group-version is not (yet) served.
    /// </summary>
    internal async Task<V1APIResourceList?> GetApiResourcesAsync(string group, string version, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(_baseUri, $"apis/{group}/{version}");
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return KubernetesJson.Deserialize<V1APIResourceList>(json);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
