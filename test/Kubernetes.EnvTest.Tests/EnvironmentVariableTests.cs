using Kubernetes.EnvTest.Internal;
using Kubernetes.EnvTest.Provisioning;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

// Environment variables are process-global, so every test touching them lives
// in this single collection to avoid races with parallel test classes.
[Collection("EnvironmentVariables")]
public class BinPathFinderTests
{
    [Fact]
    public void Per_binary_override_wins_over_everything()
    {
        using var etcdOverride = new TemporaryEnvironmentVariable("TEST_ASSET_ETCD", "/custom/etcd");
        using var assetsOverride = new TemporaryEnvironmentVariable("KUBEBUILDER_ASSETS", "/assets");

        Assert.Equal("/custom/etcd", BinPathFinder.Find("etcd", "/config-dir"));
    }

    [Fact]
    public void Kubebuilder_assets_wins_over_configured_directory()
    {
        using var etcdOverride = new TemporaryEnvironmentVariable("TEST_ASSET_ETCD", null);
        using var assetsOverride = new TemporaryEnvironmentVariable("KUBEBUILDER_ASSETS", "/assets");

        Assert.Equal(Path.Combine("/assets", "etcd"), BinPathFinder.Find("etcd", "/config-dir"));
    }

    [Fact]
    public void Configured_directory_wins_over_default()
    {
        using var etcdOverride = new TemporaryEnvironmentVariable("TEST_ASSET_ETCD", null);
        using var assetsOverride = new TemporaryEnvironmentVariable("KUBEBUILDER_ASSETS", null);

        Assert.Equal(Path.Combine("/config-dir", "etcd"), BinPathFinder.Find("etcd", "/config-dir"));
    }

    [Fact]
    public void Falls_back_to_the_kubebuilder_default_path()
    {
        using var etcdOverride = new TemporaryEnvironmentVariable("TEST_ASSET_ETCD", null);
        using var assetsOverride = new TemporaryEnvironmentVariable("KUBEBUILDER_ASSETS", null);

        Assert.Equal(Path.Combine("/usr/local/kubebuilder/bin", "etcd"), BinPathFinder.Find("etcd", null));
    }

    [Fact]
    public void Binary_names_are_sanitized_into_variable_names()
    {
        using var apiServerOverride = new TemporaryEnvironmentVariable("TEST_ASSET_KUBE_APISERVER", "/custom/apiserver");

        Assert.Equal("/custom/apiserver", BinPathFinder.Find("kube-apiserver", null));
    }
}

[Collection("EnvironmentVariables")]
public class BinaryStoreDefaultDirectoryTests
{
    [Fact]
    public void Linux_default_honors_xdg_data_home()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var xdgOverride = new TemporaryEnvironmentVariable("XDG_DATA_HOME", "/xdg-data");

        Assert.Equal(Path.Combine("/xdg-data", "kubebuilder-envtest", "k8s"), BinaryStore.GetDefaultDirectory());
    }

    [Fact]
    public void Linux_default_falls_back_to_local_share()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var xdgOverride = new TemporaryEnvironmentVariable("XDG_DATA_HOME", null);
        using var homeOverride = new TemporaryEnvironmentVariable("HOME", "/home/someone");

        Assert.Equal(
            Path.Combine("/home/someone", ".local", "share", "kubebuilder-envtest", "k8s"),
            BinaryStore.GetDefaultDirectory());
    }
}

internal sealed class TemporaryEnvironmentVariable : IDisposable
{
    private readonly string _name;
    private readonly string? _originalValue;

    internal TemporaryEnvironmentVariable(string name, string? value)
    {
        _name = name;
        _originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
}
