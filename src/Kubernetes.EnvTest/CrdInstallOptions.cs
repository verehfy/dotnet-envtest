using k8s.Models;

namespace Kubernetes.EnvTest;

/// <summary>Options for installing CustomResourceDefinitions.</summary>
public sealed class CrdInstallOptions
{
    /// <summary>Gets the paths of files or directories containing CRD manifests (YAML or JSON).</summary>
    public IList<string> Paths { get; } = [];

    /// <summary>Gets the CRDs to install, merged with those read from <see cref="Paths"/>.</summary>
    public IList<V1CustomResourceDefinition> Crds { get; } = [];

    /// <summary>
    /// Gets the group/kinds whose CRDs should have their conversion webhook
    /// pointed at the locally served conversion endpoint. This replaces the Go
    /// library's scheme-based Hub/Spoke detection: register here the types your
    /// test serves conversion webhooks for. CRDs with a conversion webhook in
    /// their manifest that are <b>not</b> listed here get their conversion
    /// stanza removed (matching upstream's behavior for unregistered types),
    /// unless <see cref="WebhookInstallOptions.IgnoreSchemeConvertible"/> is set.
    /// </summary>
    public ISet<GroupKind> ConversionWebhookTypes { get; } = new HashSet<GroupKind>();

    /// <summary>Gets or sets a value indicating whether a missing <see cref="Paths"/> entry raises an error.</summary>
    public bool ErrorIfPathMissing { get; set; }

    /// <summary>Gets or sets the maximum time to wait for CRDs to appear in discovery; defaults to 10 seconds.</summary>
    public TimeSpan MaxWait { get; set; }

    /// <summary>Gets or sets the poll interval used while waiting; defaults to 100 milliseconds.</summary>
    public TimeSpan PollInterval { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the installed CRDs are deleted
    /// when the environment stops.
    /// </summary>
    public bool CleanUpAfterUse { get; set; }

    /// <summary>
    /// Gets or sets the webhook options carrying the conversion-webhook CA and
    /// serving address. Inherited from the environment during start; only set
    /// this when calling the installer directly.
    /// </summary>
    public WebhookInstallOptions? WebhookOptions { get; set; }
}
