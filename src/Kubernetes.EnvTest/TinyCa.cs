using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kubernetes.EnvTest;

/// <summary>
/// A minimal in-memory certificate authority for provisioning the serving and
/// client certificates envtest needs, mirroring upstream's <c>TinyCA</c>.
/// FOR TESTING ONLY — the generated certificates are short-lived (one week)
/// and use settings tuned for integration tests, not production.
/// </summary>
public sealed class TinyCa : IDisposable
{
    private const string OrganizationName = "envtest";
    private long _nextSerial = 1;

    /// <summary>Initializes a new instance of the <see cref="TinyCa"/> class with a fresh self-signed CA certificate.</summary>
    public TinyCa()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            new X500DistinguishedName($"O={OrganizationName}, CN=envtest-environment"),
            key,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CaCertificate = new CertPair(request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(7)));
    }

    /// <summary>Gets the CA certificate and key.</summary>
    public CertPair CaCertificate { get; }

    /// <summary>
    /// Creates a new serving certificate for HTTPS on the given host names
    /// and/or IP addresses; defaults to <c>localhost</c> when none are given.
    /// </summary>
    /// <param name="names">DNS names or IP address literals; empty entries are ignored.</param>
    /// <returns>The serving certificate pair.</returns>
    public CertPair CreateServingCertificate(params string?[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        bool any = false;
        foreach (string? name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            any = true;
            if (IPAddress.TryParse(name, out IPAddress? ip))
            {
                sanBuilder.AddIpAddress(ip);
            }
            else
            {
                sanBuilder.AddDnsName(name);
                // Mirror upstream, which also resolves DNS names to IPs so the
                // certificate stays valid for direct-IP connections.
                foreach (IPAddress resolved in ResolveBestEffort(name))
                {
                    sanBuilder.AddIpAddress(resolved);
                }
            }
        }

        if (!any)
        {
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
        }

        return CreateCertificate(
            $"O={OrganizationName}, CN=localhost",
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // id-kp-serverAuth
            sanBuilder.Build());
    }

    /// <summary>
    /// Creates a client-authentication certificate whose common name is the
    /// user name and whose organizations are the user's groups, as expected by
    /// the Kubernetes API server's client-certificate authenticator.
    /// </summary>
    /// <param name="user">The user to embed in the certificate.</param>
    /// <returns>The client certificate pair.</returns>
    public CertPair CreateClientCertificate(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        string subject = string.Join(
            ", ",
            user.Groups.Select(group => $"O={EscapeDistinguishedNameValue(group)}")
                .Append($"CN={EscapeDistinguishedNameValue(user.Name)}"));

        return CreateCertificate(
            subject,
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, // id-kp-clientAuth
            subjectAlternativeName: null);
    }

    /// <inheritdoc/>
    public void Dispose() => CaCertificate.Dispose();

    private static IPAddress[] ResolveBestEffort(string name)
    {
        try
        {
            return Dns.GetHostAddresses(name);
        }
        catch (System.Net.Sockets.SocketException)
        {
            return [];
        }
    }

    private static string EscapeDistinguishedNameValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal);

    private CertPair CreateCertificate(string subject, OidCollection extendedKeyUsages, X509Extension? subjectAlternativeName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName(subject), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature,
            critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(extendedKeyUsages, critical: false));
        if (subjectAlternativeName is not null)
        {
            request.CertificateExtensions.Add(subjectAlternativeName);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        byte[] serial = BitConverter.GetBytes(Interlocked.Increment(ref _nextSerial));
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(serial);
        }

        using ECDsa caKey = CaCertificate.Certificate.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("CA certificate lost its private key.");
        X509SignatureGenerator generator = X509SignatureGenerator.CreateForECDsa(caKey);
        using X509Certificate2 publicCertificate = request.Create(
            CaCertificate.Certificate.SubjectName,
            generator,
            now.AddMinutes(-5),
            now.AddDays(7),
            serial);

        // Reattach the private key so the pair can be exported together.
        return new CertPair(publicCertificate.CopyWithPrivateKey(key));
    }
}
