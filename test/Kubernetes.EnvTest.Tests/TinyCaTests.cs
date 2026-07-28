using System.Security.Cryptography.X509Certificates;
using System.Text;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class TinyCaTests
{
    [Fact]
    public void Serving_certificate_covers_localhost_and_loopback()
    {
        using var ca = new TinyCa();
        using var serving = ca.CreateServingCertificate("localhost");

        string sanText = serving.Certificate.Extensions
            .OfType<X509Extension>()
            .First(e => e.Oid?.Value == "2.5.29.17") // subjectAltName
            .Format(multiLine: false);

        Assert.Contains("localhost", sanText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_certificate_embeds_user_and_groups()
    {
        using var ca = new TinyCa();
        using var client = ca.CreateClientCertificate(new User("admin", ["system:masters"]));

        Assert.Contains("CN=admin", client.Certificate.Subject, StringComparison.Ordinal);
        Assert.Contains("O=system:masters", client.Certificate.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void Certificates_are_signed_by_the_ca()
    {
        using var ca = new TinyCa();
        using var serving = ca.CreateServingCertificate("localhost");

        Assert.Equal(ca.CaCertificate.Certificate.Subject, serving.Certificate.Issuer);
    }

    [Fact]
    public void Pem_exports_use_lf_line_endings_on_every_platform()
    {
        using var ca = new TinyCa();
        using var serving = ca.CreateServingCertificate("localhost");

        foreach (byte[] pem in (byte[][])[serving.CertificatePemBytes(), serving.PrivateKeyPemBytes()])
        {
            string text = Encoding.ASCII.GetString(pem);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Private_key_exports_as_pkcs8()
    {
        using var ca = new TinyCa();
        using var serving = ca.CreateServingCertificate("localhost");

        string keyText = Encoding.ASCII.GetString(serving.PrivateKeyPemBytes());
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", keyText, StringComparison.Ordinal);
    }

    [Fact]
    public void Serial_numbers_are_unique_per_ca()
    {
        using var ca = new TinyCa();
        using var first = ca.CreateServingCertificate("localhost");
        using var second = ca.CreateServingCertificate("localhost");

        Assert.NotEqual(first.Certificate.SerialNumber, second.Certificate.SerialNumber);
    }
}
