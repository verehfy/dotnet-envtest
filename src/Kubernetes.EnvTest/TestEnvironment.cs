using System.Diagnostics;

using k8s;
using k8s.Autorest;
using k8s.Models;

using Kubernetes.EnvTest.Internal;
using Kubernetes.EnvTest.Provisioning;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernetes.EnvTest;

/// <summary>
/// A Kubernetes test environment that starts and stops a local control plane
/// (etcd + kube-apiserver) and installs extension APIs (CRDs and webhooks)
/// into it. The .NET port of controller-runtime's <c>envtest.Environment</c>.
/// </summary>
/// <remarks>
/// Behavior can be overridden with the same environment variables the Go
/// implementation honors: <c>USE_EXISTING_CLUSTER</c>,
/// <c>TEST_ASSET_KUBE_APISERVER</c> / <c>TEST_ASSET_ETCD</c> / <c>TEST_ASSET_KUBECTL</c>,
/// <c>KUBEBUILDER_ASSETS</c>, <c>KUBEBUILDER_CONTROLPLANE_START_TIMEOUT</c> /
/// <c>KUBEBUILDER_CONTROLPLANE_STOP_TIMEOUT</c> (Go duration syntax), and
/// <c>KUBEBUILDER_ATTACH_CONTROL_PLANE_OUTPUT</c>.
/// </remarks>
public sealed partial class TestEnvironment : IAsyncDisposable
{
    private const string UseExistingClusterVariable = "USE_EXISTING_CLUSTER";
    private const string StartTimeoutVariable = "KUBEBUILDER_CONTROLPLANE_START_TIMEOUT";
    private const string StopTimeoutVariable = "KUBEBUILDER_CONTROLPLANE_STOP_TIMEOUT";
    private const string AttachOutputVariable = "KUBEBUILDER_ATTACH_CONTROL_PLANE_OUTPUT";
    private const int ControlPlaneStartRetries = 5;

    private static readonly TimeSpan DefaultControlPlaneTimeout = TimeSpan.FromSeconds(20);

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TestEnvironment> _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Initializes a new instance of the <see cref="TestEnvironment"/> class.</summary>
    /// <param name="loggerFactory">Optional logger factory for diagnostic output; a no-op factory is used when omitted.</param>
    public TestEnvironment(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<TestEnvironment>();
    }

    /// <summary>Gets the control plane (API server and etcd) managed by this environment.</summary>
    public ControlPlane ControlPlane { get; } = new();

    /// <summary>
    /// Gets or sets the client configuration for talking to the API server.
    /// Populated by <see cref="StartAsync"/>; may be pre-set when using an
    /// existing cluster.
    /// </summary>
    public KubernetesClientConfiguration? Config { get; set; }

    /// <summary>Gets or sets kubeconfig file contents equivalent to <see cref="Config"/>. Populated by <see cref="StartAsync"/>.</summary>
    public byte[]? KubeConfig { get; set; }

    /// <summary>Gets the options for installing CRDs.</summary>
    public CrdInstallOptions CrdInstallOptions { get; } = new();

    /// <summary>Gets the options for installing webhooks.</summary>
    public WebhookInstallOptions WebhookInstallOptions { get; } = new();

    /// <summary>Gets the CRDs to install; after start, holds every installed CRD.</summary>
    public IList<V1CustomResourceDefinition> Crds { get; private set; } = [];

    /// <summary>Gets the paths of files or directories containing CRD manifests, merged into <see cref="CrdInstallOptions"/>.</summary>
    public IList<string> CrdDirectoryPaths { get; } = [];

    /// <summary>Gets or sets a value indicating whether a missing CRD path raises an error instead of being skipped.</summary>
    public bool ErrorIfCrdPathMissing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the envtest binaries are
    /// downloaded (into <see cref="BinaryAssetsDirectory"/>, or the shared
    /// setup-envtest store when unset) before starting.
    /// </summary>
    public bool DownloadBinaryAssets { get; set; }

    /// <summary>
    /// Gets or sets the Kubernetes version to download: exact (<c>1.31.0</c>),
    /// series (<c>1.31</c>), or <see langword="null"/> for the latest stable release.
    /// </summary>
    public string? DownloadBinaryAssetsVersion { get; set; }

