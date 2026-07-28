using k8s;
using k8s.Autorest;
using k8s.Models;

using Kubernetes.EnvTest.Internal;

namespace Kubernetes.EnvTest;

/// <summary>
/// Options for installing mutating/validating admission webhooks
/// (<c>admissionregistration.k8s.io/v1</c>) whose client configs are rewritten
/// to point at a locally served host/port with a freshly generated CA.
/// </summary>
public sealed class WebhookInstallOptions
{
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets the paths of files or directories containing webhook configuration manifests.</summary>
    public IList<string> Paths { get; } = [];

    /// <summary>Gets the mutating webhook configurations to install.</summary>
    public IList<V1MutatingWebhookConfiguration> MutatingWebhooks { get; } = [];

    /// <summary>Gets the validating webhook configurations to install.</summary>
    public IList<V1ValidatingWebhookConfiguration> ValidatingWebhooks { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether CRD conversion webhooks are
    /// rewritten to the local serving host/port for every CRD, regardless of
    /// whether its <see cref="GroupKind"/> is registered in
    /// <see cref="CrdInstallOptions.ConversionWebhookTypes"/>. This mirrors
    /// upstream's <c>IgnoreSchemeConvertible</c> and is useful for testing
    /// conversion webhooks without registering types.
    /// </summary>
    public bool IgnoreSchemeConvertible { get; set; }

    /// <summary>Gets or sets a value indicating whether missing <see cref="Paths"/> entries are ignored instead of raising an error.</summary>
    public bool IgnoreErrorIfPathMissing { get; set; }

    /// <summary>Gets the host webhooks are served on locally; populated automatically.</summary>
    public string? LocalServingHost { get; internal set; }

    /// <summary>Gets the port allocated for serving webhooks locally; populated automatically.</summary>
    public int LocalServingPort { get; internal set; }

    /// <summary>Gets the directory holding the generated <c>tls.crt</c> / <c>tls.key</c> serving pair; populated automatically.</summary>
    public string? LocalServingCertDir { get; internal set; }

    /// <summary>Gets the PEM CA bundle that trusts the local serving certificate; populated automatically.</summary>
    public byte[] LocalServingCaData { get; internal set; } = [];

    /// <summary>
    /// Gets or sets a host name that resolves to this machine from wherever the
    /// API server runs, used instead of <see cref="LocalServingHost"/> in the
    /// webhook URLs (relevant with <c>UseExistingCluster</c>).
    /// </summary>
    public string? LocalServingHostExternalName { get; set; }

    /// <summary>Gets or sets the maximum time to wait for installed webhooks to be readable; defaults to 10 seconds.</summary>
    public TimeSpan MaxWait { get; set; }

    /// <summary>Gets or sets the poll interval used while waiting; defaults to 100 milliseconds.</summary>
    public TimeSpan PollInterval { get; set; }

    /// <summary>
    /// Performs the setup half of <see cref="InstallAsync"/> without touching
    /// the cluster: allocates the local serving host/port, generates the CA and
    /// serving certificates, reads webhook manifests from <see cref="Paths"/>,
    /// and rewrites the webhook client configs.
    /// </summary>
    /// <param name="cancellationToken">Cancels the preparation.</param>
    public async Task PrepareWithoutInstallingAsync(CancellationToken cancellationToken = default)
    {
        await SetupCaAsync(cancellationToken).ConfigureAwait(false);
        ParseWebhookManifests();
        await ModifyWebhookDefinitionsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Installs the configured webhooks into the cluster and waits for them to be readable.</summary>
    /// <param name="configuration">The client configuration of the target cluster.</param>
    /// <param name="cancellationToken">Cancels the installation.</param>
    /// <exception cref="WebhookInstallationException">Reading, creating, or waiting for webhook configurations failed.</exception>
    public async Task InstallAsync(KubernetesClientConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (MaxWait <= TimeSpan.Zero)
        {
            MaxWait = DefaultMaxWait;
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            PollInterval = DefaultPollInterval;
        }

        if (LocalServingCaData.Length == 0)
        {
            await PrepareWithoutInstallingAsync(cancellationToken).ConfigureAwait(false);
        }

        if (MutatingWebhooks.Count == 0 && ValidatingWebhooks.Count == 0)
        {
            return;
        }

        using var client = new k8s.Kubernetes(configuration);
        foreach (V1MutatingWebhookConfiguration hook in MutatingWebhooks)
        {
            await EnsureCreatedAsync(
                hook.Metadata.Name,
                () => client.AdmissionregistrationV1.CreateMutatingWebhookConfigurationAsync(hook, cancellationToken: cancellationToken),
                async () =>
                {
                    V1MutatingWebhookConfiguration existing = await client.AdmissionregistrationV1
                        .ReadMutatingWebhookConfigurationAsync(hook.Metadata.Name, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    hook.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
                    await client.AdmissionregistrationV1
                        .ReplaceMutatingWebhookConfigurationAsync(hook, hook.Metadata.Name, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        foreach (V1ValidatingWebhookConfiguration hook in ValidatingWebhooks)
        {
            await EnsureCreatedAsync(
                hook.Metadata.Name,
                () => client.AdmissionregistrationV1.CreateValidatingWebhookConfigurationAsync(hook, cancellationToken: cancellationToken),
                async () =>
                {
                    V1ValidatingWebhookConfiguration existing = await client.AdmissionregistrationV1
                        .ReadValidatingWebhookConfigurationAsync(hook.Metadata.Name, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    hook.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
                    await client.AdmissionregistrationV1
                        .ReplaceValidatingWebhookConfigurationAsync(hook, hook.Metadata.Name, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        await WaitForWebhooksAsync(client, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the generated local serving certificate directory.</summary>
    public Task CleanupAsync()
    {
        if (LocalServingCertDir is not null)
        {
            try
            {
                Directory.Delete(LocalServingCertDir, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone.
            }

            LocalServingCertDir = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the <c>host:port</c> webhooks are served on, allocating the port
    /// on first use.
    /// </summary>
    /// <param name="cancellationToken">Cancels the port allocation.</param>
    internal async Task<string> GenerateHostPortAsync(CancellationToken cancellationToken)
    {
        if (LocalServingPort == 0)
        {
            ListenAddress allocated = await AddressAllocator.SuggestAsync(LocalServingHost, cancellationToken)
                .ConfigureAwait(false);
            LocalServingPort = allocated.Port;
            LocalServingHost = allocated.Address;
        }

        string host = string.IsNullOrEmpty(LocalServingHostExternalName)
            ? LocalServingHost!
            : LocalServingHostExternalName;
        return new ListenAddress(host, LocalServingPort).HostPort();
    }

    private static async Task EnsureCreatedAsync(string name, Func<Task> create, Func<Task> replace)
    {
        try
        {
            await create().ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            await replace().ConfigureAwait(false);
        }
        catch (HttpOperationException ex)
        {
            throw new WebhookInstallationException($"Unable to create webhook configuration '{name}': {ex.Message}", ex);
        }
    }

    private async Task WaitForWebhooksAsync(k8s.Kubernetes client, CancellationToken cancellationToken)
    {
        var waitingForMutating = new HashSet<string>(MutatingWebhooks.Select(h => h.Metadata.Name), StringComparer.Ordinal);
        var waitingForValidating = new HashSet<string>(ValidatingWebhooks.Select(h => h.Metadata.Name), StringComparer.Ordinal);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MaxWait);
        try
        {
            while (waitingForMutating.Count > 0 || waitingForValidating.Count > 0)
            {
                foreach (string name in waitingForMutating.ToArray())
                {
                    if (await ExistsAsync(
                            () => client.AdmissionregistrationV1.ReadMutatingWebhookConfigurationAsync(name, cancellationToken: timeout.Token))
                        .ConfigureAwait(false))
                    {
                        waitingForMutating.Remove(name);
                    }
                }

                foreach (string name in waitingForValidating.ToArray())
                {
                    if (await ExistsAsync(
                            () => client.AdmissionregistrationV1.ReadValidatingWebhookConfigurationAsync(name, cancellationToken: timeout.Token))
                        .ConfigureAwait(false))
                    {
                        waitingForValidating.Remove(name);
                    }
                }

                if (waitingForMutating.Count == 0 && waitingForValidating.Count == 0)
                {
                    return;
                }

                await Task.Delay(PollInterval, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            IEnumerable<string> missing = waitingForMutating.Concat(waitingForValidating);
            throw new WebhookInstallationException(
                $"Timed out after {MaxWait.TotalSeconds:F0}s waiting for webhook configurations to be registered: {string.Join(", ", missing)}.");
        }
    }

    private static async Task<bool> ExistsAsync(Func<Task> read)
    {
        try
        {
            await read().ConfigureAwait(false);
            return true;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task ModifyWebhookDefinitionsAsync(CancellationToken cancellationToken)
    {
        string hostPort = await GenerateHostPortAsync(cancellationToken).ConfigureAwait(false);

        foreach (V1MutatingWebhookConfiguration configuration in MutatingWebhooks)
        {
            foreach (V1MutatingWebhook webhook in configuration.Webhooks ?? [])
            {
                UpdateClientConfig(webhook.ClientConfig, hostPort);
            }
        }

        foreach (V1ValidatingWebhookConfiguration configuration in ValidatingWebhooks)
        {
            foreach (V1ValidatingWebhook webhook in configuration.Webhooks ?? [])
            {
                UpdateClientConfig(webhook.ClientConfig, hostPort);
            }
        }
    }

    private void UpdateClientConfig(Admissionregistrationv1WebhookClientConfig clientConfig, string hostPort)
    {
        clientConfig.CaBundle = LocalServingCaData;
        if (clientConfig.Service?.Path is { } servicePath)
        {
            clientConfig.Url = $"https://{hostPort}/{servicePath.TrimStart('/')}";
            clientConfig.Service = null;
        }
    }

    private void ParseWebhookManifests()
    {
        foreach (string path in Paths)
        {
            bool exists = File.Exists(path) || Directory.Exists(path);
            if (!exists)
            {
                if (IgnoreErrorIfPathMissing)
                {
                    continue;
                }

                throw new WebhookInstallationException($"Webhook path '{path}' does not exist.");
            }

            foreach (string document in ManifestReader.ReadDocuments(path))
            {
                (string? apiVersion, string? kind) = ManifestReader.PeekType(document);
                const string AdmissionRegistrationV1 = "admissionregistration.k8s.io/v1";
                switch (kind)
                {
                    case "MutatingWebhookConfiguration":
                        if (apiVersion != AdmissionRegistrationV1)
                        {
                            throw new WebhookInstallationException(
                                "Only admissionregistration.k8s.io/v1 is supported for MutatingWebhookConfiguration.");
                        }

                        MutatingWebhooks.Add(KubernetesYaml.Deserialize<V1MutatingWebhookConfiguration>(document));
                        break;
                    case "ValidatingWebhookConfiguration":
                        if (apiVersion != AdmissionRegistrationV1)
                        {
                            throw new WebhookInstallationException(
                                "Only admissionregistration.k8s.io/v1 is supported for ValidatingWebhookConfiguration.");
                        }

                        ValidatingWebhooks.Add(KubernetesYaml.Deserialize<V1ValidatingWebhookConfiguration>(document));
                        break;
                    default:
                        break;
                }
            }
        }
    }

    private async Task SetupCaAsync(CancellationToken cancellationToken)
    {
        using var ca = new TinyCa();
        using CertPair servingCert = ca.CreateServingCertificate("localhost", LocalServingHost, LocalServingHostExternalName);

        LocalServingCertDir = Directory.CreateTempSubdirectory("envtest-serving-certs-").FullName;

        byte[] certificatePem = servingCert.CertificatePemBytes();
        await File.WriteAllBytesAsync(
            Path.Combine(LocalServingCertDir, "tls.crt"),
            certificatePem,
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(LocalServingCertDir, "tls.key"),
            servingCert.PrivateKeyPemBytes(),
            cancellationToken).ConfigureAwait(false);

        // Upstream uses the serving certificate itself as the CA bundle, which
        // works because it is self-contained enough for the API server's TLS
        // validation of the webhook endpoint; we keep that behavior for parity.
        LocalServingCaData = certificatePem;
    }
}
