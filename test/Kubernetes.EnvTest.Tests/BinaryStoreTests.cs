using Kubernetes.EnvTest.Provisioning;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class BinaryStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("envtest-store-tests-").FullName;

    [Fact]
    public void GetVersionDirectory_uses_setup_envtest_layout()
    {
        var directory = BinaryStore.GetVersionDirectory(
            _root,
            KubernetesVersion.Parse("1.31.0"),
            new ReleasePlatform("linux", "amd64"));

        Assert.Equal(Path.Combine(_root, "1.31.0-linux-amd64"), directory);
    }

    [Fact]
    public void TryGetBinaries_returns_null_when_any_binary_is_missing()
    {
        var version = KubernetesVersion.Parse("1.31.0");
        var platform = new ReleasePlatform("linux", "amd64");
        string directory = BinaryStore.GetVersionDirectory(_root, version, platform);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "kube-apiserver"), "");
        File.WriteAllText(Path.Combine(directory, "etcd"), "");
        // kubectl intentionally missing.

        Assert.Null(BinaryStore.TryGetBinaries(_root, version, platform));
    }

    [Fact]
    public void TryGetBinaries_finds_complete_installations()
    {
        var version = KubernetesVersion.Parse("1.31.0");
        var platform = new ReleasePlatform("linux", "amd64");
        string directory = BinaryStore.GetVersionDirectory(_root, version, platform);
        Directory.CreateDirectory(directory);
        foreach (string name in (string[])["kube-apiserver", "etcd", "kubectl"])
        {
            File.WriteAllText(Path.Combine(directory, name), "");
        }

        var binaries = BinaryStore.TryGetBinaries(_root, version, platform);

        Assert.NotNull(binaries);
        Assert.Equal(Path.Combine(directory, "kube-apiserver"), binaries.ApiServerPath);
        Assert.Equal(Path.Combine(directory, "etcd"), binaries.EtcdPath);
        Assert.Equal(Path.Combine(directory, "kubectl"), binaries.KubectlPath);
        Assert.Equal(directory, binaries.Directory);
    }

    [Fact]
    public void TryGetBinaries_expects_exe_suffix_for_windows_platform()
    {
        var version = KubernetesVersion.Parse("1.35.0");
        var platform = new ReleasePlatform("windows", "arm64");
        string directory = BinaryStore.GetVersionDirectory(_root, version, platform);
        Directory.CreateDirectory(directory);
        foreach (string name in (string[])["kube-apiserver.exe", "etcd.exe", "kubectl.exe"])
        {
            File.WriteAllText(Path.Combine(directory, name), "");
        }

        var binaries = BinaryStore.TryGetBinaries(_root, version, platform);

        Assert.NotNull(binaries);
        Assert.EndsWith("kube-apiserver.exe", binaries.ApiServerPath, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