    /// <summary>Gets or sets the release index URL used for downloads; defaults to <see cref="ReleaseIndex.DefaultIndexUrl"/>.</summary>
    public string? DownloadBinaryAssetsIndexUrl { get; set; }

    /// <summary>
    /// Gets or sets the directory containing the control-plane binaries. Can be
    /// overridden by <c>KUBEBUILDER_ASSETS</c> / <c>TEST_ASSET_*</c>. Set this
    /// to <see cref="BinaryStore.GetDefaultDirectory"/> to share binaries with
    /// setup-envtest (that is also where downloads land when
    /// <see cref="DownloadBinaryAssets"/> is enabled and this is unset).
    /// </summary>
    public string? BinaryAssetsDirectory { get; set; }

    /// <summary>
    /// Gets or sets whether an existing cluster (from the standard kubeconfig
    /// discovery) is used instead of starting a local control plane. When
    /// unset, the <c>USE_EXISTING_CLUSTER</c> environment variable decides.
    /// </summary>
    public bool? UseExistingCluster { get; set; }

    /// <summary>
    /// Gets or sets the maximum time each control-plane component may take to
    /// start. Defaults to <c>KUBEBUILDER_CONTROLPLANE_START_TIMEOUT</c> or 20 seconds.
    /// </summary>
    public TimeSpan ControlPlaneStartTimeout { get; set; }

