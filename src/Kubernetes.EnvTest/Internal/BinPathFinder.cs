using System.Text.RegularExpressions;

namespace Kubernetes.EnvTest.Internal;

/// <summary>
/// Locates control-plane binaries the same way upstream envtest does, honoring
/// the <c>TEST_ASSET_*</c> and <c>KUBEBUILDER_ASSETS</c> environment variables.
/// </summary>
internal static partial class BinPathFinder
{
    /// <summary>The environment variable holding the global test binary directory.</summary>
    internal const string AssetsEnvironmentVariable = "KUBEBUILDER_ASSETS";

    /// <summary>The prefix of per-binary override environment variables.</summary>
    internal const string AssetOverridePrefix = "TEST_ASSET_";

    /// <summary>The fallback directory when no override is configured.</summary>
    internal const string DefaultAssetsPath = "/usr/local/kubebuilder/bin";

    /// <summary>
    /// Finds the path of a named binary using, in order of precedence:
    /// <c>TEST_ASSET_&lt;NAME&gt;</c>, <c>KUBEBUILDER_ASSETS</c>, the provided
    /// asset directory, then <c>/usr/local/kubebuilder/bin</c>. The result is
    /// not checked for existence, matching upstream.
    /// </summary>
    internal static string Find(string symbolicName, string? assetDirectory)
    {
        string sanitized = NonAlphanumericPattern().Replace(symbolicName.ToUpperInvariant(), "_");
        sanitized = LeadingDigitsPattern().Replace(sanitized, string.Empty);

        string? overridePath = System.Environment.GetEnvironmentVariable(AssetOverridePrefix + sanitized);
        if (overridePath is not null)
        {
            return overridePath;
        }

        string? assetsDirectory = System.Environment.GetEnvironmentVariable(AssetsEnvironmentVariable);
        if (assetsDirectory is not null)
        {
            return Path.Combine(assetsDirectory, symbolicName);
        }

        if (!string.IsNullOrEmpty(assetDirectory))
        {
            return Path.Combine(assetDirectory, symbolicName);
        }

        return Path.Combine(DefaultAssetsPath, symbolicName);
    }

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NonAlphanumericPattern();

    [GeneratedRegex("^[0-9]+")]
    private static partial Regex LeadingDigitsPattern();
}
