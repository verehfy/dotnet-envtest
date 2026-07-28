using System.Text;

using k8s;
using k8s.KubeConfigModels;

using Kubernetes.EnvTest.Internal;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class KubeConfigFactoryTests
{
    [Fact]
    public void Generated_kubeconfig_round_trips_through_the_official_parser()
    {
        using var ca = new TinyCa();
        using var client = ca.CreateClientCertificate(new User("admin", ["system:masters"]));

        var configuration = new KubernetesClientConfiguration
        {
            Host = "https://127.0.0.1:6443",
            ClientCertificateData = Convert.ToBase64String(client.CertificatePemBytes()),
            ClientCertificateKeyData = Convert.ToBase64String(client.PrivateKeyPemBytes()),
            SslCaCerts = [],
        };
        configuration.SslCaCerts.Add(ca.CaCertificate.Certificate);

        byte[] kubeConfigBytes = KubeConfigFactory.ToKubeConfigBytes(configuration);
        K8SConfiguration parsed = KubernetesYaml.Deserialize<K8SConfiguration>(Encoding.UTF8.GetString(kubeConfigBytes));

        Assert.Equal("envtest", parsed.CurrentContext);
        Cluster cluster = Assert.Single(parsed.Clusters);
        Assert.Equal("https://127.0.0.1:6443", cluster.ClusterEndpoint.Server);
        Assert.False(string.IsNullOrEmpty(cluster.ClusterEndpoint.CertificateAuthorityData));
        var user = Assert.Single(parsed.Users);
        Assert.Equal(configuration.ClientCertificateData, user.UserCredentials.ClientCertificateData);
    }

    [Fact]
    public void Generated_kubeconfig_uses_lf_line_endings()
    {
        var configuration = new KubernetesClientConfiguration { Host = "https://127.0.0.1:6443" };

        string text = Encoding.UTF8.GetString(KubeConfigFactory.ToKubeConfigBytes(configuration));

        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Copy_preserves_authentication_fields()
    {
        var source = new KubernetesClientConfiguration
        {
            Host = "https://example.test",
            AccessToken = "token",
            Username = "user",
            Password = "secret",
            SkipTlsVerify = true,
        };

        KubernetesClientConfiguration copy = KubeConfigFactory.Copy(source);

        Assert.Equal(source.Host, copy.Host);
        Assert.Equal(source.AccessToken, copy.AccessToken);
        Assert.Equal(source.Username, copy.Username);
        Assert.Equal(source.Password, copy.Password);
        Assert.True(copy.SkipTlsVerify);
    }
}
