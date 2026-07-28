using System.Security.Cryptography.X509Certificates;
using System.Text;

using k8s;
using k8s.KubeConfigModels;

using KubeConfigContext = k8s.KubeConfigModels.Context;
using KubeConfigUser = k8s.KubeConfigModels.User;

namespace Kubernetes.EnvTest.Internal;

/// <summary>
/// Builds kubeconfig documents and copies of client configurations for the
/// configurations envtest generates (upstream's <c>KubeConfigFromREST</c>).
/// </summary>
internal static class KubeConfigFactory
{
    private const string EnvTestName = "envtest";

    /// <summary>
    /// Serializes a kubeconfig equivalent to the given client configuration.
    /// Covers the auth strategies envtest produces (client certificates,
    /// bearer tokens, and basic auth) — not exec plugins.
    /// </summary>
    internal static byte[] ToKubeConfigBytes(KubernetesClientConfiguration configuration)
    {
        string? caData = null;
        if (configuration.SslCaCerts is { Count: > 0 } caCerts)
        {
            caData = Convert.ToBase64String(Encoding.ASCII.GetBytes(caCerts[0].ExportCertificatePem() + "\n"));
        }

        var kubeConfig = new K8SConfiguration
        {
            ApiVersion = "v1",
            Kind = "Config",
            CurrentContext = EnvTestName,
            Clusters =
            [
                new Cluster
                {
                    Name = EnvTestName,
                    ClusterEndpoint = new ClusterEndpoint
                    {
                        Server = configuration.Host,
                        CertificateAuthorityData = caData,
                        SkipTlsVerify = configuration.SkipTlsVerify,
                    },
                },
            ],
            Users =
            [
                new KubeConfigUser
                {
                    Name = EnvTestName,
                    UserCredentials = new UserCredentials
                    {
                        ClientCertificateData = configuration.ClientCertificateData,
                        ClientKeyData = configuration.ClientCertificateKeyData,
                        Token = configuration.AccessToken,
                        UserName = configuration.Username,
                        Password = configuration.Password,
                    },
                },
            ],
            Contexts =
            [
                new KubeConfigContext
                {
                    Name = EnvTestName,
                    ContextDetails = new ContextDetails
                    {
                        Cluster = EnvTestName,
                        User = EnvTestName,
                    },
                },
            ],
        };

        // LF newlines regardless of platform: kubeconfigs are consumed by
        // kubectl and client libraries on all OSes and CRLF trips some parsers.
        string yaml = KubernetesYaml.Serialize(kubeConfig).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(yaml);
    }

    /// <summary>Copies the connection-relevant fields of a client configuration.</summary>
    internal static KubernetesClientConfiguration Copy(KubernetesClientConfiguration? source)
    {
        var copy = new KubernetesClientConfiguration();
        if (source is null)
        {
            return copy;
        }

        copy.Host = source.Host;
        copy.Namespace = source.Namespace;
        copy.SkipTlsVerify = source.SkipTlsVerify;
        copy.SslCaCerts = source.SslCaCerts is null ? null : new X509Certificate2Collection(source.SslCaCerts);
        copy.ClientCertificateData = source.ClientCertificateData;
        copy.ClientCertificateKeyData = source.ClientCertificateKeyData;
        copy.ClientCertificateFilePath = source.ClientCertificateFilePath;
        copy.ClientKeyFilePath = source.ClientKeyFilePath;
        copy.AccessToken = source.AccessToken;
        copy.Username = source.Username;
        copy.Password = source.Password;
        copy.HttpClientTimeout = source.HttpClientTimeout;
        copy.DisableHttp2 = source.DisableHttp2;
        return copy;
    }
}
