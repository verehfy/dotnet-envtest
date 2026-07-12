namespace Kubernetes.EnvTest;

/// <summary>How the API server serves on its secure port.</summary>
public sealed class SecureServing
{
    /// <summary>
    /// Gets or sets the listen address. When unset, a free port on
    /// <c>localhost</c> is allocated during start.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>Gets or sets the secure port; <c>0</c> allocates a free port during start.</summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets the PEM bytes of the CA that signed the API server's serving
    /// certificate. Populated during start; read-only.
    /// </summary>
    public byte[] CaCertificatePem { get; internal set; } = [];

    /// <summary>
    /// Gets or sets the strategy used to provision users. Defaults to
    /// <see cref="CertificateAuthentication"/> when unset at start.
    /// </summary>
    public IAuthenticationStrategy? Authentication { get; set; }

    /// <summary>Gets the resolved listen address once the server has been prepared.</summary>
    /// <exception cref="InvalidOperationException">The address has not been allocated yet.</exception>
    public ListenAddress ListenAddress =>
        Address is not null && Port != 0
            ? new ListenAddress(Address, Port)
            : throw new InvalidOperationException("The secure serving address is not populated until the API server is started.");

    /// <summary>Builds a URI against the secure endpoint.</summary>
    /// <param name="path">The absolute path, for example <c>/healthz</c>.</param>
    public Uri BuildUri(string path = "/") => ListenAddress.ToUri("https", path);
}
