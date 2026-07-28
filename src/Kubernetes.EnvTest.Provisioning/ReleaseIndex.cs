using YamlDotNet.RepresentationModel;

namespace Kubernetes.EnvTest.Provisioning;

/// <summary>
/// The parsed contents of an <c>envtest-releases.yaml</c> release index — the
/// same manifest consumed by upstream <c>setup-envtest</c>, mapping Kubernetes
/// versions to per-platform binary archives.
/// </summary>
public sealed class ReleaseIndex
{
    /// <summary>
    /// The default index published by the Kubernetes SIGs, matching upstream
    /// controller-runtime's <c>DefaultBinaryAssetsIndexURL</c>.
    /// </summary>
    public const string DefaultIndexUrl = "https://raw.githubusercontent.com/kubernetes-sigs/controller-tools/HEAD/envtest-releases.yaml";

    private readonly Dictionary<KubernetesVersion, Dictionary<string, ReleaseArchive>> _releases;

    private ReleaseIndex(Dictionary<KubernetesVersion, Dictionary<string, ReleaseArchive>> releases)
    {
        _releases = releases;
    }

    /// <summary>Gets all versions present in the index, in no particular order.</summary>
    public IReadOnlyCollection<KubernetesVersion> Versions => _releases.Keys;

    /// <summary>Parses an <c>envtest-releases.yaml</c> document.</summary>
    /// <param name="yaml">The YAML document text.</param>
    /// <returns>The parsed index.</returns>
    /// <exception cref="FormatException">The document does not have the expected shape.</exception>
    public static ReleaseIndex Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var stream = new YamlStream();
        using (var reader = new StringReader(yaml))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new FormatException("Release index is empty or not a YAML mapping.");
        }

        if (!root.Children.TryGetValue(new YamlScalarNode("releases"), out YamlNode? releasesNode)
            || releasesNode is not YamlMappingNode releasesMapping)
        {
            throw new FormatException("Release index has no 'releases' mapping.");
        }

        var releases = new Dictionary<KubernetesVersion, Dictionary<string, ReleaseArchive>>();
        foreach ((YamlNode versionNode, YamlNode archivesNode) in releasesMapping.Children)
        {
            string versionText = GetScalar(versionNode, "release version key");
            if (!KubernetesVersion.TryParse(versionText, out KubernetesVersion? version))
            {
                throw new FormatException($"Release index contains unparseable version '{versionText}'.");
            }

            if (archivesNode is not YamlMappingNode archivesMapping)
            {
                throw new FormatException($"Release '{versionText}' is not a mapping of archives.");
            }

            var archives = new Dictionary<string, ReleaseArchive>(StringComparer.Ordinal);
            foreach ((YamlNode nameNode, YamlNode archiveNode) in archivesMapping.Children)
            {
                string name = GetScalar(nameNode, "archive name key");
                if (archiveNode is not YamlMappingNode archiveMapping)
                {
                    throw new FormatException($"Archive '{name}' of release '{versionText}' is not a mapping.");
                }

                archives[name] = new ReleaseArchive(
                    name,
                    GetRequiredChild(archiveMapping, "hash", name),
                    GetRequiredChild(archiveMapping, "selfLink", name));
            }

            releases[version] = archives;
        }

        return new ReleaseIndex(releases);
    }

    /// <summary>Determines whether the index contains the given version.</summary>
    /// <param name="version">The version to look up.</param>
    public bool ContainsVersion(KubernetesVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return _releases.ContainsKey(version);
    }

    /// <summary>
    /// Gets the archive for a version/platform pair.
    /// </summary>
    /// <param name="version">The Kubernetes version.</param>
    /// <param name="platform">The target platform.</param>
    /// <param name="indexUrl">The index location, used only for error messages.</param>
    /// <returns>The matching archive.</returns>
    /// <exception cref="ReleaseVersionNotFoundException">The version is not in the index.</exception>
    /// <exception cref="PlatformBinariesNotFoundException">The version publishes no binaries for the platform.</exception>
    public ReleaseArchive GetArchive(KubernetesVersion version, ReleasePlatform platform, string indexUrl = DefaultIndexUrl)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (!_releases.TryGetValue(version, out Dictionary<string, ReleaseArchive>? archives))
        {
            throw new ReleaseVersionNotFoundException(version.ToTaggedString(), indexUrl);
        }

        string archiveName = $"envtest-{version.ToTaggedString()}-{platform.ToArchiveSuffix()}.tar.gz";
        return archives.TryGetValue(archiveName, out ReleaseArchive? archive)
            ? archive
            : throw new PlatformBinariesNotFoundException(version, platform);
    }

    /// <summary>
    /// Resolves a version spec against the index: exact specs are validated for
    /// presence; series and "latest" specs select the highest-precedence stable
    /// (non-pre-release) version.
    /// </summary>
    /// <param name="spec">The version request.</param>
    /// <param name="indexUrl">The index location, used only for error messages.</param>
    /// <returns>The resolved concrete version.</returns>
    /// <exception cref="ReleaseVersionNotFoundException">No release in the index satisfies the spec.</exception>
    public KubernetesVersion Resolve(KubernetesVersionSpec spec, string indexUrl = DefaultIndexUrl)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Exact is not null)
        {
            return _releases.ContainsKey(spec.Exact)
                ? spec.Exact
                : throw new ReleaseVersionNotFoundException(spec.Exact.ToTaggedString(), indexUrl);
        }

        KubernetesVersion? best = null;
        foreach (KubernetesVersion candidate in _releases.Keys)
        {
            if (candidate.IsPreRelease)
            {
                continue;
            }

            if (spec.SeriesMajor is not null
                && (candidate.Major != spec.SeriesMajor || candidate.Minor != spec.SeriesMinor))
            {
                continue;
            }

            if (best is null || candidate > best)
            {
                best = candidate;
            }
        }

        return best ?? throw new ReleaseVersionNotFoundException(spec.ToString(), indexUrl);
    }

    private static string GetScalar(YamlNode node, string description)
    {
        return node is YamlScalarNode { Value: { } value }
            ? value
            : throw new FormatException($"Release index: expected a scalar for {description}.");
    }

    private static string GetRequiredChild(YamlMappingNode mapping, string key, string archiveName)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node)
            ? GetScalar(node, $"'{key}' of archive '{archiveName}'")
            : throw new FormatException($"Archive '{archiveName}' in release index is missing '{key}'.");
    }
}
