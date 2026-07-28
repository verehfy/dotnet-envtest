using k8s;
using k8s.Autorest;
using k8s.Models;

using Kubernetes.EnvTest.Internal;

namespace Kubernetes.EnvTest;

/// <summary>
/// Installs, waits for, and uninstalls CustomResourceDefinitions on a cluster
/// (upstream envtest's <c>InstallCRDs</c> / <c>WaitForCRDs</c> / <c>UninstallCRDs</c>).
/// </summary>
public static class CrdInstaller
{
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Installs the CRDs from <see cref="CrdInstallOptions.Crds"/> and
    /// <see cref="CrdInstallOptions.Paths"/> into the cluster, patching
    /// conversion webhooks where configured, and waits until all served
    /// resources appear in discovery.
    /// </summary>
    /// <param name="configuration">The client configuration of the target cluster.</param>
    /// <param name="options">The installation options.</param>
    /// <param name="cancellationToken">Cancels the installation.</param>
    /// <returns>The full list of installed CRDs (including those read from paths).</returns>
    /// <exception cref="CrdInstallationException">Reading, creating, or waiting for the CRDs failed.</exception>
    public static async Task<IReadOnlyList<V1CustomResourceDefinition>> InstallAsync(
        KubernetesClientConfiguration configuration,
        CrdInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        ApplyDefaults(options);

        List<V1CustomResourceDefinition> crds = ReadCrdManifests(options);
        await ModifyConversionWebhooksAsync(crds, options, cancellationToken).ConfigureAwait(false);

        using (var client = new k8s.Kubernetes(configuration))
        {
            foreach (V1CustomResourceDefinition crd in crds)
            {
                await CreateOrUpdateAsync(client, crd, cancellationToken).ConfigureAwait(false);
            }
        }

        await WaitForCrdsAsync(configuration, crds, options, cancellationToken).ConfigureAwait(false);
        return crds;
    }

    /// <summary>
    /// Deletes the CRDs described by the options from the cluster; CRDs that do
    /// not exist are ignored.
    /// </summary>
    /// <param name="configuration">The client configuration of the target cluster.</param>
    /// <param name="options">The options describing which CRDs to remove.</param>
    /// <param name="cancellationToken">Cancels the uninstall.</param>
    public static async Task UninstallAsync(
        KubernetesClientConfiguration configuration,
        CrdInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        List<V1CustomResourceDefinition> crds = ReadCrdManifests(options);
        using var client = new k8s.Kubernetes(configuration);
        foreach (V1CustomResourceDefinition crd in crds)
        {
            try
            {
                await client.ApiextensionsV1
                    .DeleteCustomResourceDefinitionAsync(crd.Metadata.Name, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Success: it is already gone.
            }
        }
    }

    /// <summary>
    /// Waits until every served version of every given CRD appears in API
    /// discovery.
    /// </summary>
    /// <param name="configuration">The client configuration of the target cluster.</param>
    /// <param name="crds">The CRDs to wait for.</param>
    /// <param name="options">Options carrying <see cref="CrdInstallOptions.MaxWait"/> and <see cref="CrdInstallOptions.PollInterval"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="CrdInstallationException">The CRDs did not appear within the deadline.</exception>
    public static async Task WaitForCrdsAsync(
        KubernetesClientConfiguration configuration,
        IReadOnlyList<V1CustomResourceDefinition> crds,
        CrdInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(crds);
        ArgumentNullException.ThrowIfNull(options);

        ApplyDefaults(options);

        var waitingFor = new Dictionary<(string Group, string Version), HashSet<string>>();
        foreach (V1CustomResourceDefinition crd in crds)
        {
            foreach (V1CustomResourceDefinitionVersion version in crd.Spec.Versions.Where(v => v.Served))
            {
                (string, string) groupVersion = (crd.Spec.Group, version.Name);
                if (!waitingFor.TryGetValue(groupVersion, out HashSet<string>? resources))
                {
                    resources = new HashSet<string>(StringComparer.Ordinal);
                    waitingFor[groupVersion] = resources;
                }

                resources.Add(crd.Spec.Names.Plural);
            }
        }

        using var discovery = new DiscoveryClient(configuration);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.MaxWait);
        try
        {
            while (true)
            {
                foreach (((string group, string version), HashSet<string> resources) in waitingFor.ToArray())
                {
                    V1APIResourceList? resourceList = await discovery
                        .GetApiResourcesAsync(group, version, timeout.Token)
                        .ConfigureAwait(false);
                    if (resourceList is null)
                    {
                        continue;
                    }

                    resources.ExceptWith(resourceList.Resources.Select(r => r.Name));
                    if (resources.Count == 0)
                    {
                        waitingFor.Remove((group, version));
                    }
                }

                if (waitingFor.Count == 0)
                {
                    return;
                }

                await Task.Delay(options.PollInterval, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            IEnumerable<string> missing = waitingFor.Select(pair =>
                $"{pair.Key.Group}/{pair.Key.Version}: {string.Join(", ", pair.Value)}");
            throw new CrdInstallationException(
                $"Timed out after {options.MaxWait.TotalSeconds:F0}s waiting for CRDs to appear in discovery ({string.Join("; ", missing)}).");
        }
    }

    private static void ApplyDefaults(CrdInstallOptions options)
    {
        if (options.MaxWait <= TimeSpan.Zero)
        {
            options.MaxWait = DefaultMaxWait;
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            options.PollInterval = DefaultPollInterval;
        }
    }

    private static List<V1CustomResourceDefinition> ReadCrdManifests(CrdInstallOptions options)
    {
        // Later files win for duplicate (group, kind, name) tuples, and
        // explicitly provided CRDs win over ones read from paths.
        var byIdentity = new Dictionary<(string? ApiVersion, string? Kind, string Name), V1CustomResourceDefinition>();

        foreach (string path in options.Paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                if (options.ErrorIfPathMissing)
                {
                    throw new CrdInstallationException($"CRD path '{path}' does not exist.");
                }

                continue;
            }

            foreach (string document in ManifestReader.ReadDocuments(path))
            {
                (_, string? kind) = ManifestReader.PeekType(document);
                if (kind != "CustomResourceDefinition")
                {
                    continue;
                }

                V1CustomResourceDefinition crd;
                try
                {
                    crd = KubernetesYaml.Deserialize<V1CustomResourceDefinition>(document);
                }
                catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException)
                {
                    throw new CrdInstallationException($"Unable to parse a CustomResourceDefinition from '{path}': {ex.Message}", ex);
                }

                if (string.IsNullOrEmpty(crd.Spec?.Names?.Kind) || string.IsNullOrEmpty(crd.Spec?.Group))
                {
                    continue;
                }

                byIdentity[(crd.ApiVersion, crd.Kind, crd.Metadata.Name)] = crd;
            }
        }

        List<V1CustomResourceDefinition> result = [.. byIdentity.Values];
        foreach (V1CustomResourceDefinition crd in options.Crds)
        {
            result.RemoveAll(existing => existing.Metadata.Name == crd.Metadata.Name);
            result.Add(crd);
        }

        return result;
    }

