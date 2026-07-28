using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using Kubernetes.EnvTest.Provisioning;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class BinaryProvisionerTests : IDisposable
{
    private static readonly ReleasePlatform LinuxAmd64 = new("linux", "amd64");
    private readonly string _storeRoot = Directory.CreateTempSubdirectory("envtest-provisioner-tests-").FullName;

    [Fact]
    public async Task Downloads_verifies_and_extracts_binaries()
    {
        byte[] archive = BuildArchive();
        var handler = new FakeHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://index.test/envtest-releases.yaml"] = System.Text.Encoding.UTF8.GetBytes(BuildIndexYaml(archive)),
            ["https://downloads.test/envtest-v1.31.0-linux-amd64.tar.gz"] = archive,
        });
        var provisioner = CreateProvisioner(handler, version: "1.31.0");

        EnvTestBinaries binaries = await provisioner.EnsureBinariesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(KubernetesVersion.Parse("1.31.0"), binaries.Version);
        Assert.True(File.Exists(binaries.ApiServerPath));
        Assert.True(File.Exists(binaries.EtcdPath));
        Assert.True(File.Exists(binaries.KubectlPath));
        Assert.Equal("fake-apiserver", File.ReadAllText(binaries.ApiServerPath));

        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(binaries.ApiServerPath);
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute), "extracted binaries must be executable");
        }
    }

    [Fact]
    public async Task Cached_exact_version_short_circuits_without_network()
    {
        var version = KubernetesVersion.Parse("1.31.0");
        string directory = BinaryStore.GetVersionDirectory(_storeRoot, version, LinuxAmd64);
        Directory.CreateDirectory(directory);
        foreach (string name in (string[])["kube-apiserver", "etcd", "kubectl"])
        {
            File.WriteAllText(Path.Combine(directory, name), "cached");
        }

        var handler = new FakeHttpMessageHandler([]);
        var provisioner = CreateProvisioner(handler, version: "1.31.0");

        EnvTestBinaries binaries = await provisioner.EnsureBinariesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(directory, binaries.Directory);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Checksum_mismatch_raises_a_specific_exception()
    {
        byte[] archive = BuildArchive();
        string indexYaml = BuildIndexYaml(archive).Replace(
            Convert.ToHexStringLower(SHA512.HashData(archive)),
            new string('0', 128),
            StringComparison.Ordinal);
        var handler = new FakeHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://index.test/envtest-releases.yaml"] = System.Text.Encoding.UTF8.GetBytes(indexYaml),
            ["https://downloads.test/envtest-v1.31.0-linux-amd64.tar.gz"] = archive,
        });
        var provisioner = CreateProvisioner(handler, version: "1.31.0");

        var exception = await Assert.ThrowsAsync<ChecksumMismatchException>(
            () => provisioner.EnsureBinariesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("envtest-v1.31.0-linux-amd64.tar.gz", exception.ArchiveName);
        Assert.Equal(new string('0', 128), exception.ExpectedHash);
    }

    [Fact]
    public async Task Latest_resolves_from_index_before_probing_the_store()
    {
        byte[] archive = BuildArchive();
        var handler = new FakeHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://index.test/envtest-releases.yaml"] = System.Text.Encoding.UTF8.GetBytes(BuildIndexYaml(archive)),
            ["https://downloads.test/envtest-v1.31.0-linux-amd64.tar.gz"] = archive,
        });
        var provisioner = CreateProvisioner(handler, version: null);

        EnvTestBinaries binaries = await provisioner.EnsureBinariesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(KubernetesVersion.Parse("1.31.0"), binaries.Version);
    }

    [Fact]
    public async Task Concurrent_provisioners_share_one_download()
    {
        byte[] archive = BuildArchive();
        const string archiveUrl = "https://downloads.test/envtest-v1.31.0-linux-amd64.tar.gz";
        var handler = new FakeHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://index.test/envtest-releases.yaml"] = System.Text.Encoding.UTF8.GetBytes(BuildIndexYaml(archive)),
            [archiveUrl] = archive,
        });

        // Regression: parallel test classes provisioning the same version used
        // to collide on a shared temp file and delete each other's download.
        Task<EnvTestBinaries>[] tasks = [.. Enumerable.Range(0, 4)
            .Select(_ => CreateProvisioner(handler, version: "1.31.0")
                .EnsureBinariesAsync(TestContext.Current.CancellationToken))];
        EnvTestBinaries[] results = await Task.WhenAll(tasks);

        Assert.All(results, binaries =>
        {
            Assert.True(File.Exists(binaries.ApiServerPath));
            Assert.True(File.Exists(binaries.EtcdPath));
            Assert.True(File.Exists(binaries.KubectlPath));
        });
        Assert.Equal(1, handler.CountFor(archiveUrl));
    }

    [Fact]
    public async Task Missing_platform_archive_raises_platform_exception()
    {
        byte[] archive = BuildArchive();
        var handler = new FakeHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://index.test/envtest-releases.yaml"] = System.Text.Encoding.UTF8.GetBytes(BuildIndexYaml(archive)),
        });
        var options = new BinaryProvisionerOptions
        {
            Version = "1.31.0",
            IndexUrl = "https://index.test/envtest-releases.yaml",
            StoreDirectory = _storeRoot,
            Platform = new ReleasePlatform("windows", "arm64"),
        };
        var provisioner = new BinaryProvisioner(options, httpClient: new HttpClient(handler));

        await Assert.ThrowsAsync<PlatformBinariesNotFoundException>(
            () => provisioner.EnsureBinariesAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        Directory.Delete(_storeRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private BinaryProvisioner CreateProvisioner(FakeHttpMessageHandler handler, string? version)
    {
        var options = new BinaryProvisionerOptions
        {
            Version = version,
            IndexUrl = "https://index.test/envtest-releases.yaml",
            StoreDirectory = _storeRoot,
            Platform = LinuxAmd64,
        };
        return new BinaryProvisioner(options, httpClient: new HttpClient(handler));
    }

    private static string BuildIndexYaml(byte[] archive)
    {
        string hash = Convert.ToHexStringLower(SHA512.HashData(archive));
        return $"""
            releases:
              v1.31.0:
                envtest-v1.31.0-linux-amd64.tar.gz:
                  hash: {hash}
                  selfLink: https://downloads.test/envtest-v1.31.0-linux-amd64.tar.gz
            """;
    }

    private static byte[] BuildArchive()
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            // The real archives nest binaries under controller-tools/envtest/;
            // extraction must flatten that.
            WriteEntry(tar, "controller-tools/envtest/kube-apiserver", "fake-apiserver");
            WriteEntry(tar, "controller-tools/envtest/etcd", "fake-etcd");
            WriteEntry(tar, "controller-tools/envtest/kubectl", "fake-kubectl");
        }

        return buffer.ToArray();
    }

    private static void WriteEntry(TarWriter tar, string name, string content)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                 | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                 | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
        };
        tar.WriteEntry(entry);
    }
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _responses;
    private readonly Lock _countsLock = new();
    private readonly Dictionary<string, int> _requestCounts = [];

    internal FakeHttpMessageHandler(Dictionary<string, byte[]> responses)
    {
        _responses = responses;
    }

    internal int RequestCount { get; private set; }

    internal int CountFor(string url)
    {
        lock (_countsLock)
        {
            return _requestCounts.GetValueOrDefault(url);
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        lock (_countsLock)
        {
            RequestCount++;
            _requestCounts[url] = _requestCounts.GetValueOrDefault(url) + 1;
        }

        if (_responses.TryGetValue(url, out byte[]? body))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
