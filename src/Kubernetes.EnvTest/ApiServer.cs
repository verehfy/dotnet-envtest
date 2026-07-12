using Kubernetes.EnvTest.Internal;

namespace Kubernetes.EnvTest;

/// <summary>Runs a <c>kube-apiserver</c> process for the test environment.</summary>
public sealed class ApiServer
{
    private const string SaSignerKeyFile = "sa-signer.key";
    private const string SaSignerCertFile = "sa-signer.crt";

    private ProcessState? _processState;
    private ArgumentSet? _arguments;

    /// <summary>Gets the secure serving configuration (address, CA, authentication).</summary>
    public SecureServing SecureServing { get; } = new();

    /// <summary>
    /// Gets or sets the path of the <c>kube-apiserver</c> binary. When unset it
    /// is resolved from <c>TEST_ASSET_KUBE_APISERVER</c>, <c>KUBEBUILDER_ASSETS</c>,
    /// the environment's binary assets directory, or <c>/usr/local/kubebuilder/bin</c>.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the directory holding the server's certificates. A fresh
    /// temp directory is created (and cleaned up on stop) when unset.
    /// </summary>
    public string? CertDir { get; set; }

    /// <summary>Gets or sets the URL of the etcd instance the server should use. Populated by <see cref="ControlPlane"/>.</summary>
    public Uri? EtcdUri { get; set; }

    /// <summary>Gets or sets the maximum time the server may take to become ready. Defaults to 20 seconds.</summary>
    public TimeSpan StartTimeout { get; set; }

    /// <summary>Gets or sets the maximum time the server may take to stop. Defaults to 20 seconds.</summary>
    public TimeSpan StopTimeout { get; set; }

    /// <summary>Gets or sets the writer receiving the server's stdout; discarded when null.</summary>
    public TextWriter? Out { get; set; }

    /// <summary>Gets or sets the writer receiving the server's stderr; discarded when null.</summary>
    public TextWriter? Err { get; set; }

    /// <summary>
    /// Returns the argument set used to customize the server's flags on top of
    /// the built-in defaults.
    /// </summary>
    public ArgumentSet Configure() => _arguments ??= new ArgumentSet();

    /// <summary>Starts the server and waits for its <c>/healthz</c> endpoint.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken).ConfigureAwait(false);
        await _processState!.StartAsync(Out, Err, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the server, waits for termination, and cleans up the certificate directory if owned.</summary>
    /// <param name="cancellationToken">Cancels waiting for a graceful stop.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_processState is not null)
        {
            if (_processState.DirNeedsCleaning)
            {
                // Reset so a restart can allocate a fresh directory.
                CertDir = null;
            }

            await _processState.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        if (SecureServing.Authentication is not null)
        {
            await SecureServing.Authentication.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal IReadOnlyList<string> GetRenderedArguments() =>
        _processState?.Args ?? throw new InvalidOperationException("The API server has not been prepared yet.");

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (EtcdUri is null)
        {
            throw new InvalidOperationException("Expected EtcdUri to be configured before starting the API server.");
        }

        Path ??= BinPathFinder.Find("kube-apiserver", null);

        // Unconditionally re-created so the server can be restarted.
        _processState = new ProcessState
        {
            Path = Path,
            Dir = CertDir,
            StartTimeout = StartTimeout,
            StopTimeout = StopTimeout,
        };
        _processState.Initialize();

        if (SecureServing.Address is null || SecureServing.Port == 0)
        {
            ListenAddress allocated = await AddressAllocator.SuggestAsync(SecureServing.Address, cancellationToken)
                .ConfigureAwait(false);
            SecureServing.Address = allocated.Address;
            SecureServing.Port = allocated.Port;
        }

        _processState.HealthCheckUri = SecureServing.BuildUri("/healthz");

        CertDir = _processState.Dir;
        StartTimeout = _processState.StartTimeout;
        StopTimeout = _processState.StopTimeout;

        await PopulateServingCertificatesAsync(cancellationToken).ConfigureAwait(false);

        SecureServing.Authentication ??= new CertificateAuthentication();
        SecureServing.Authentication.Configure(CertDir!, Configure());

        // Version-adaptive flags: --insecure-port must be passed as 0 up to
        // 1.23, and must not be passed at all from 1.24 on.
        if (!await _processState.CheckFlagAsync("insecure-port", cancellationToken).ConfigureAwait(false))
        {
            Configure().Disable("insecure-port");
        }

        _processState.Args = Configure().AsStrings(BuildDefaultArguments());

        await SecureServing.Authentication.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private Dictionary<string, IReadOnlyList<string>> BuildDefaultArguments()
    {
        ListenAddress listen = SecureServing.ListenAddress;
        var defaults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["service-cluster-ip-range"] = ["10.0.0.0/24"],
            ["allow-privileged"] = ["true"],
            // ServiceAccount admission stays disabled because the controller
            // that creates default service accounts is not running in envtest.
            ["disable-admission-plugins"] = ["ServiceAccount"],
            ["cert-dir"] = [CertDir!],
            ["authorization-mode"] = ["RBAC"],
            ["secure-port"] = [listen.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            ["bind-address"] = [listen.Address],
            ["service-account-issuer"] = [SecureServing.BuildUri("/").ToString()],
            ["service-account-key-file"] = [System.IO.Path.Combine(CertDir!, SaSignerCertFile)],
            ["service-account-signing-key-file"] = [System.IO.Path.Combine(CertDir!, SaSignerKeyFile)],
            ["insecure-port"] = ["0"],
        };
        if (EtcdUri is not null)
        {
            defaults["etcd-servers"] = [EtcdUri.AbsoluteUri.TrimEnd('/')];
        }

        return defaults;
    }

    private async Task PopulateServingCertificatesAsync(CancellationToken cancellationToken)
    {
        string certPath = System.IO.Path.Combine(CertDir!, "apiserver.crt");
        if (File.Exists(certPath))
        {
            return;
        }

        using var ca = new TinyCa();
        using CertPair servingCert = ca.CreateServingCertificate("localhost", SecureServing.Address);

        await File.WriteAllBytesAsync(certPath, servingCert.CertificatePemBytes(), cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(CertDir!, "apiserver.key"),
            servingCert.PrivateKeyPemBytes(),
            cancellationToken).ConfigureAwait(false);

        SecureServing.CaCertificatePem = ca.CaCertificate.CertificatePemBytes();

        // Service-account token signing files. A second CA is used purely as a
        // convenient key-pair generator, matching upstream.
        using var saCa = new TinyCa();
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(CertDir!, SaSignerCertFile),
            saCa.CaCertificate.CertificatePemBytes(),
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(CertDir!, SaSignerKeyFile),
            saCa.CaCertificate.PrivateKeyPemBytes(),
            cancellationToken).ConfigureAwait(false);
    }
}
