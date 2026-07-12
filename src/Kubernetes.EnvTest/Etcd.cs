using Kubernetes.EnvTest.Internal;

namespace Kubernetes.EnvTest;

/// <summary>Runs an <c>etcd</c> process for the test environment.</summary>
public sealed class Etcd
{
    private ProcessState? _processState;
    private ArgumentSet? _arguments;
    private Uri? _listenPeerUri;

    /// <summary>
    /// Gets or sets the client URL etcd listens on. When unset, a free port on
    /// <c>localhost</c> is allocated during start.
    /// </summary>
    public Uri? ClientUri { get; set; }

    /// <summary>
    /// Gets or sets the path of the <c>etcd</c> binary. When unset it is
    /// resolved from <c>TEST_ASSET_ETCD</c>, <c>KUBEBUILDER_ASSETS</c>, the
    /// environment's binary assets directory, or <c>/usr/local/kubebuilder/bin</c>.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the directory etcd stores its state in. A fresh temp
    /// directory is created (and cleaned up on stop) when unset.
    /// </summary>
    public string? DataDir { get; set; }

    /// <summary>Gets or sets the maximum time etcd may take to become ready. Defaults to 20 seconds.</summary>
    public TimeSpan StartTimeout { get; set; }

    /// <summary>Gets or sets the maximum time etcd may take to stop. Defaults to 20 seconds.</summary>
    public TimeSpan StopTimeout { get; set; }

    /// <summary>Gets or sets the writer receiving etcd's stdout; discarded when null.</summary>
    public TextWriter? Out { get; set; }

    /// <summary>Gets or sets the writer receiving etcd's stderr; discarded when null.</summary>
    public TextWriter? Err { get; set; }

    /// <summary>
    /// Returns the argument set used to customize etcd's flags on top of the
    /// built-in defaults.
    /// </summary>
    public ArgumentSet Configure() => _arguments ??= new ArgumentSet();

    /// <summary>Starts etcd and waits for its <c>/health</c> endpoint.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken).ConfigureAwait(false);
        await _processState!.StartAsync(Out, Err, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops etcd, waits for termination, and cleans up the data directory if owned.</summary>
    /// <param name="cancellationToken">Cancels waiting for a graceful stop.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_processState is null)
        {
            return;
        }

        if (_processState.DirNeedsCleaning)
        {
            // Reset so a restart can allocate a fresh directory.
            DataDir = null;
        }

        await _processState.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    internal IReadOnlyList<string> GetRenderedArguments() =>
        _processState?.Args ?? throw new InvalidOperationException("etcd has not been prepared yet.");

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        Path ??= BinPathFinder.Find("etcd", null);

        _processState = new ProcessState
        {
            Path = Path,
            Dir = DataDir,
            StartTimeout = StartTimeout,
            StopTimeout = StopTimeout,
        };
        _processState.Initialize();

        if (ClientUri is null)
        {
            ListenAddress allocated = await AddressAllocator.SuggestAsync(null, cancellationToken).ConfigureAwait(false);
            ClientUri = allocated.ToUri("http");
        }

        ListenAddress peerAddress = await AddressAllocator.SuggestAsync(null, cancellationToken).ConfigureAwait(false);
        _listenPeerUri = peerAddress.ToUri("http");

        // /health is available as of etcd 3.3.0.
        _processState.HealthCheckUri = new Uri(ClientUri, "/health");

        DataDir = _processState.Dir;
        StartTimeout = _processState.StartTimeout;
        StopTimeout = _processState.StopTimeout;

        _processState.Args = Configure().AsStrings(await BuildDefaultArgumentsAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> BuildDefaultArgumentsAsync(CancellationToken cancellationToken)
    {
        string clientUrl = ClientUri!.AbsoluteUri.TrimEnd('/');
        var defaults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["listen-peer-urls"] = [_listenPeerUri!.AbsoluteUri.TrimEnd('/')],
            ["data-dir"] = [DataDir!],
            ["advertise-client-urls"] = [clientUrl],
            ["listen-client-urls"] = [clientUrl],
        };

        // Skipping fsync makes etcd dramatically faster for tests; the flag
        // exists from etcd 3.5 on, so probe for it (version-adaptive).
        if (await _processState!.CheckFlagAsync("unsafe-no-fsync", cancellationToken).ConfigureAwait(false))
        {
            defaults["unsafe-no-fsync"] = ["true"];
        }

        return defaults;
    }
}