    private static async Task ModifyConversionWebhooksAsync(
        List<V1CustomResourceDefinition> crds,
        CrdInstallOptions options,
        CancellationToken cancellationToken)
    {
        WebhookInstallOptions? webhookOptions = options.WebhookOptions;
        if (webhookOptions is null || webhookOptions.LocalServingCaData.Length == 0)
        {
            return;
        }

        string hostPort = await webhookOptions.GenerateHostPortAsync(cancellationToken).ConfigureAwait(false);
        string url = $"https://{hostPort}/convert";

        foreach (V1CustomResourceDefinition crd in crds)
        {
            if (crd.Spec.PreserveUnknownFields == true)
            {
                continue;
            }

            if (!webhookOptions.IgnoreSchemeConvertible)
            {
                // Analogous to upstream's scheme check: only types explicitly
                // registered as convertible keep (and get) a conversion
                // webhook; others have any generated conversion stanza removed
                // so the API server does not reject the manifest.
                if (!options.ConversionWebhookTypes.Contains(new GroupKind(crd.Spec.Group, crd.Spec.Names.Kind)))
                {
                    crd.Spec.Conversion = null;
                    continue;
                }
            }

            crd.Spec.Conversion = new V1CustomResourceConversion
            {
                Strategy = "Webhook",
                Webhook = new V1WebhookConversion
                {
                    ConversionReviewVersions = ["v1", "v1beta1"],
                    ClientConfig = new Apiextensionsv1WebhookClientConfig
                    {
                        Url = url,
                        CaBundle = webhookOptions.LocalServingCaData,
                    },
                },
            };
        }
    }

    private static async Task CreateOrUpdateAsync(
        k8s.Kubernetes client,
        V1CustomResourceDefinition crd,
        CancellationToken cancellationToken)
    {
        const int MaxConflictRetries = 5;
        try
        {
            await client.ApiextensionsV1.CreateCustomResourceDefinitionAsync(crd, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Exists already: fall through to update.
        }
        catch (HttpOperationException ex)
        {
            throw new CrdInstallationException($"Unable to create CRD '{crd.Metadata.Name}': {ex.Message}", ex);
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                V1CustomResourceDefinition existing = await client.ApiextensionsV1
                    .ReadCustomResourceDefinitionAsync(crd.Metadata.Name, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                crd.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
                await client.ApiextensionsV1
                    .ReplaceCustomResourceDefinitionAsync(crd, crd.Metadata.Name, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (HttpOperationException ex) when (
                ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict && attempt < MaxConflictRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpOperationException ex)
            {
                throw new CrdInstallationException($"Unable to update CRD '{crd.Metadata.Name}': {ex.Message}", ex);
            }
        }
    }
}
