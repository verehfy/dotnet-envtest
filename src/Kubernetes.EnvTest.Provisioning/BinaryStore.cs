namespace Kubernetes.EnvTest.Provisioning;

/// <summary>
/// The on-disk layout of provisioned envtest binaries, byte-for-byte compatible
/// with the store used by upstream <c>setup-envtest</c> so that caches populated
/// by either tool are shared.
/// </summary>
/// <remarks>
/// The layout is <c>&lt;root&gt;/&lt;version&gt;-&lt;os&gt;-&lt;arch&gt;/&lt;binary&gt;</c>, where
/// <c>&lt;root&gt;</c> defaults to (matching upstream
/// <c>SetupEnvtestDefaultBinaryAssetsDirectory</c>):
/// <list type="bullet">
///   <item><description>Windows: <c>%LocalAppData%\kubebuilder-envtest\k8s</c></description></item>
///   <item><description>macOS: <c>~/Library/Application Support/io.kubebuilder.envtest/k8s</c></description></item>
///   <item><description>Other: <c>${XDG_DATA_HOME:-~/.local/share}/kubebuilder-envtest/k8s</c></description></item>
/// </list>
/// </remarks>
public static class BinaryStore
{
    /// <summary>
    /// Returns the default store root shared with <c>setup-envtest</c> for the
    /// current operating system.
    /// </summary>
    /// <returns>The default binary store directory (its existence is not guaranteed).</returns>
    /// <exception cref="EnvTestException">The per-user base directory cannot be determined.</exception>
    public static string GetDefaultDirectory()
    {
        if (System.OperatingSystem.IsWindows())
        {
            string localAppData = System.Environment.GetEnvironmentVariable("LocalAppData")
                ?? throw new EnvTestException("%LocalAppData% is not defined.");
            return Path.Combine(localAppData, "kubebuilder-envtest", "k8s");
        }

        if (System.OperatingSystem.IsMacOS())
        {
            string home = System.Environment.GetEnvironmentVariable("HOME")
                ?? throw new EnvTestException("$HOME is not defined.");
            return Path.Combine(home, "Library", "Application Support", "io.kubebuilder.envtest", "k8s");
        }

        string? xdgDataHome = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "kubebuilder-envtest", "k8s");
        }

        string homeDir = System.Environment.GetEnvironmentVariable("HOME")
            ?? throw new EnvTestException("Neither $XDG_DATA_HOME nor $HOME are defined.");
        return Path.Combine(homeDir, ".local", "share", "kubebuilder-envtest", "k8s");
    }

    /// <summary>
    /// Returns the directory holding the binaries of one version/platform pair
    /// inside a store root, for example <c>&lt;root&gt;/1.31.0-linux-amd64</c>.
    /// </summary>
    /// <param name="storeRoot">The store root directory.</param>
    /// <param name="version">The Kubernetes version.</param>
    /// <param name="platform">The target platform.</param>
    public static string GetVersionDirectory(string storeRoot, KubernetesVersion version, ReleasePlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
        ArgumentNullException.ThrowIfNull(version);
        return Path.Combine(storeRoot, $"{version}-{platform.ToArchiveSuffix()}");
    }

    /// <summary>
    /// Probes a version directory for the three control-plane binaries.
    /// </summary>
    /// <param name="storeRoot">The store root directory.</param>
    /// <param name="version">The Kubernetes version.</param>
    /// <param name="platform">The target platform.</param>
    /// <returns>The binaries when all three are present, otherwise <see langword="null"/>.</returns>
    public static EnvTestBinaries? TryGetBinaries(string storeRoot, KubernetesVersion version, ReleasePlatform platform)
    {
        string directory = GetVersionDirectory(storeRoot, version, platform);
        EnvTestBinaries binaries = DescribeBinaries(directory, version, platform);

        return File.Exists(binaries.ApiServerPath) && File.Exists(binaries.EtcdPath) && File.Exists(binaries.KubectlPath)
            ? binaries
            : null;
    }

    /// <summary>
    /// Returns the expected binary paths of a version directory without
    /// checking for their existence.
    /// </summary>
    /// <param name="directory">The version directory.</param>
    /// <param name="version">The Kubernetes version.</param>
    /// <param name="platform">The target platform.</param>
    public static EnvTestBinaries DescribeBinaries(string directory, KubernetesVersion version, ReleasePlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(version);

        return new EnvTestBinaries(
            version,
            platform,
            directory,
            Path.Combine(directory, platform.GetBinaryFileName("kube-apiserver")),
            Path.Combine(directory, platform.GetBinaryFileName("etcd")),
            Path.Combine(directory, platform.GetBinaryFileName("kubectl")));
    }
}