    /// <summary>
    /// Gets or sets the maximum time each control-plane component may take to
    /// stop. Defaults to <c>KUBEBUILDER_CONTROLPLANE_STOP_TIMEOUT</c> or 20 seconds.
    /// </summary>
    public TimeSpan ControlPlaneStopTimeout { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether control-plane stdout/stderr are
    /// attached to the current console for debugging. Also enabled by the
    /// <c>KUBEBUILDER_ATTACH_CONTROL_PLANE_OUTPUT</c> environment variable.
    /// </summary>
    public bool AttachControlPlaneOutput { get; set; }

    /// <summary>Gets or sets a value indicating whether phase durations are reported in log messages.</summary>
    public bool LogTimings { get; set; } = true;

    /// <summary>
    /// Starts the control plane (or connects to an existing cluster), waits for
    /// it to be usable, and installs the configured CRDs and webhooks.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start; any partially started processes are torn down.</param>
    /// <returns>The admin client configuration for the started environment.</returns>
    public async Task<KubernetesClientConfiguration> StartAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable? environmentScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["EnvTestInstance"] = _instanceId,
        });

        if (ShouldUseExistingCluster())
        {
            LogUsingExistingCluster(_instanceId);
            Config ??= KubernetesClientConfiguration.BuildDefaultConfig();
        }
        else
        {
            ApiServer apiServer = ControlPlane.GetApiServer();
            Etcd etcd = ControlPlane.GetEtcd();

            if (string.Equals(
                    System.Environment.GetEnvironmentVariable(AttachOutputVariable),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                AttachControlPlaneOutput = true;
            }

            if (AttachControlPlaneOutput)
            {
                apiServer.Out ??= Console.Out;
                apiServer.Err ??= Console.Error;
                etcd.Out ??= Console.Out;
                etcd.Err ??= Console.Error;
            }

            await ConfigureBinaryPathsAsync(cancellationToken).ConfigureAwait(false);
            ApplyTimeoutDefaults();
            etcd.StartTimeout = ControlPlaneStartTimeout;
            etcd.StopTimeout = ControlPlaneStopTimeout;
            apiServer.StartTimeout = ControlPlaneStartTimeout;
            apiServer.StopTimeout = ControlPlaneStopTimeout;

            await StartControlPlaneWithRetriesAsync(cancellationToken).ConfigureAwait(false);

            AuthenticatedUser adminUser = await ControlPlane
                .AddUserAsync(new User("admin", ["system:masters"]), null, cancellationToken)
                .ConfigureAwait(false);
            Config = adminUser.Configuration;
        }

        if (KubeConfig is null || KubeConfig.Length == 0)
        {
            KubeConfig = KubeConfigFactory.ToKubeConfigBytes(Config);
        }

        // When etcd comes up for the first time it can take a moment for the
        // default namespace to be visible through the API server.
        await WaitForDefaultNamespaceAsync(cancellationToken).ConfigureAwait(false);

        // Certificates must exist before CRD installation so conversion
        // webhooks can be patched with the CA bundle.
        await RunPhaseAsync(
            "PreparingWebhookCertificates",
            ct => WebhookInstallOptions.PrepareWithoutInstallingAsync(ct),
            cancellationToken).ConfigureAwait(false);

        await RunPhaseAsync("InstallingCRDs", InstallCrdsAsync, cancellationToken).ConfigureAwait(false);

        await RunPhaseAsync(
            "InstallingWebhooks",
            ct => WebhookInstallOptions.InstallAsync(Config!, ct),
            cancellationToken).ConfigureAwait(false);

        return Config!;
    }

    /// <summary>
    /// Stops the environment: uninstalls CRDs when
    /// <see cref="CrdInstallOptions.CleanUpAfterUse"/> is set, removes generated
    /// webhook certificates, and tears down the control plane (unless an
    /// existing cluster is used).
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for graceful teardown.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable? environmentScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["EnvTestInstance"] = _instanceId,
        });

        long startTimestamp = Stopwatch.GetTimestamp();
        using (BeginPhaseScope("TearDown"))
        {
            if (CrdInstallOptions.CleanUpAfterUse && Config is not null)
            {
                await CrdInstaller.UninstallAsync(Config, CrdInstallOptions, cancellationToken).ConfigureAwait(false);
            }

            await WebhookInstallOptions.CleanupAsync().ConfigureAwait(false);

            if (!ShouldUseExistingCluster())
            {
                await ControlPlane.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (LogTimings)
        {
            long elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            LogPhaseCompleted("TearDown", _instanceId, elapsedMilliseconds);
        }
    }

    /// <summary>
    /// Provisions an additional user for connecting to this environment; see
    /// <see cref="ControlPlane.AddUserAsync"/>.
    /// </summary>
    /// <param name="user">The user to provision.</param>
    /// <param name="baseConfiguration">Optional base client configuration whose settings are preserved.</param>
    /// <param name="cancellationToken">Cancels the provisioning.</param>
    public Task<AuthenticatedUser> AddUserAsync(
        User user,
        KubernetesClientConfiguration? baseConfiguration = null,
        CancellationToken cancellationToken = default) =>
        ControlPlane.AddUserAsync(user, baseConfiguration, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task InstallCrdsAsync(CancellationToken cancellationToken)
    {
        foreach (string path in CrdDirectoryPaths)
        {
            if (!CrdInstallOptions.Paths.Contains(path))
            {
                CrdInstallOptions.Paths.Add(path);
            }
        }

        foreach (V1CustomResourceDefinition crd in Crds)
        {
            CrdInstallOptions.Crds.Add(crd);
        }

        CrdInstallOptions.ErrorIfPathMissing = ErrorIfCrdPathMissing;
        CrdInstallOptions.WebhookOptions = WebhookInstallOptions;

        IReadOnlyList<V1CustomResourceDefinition> installed =
            await CrdInstaller.InstallAsync(Config!, CrdInstallOptions, cancellationToken).ConfigureAwait(false);
        Crds = [.. installed];
    }

    private async Task ConfigureBinaryPathsAsync(CancellationToken cancellationToken)
    {
        ApiServer apiServer = ControlPlane.GetApiServer();
        Etcd etcd = ControlPlane.GetEtcd();

        if (DownloadBinaryAssets)
        {
            var provisioner = new BinaryProvisioner(
                new BinaryProvisionerOptions
                {
                    Version = DownloadBinaryAssetsVersion,
                    IndexUrl = DownloadBinaryAssetsIndexUrl ?? ReleaseIndex.DefaultIndexUrl,
                    StoreDirectory = BinaryAssetsDirectory,
                    LogTimings = LogTimings,
                },
                _loggerFactory.CreateLogger<BinaryProvisioner>());

            EnvTestBinaries binaries;
            long startTimestamp = Stopwatch.GetTimestamp();
            using (BeginPhaseScope("ProvisioningBinaries"))
            {
                binaries = await provisioner.EnsureBinariesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (LogTimings)
            {
                long elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                LogPhaseCompleted("ProvisioningBinaries", _instanceId, elapsedMilliseconds);
            }

            LogProvisionedBinaries(binaries.Version, binaries.Directory);
            apiServer.Path = binaries.ApiServerPath;
            etcd.Path = binaries.EtcdPath;
            ControlPlane.KubectlPath = binaries.KubectlPath;
        }
        else
        {
            apiServer.Path ??= WithExecutableExtension(BinPathFinder.Find("kube-apiserver", BinaryAssetsDirectory));
            etcd.Path ??= WithExecutableExtension(BinPathFinder.Find("etcd", BinaryAssetsDirectory));
            ControlPlane.KubectlPath ??= WithExecutableExtension(BinPathFinder.Find("kubectl", BinaryAssetsDirectory));
        }
    }

    private static string WithExecutableExtension(string path)
    {
        // The Windows archives ship kube-apiserver.exe etc.; resolve the .exe
        // variant when the bare name does not exist.
        if (OperatingSystem.IsWindows() && !File.Exists(path) && File.Exists(path + ".exe"))
        {
            return path + ".exe";
        }

        return path;
    }

    private void ApplyTimeoutDefaults()
    {
        if (ControlPlaneStartTimeout == TimeSpan.Zero)
        {
            string? configured = System.Environment.GetEnvironmentVariable(StartTimeoutVariable);
            ControlPlaneStartTimeout = string.IsNullOrEmpty(configured)
                ? DefaultControlPlaneTimeout
                : GoDuration.Parse(configured);
        }

        if (ControlPlaneStopTimeout == TimeSpan.Zero)
        {
            string? configured = System.Environment.GetEnvironmentVariable(StopTimeoutVariable);
            ControlPlaneStopTimeout = string.IsNullOrEmpty(configured)
                ? DefaultControlPlaneTimeout
                : GoDuration.Parse(configured);
        }
    }

    private async Task StartControlPlaneWithRetriesAsync(CancellationToken cancellationToken)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        using (BeginPhaseScope("StartingControlPlane"))
        {
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ControlPlane.StartAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (EnvTestException ex) when (attempt < ControlPlaneStartRetries)
                {
                    LogControlPlaneStartRetry(ex, attempt, _instanceId);
                }
                catch (EnvTestException ex)
                {
                    throw new ControlPlaneStartException(ControlPlaneStartRetries, ex);
                }
            }
        }

        if (LogTimings)
        {
            long elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            LogPhaseCompleted("StartingControlPlane", _instanceId, elapsedMilliseconds);
        }
    }

    private async Task WaitForDefaultNamespaceAsync(CancellationToken cancellationToken)
    {
        using var client = new k8s.Kubernetes(Config);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                try
                {
                    await client.CoreV1.ReadNamespaceAsync("default", cancellationToken: timeout.Token).ConfigureAwait(false);
                    return;
                }
                catch (HttpOperationException)
                {
                    // Not there yet.
                }
                catch (HttpRequestException)
                {
                    // The API server may briefly refuse connections right after start.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ControlPlaneReadinessException("The default namespace did not become available within 5 seconds.");
        }
    }

    private bool ShouldUseExistingCluster() =>
        UseExistingCluster ?? string.Equals(
            System.Environment.GetEnvironmentVariable(UseExistingClusterVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private async Task RunPhaseAsync(string phase, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        using (BeginPhaseScope(phase))
        {
            await action(cancellationToken).ConfigureAwait(false);
        }

        if (LogTimings)
        {
            long elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            LogPhaseCompleted(phase, _instanceId, elapsedMilliseconds);
        }
    }

    private IDisposable? BeginPhaseScope(string phase) =>
        _logger.BeginScope(new Dictionary<string, object?> { ["Phase"] = phase });

    [LoggerMessage(Level = LogLevel.Information, Message = "envtest {EnvTestInstance}: using an existing cluster")]
    private partial void LogUsingExistingCluster(string envTestInstance);

    [LoggerMessage(Level = LogLevel.Information, Message = "envtest {EnvTestInstance}: phase {Phase} completed in {ElapsedMilliseconds} ms")]
    private partial void LogPhaseCompleted(string phase, string envTestInstance, long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using envtest binaries for Kubernetes {KubernetesVersion} from {Directory}")]
    private partial void LogProvisionedBinaries(KubernetesVersion kubernetesVersion, string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "envtest {EnvTestInstance}: unable to start the control plane (attempt {Attempt}); retrying")]
    private partial void LogControlPlaneStartRetry(Exception exception, int attempt, string envTestInstance);
}
