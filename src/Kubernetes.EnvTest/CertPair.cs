using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Kubernetes.EnvTest;

/// <summary>A certificate together with its private key, PEM-exportable for on-disk use.</summary>
public sealed class CertPair : IDisposable
{
    internal CertPair(X509Certificate2 certificate)
    {
        Certificate = certificate;
    }

    /// <summary>Gets the certificate (with an attached private key).</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>
    /// Returns the PEM-encoded certificate. Line endings are always LF
    /// regardless of operating system, since the Kubernetes components
    /// consuming these files expect Unix-style PEM.
    /// </summary>
    public byte[] CertificatePemBytes() => Encoding.ASCII.GetBytes(Certificate.ExportCertificatePem() + "\n");

    /// <summary>
    /// Returns the PEM-encoded PKCS#8 private key, with LF line endings (see
    /// <see cref="CertificatePemBytes"/>).
    /// </summary>
    public byte[] PrivateKeyPemBytes()
    {
        using var key = Certificate.GetECDsaPrivateKey();
        if (key is null)
        {
            throw new InvalidOperationException("The certificate has no ECDSA private key attached.");
        }

        return Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem() + "\n");
    }

    /// <inheritdoc/>
    public void Dispose() => Certificate.Dispose();
}
