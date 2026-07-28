using Kubernetes.EnvTest.Provisioning;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class ReleaseIndexTests
{
    private const string SampleIndex = """
        releases:
          v1.32.0:
            envtest-v1.32.0-darwin-amd64.tar.gz:
              hash: aaa111
              selfLink: https://example.test/envtest-v1.32.0-darwin-amd64.tar.gz
            envtest-v1.32.0-linux-amd64.tar.gz:
              hash: bbb222
              selfLink: https://example.test/envtest-v1.32.0-linux-amd64.tar.gz
          v1.31.4:
            envtest-v1.31.4-linux-amd64.tar.gz:
              hash: ccc333
              selfLink: https://example.test/envtest-v1.31.4-linux-amd64.tar.gz
          v1.31.1:
            envtest-v1.31.1-linux-amd64.tar.gz:
              hash: ddd444
              selfLink: https://example.test/envtest-v1.31.1-linux-amd64.tar.gz
          v1.33.0-alpha.2:
            envtest-v1.33.0-alpha.2-linux-amd64.tar.gz:
              hash: eee555
              selfLink: https://example.test/envtest-v1.33.0-alpha.2-linux-amd64.tar.gz
        """;

    private static readonly ReleasePlatform LinuxAmd64 = new("linux", "amd64");

    [Fact]
    public void Parse_reads_all_versions()
    {
        var index = ReleaseIndex.Parse(SampleIndex);

        Assert.Equal(4, index.Versions.Count);
        Assert.True(index.ContainsVersion(KubernetesVersion.Parse("1.31.4")));
    }

    [Fact]
    public void Resolve_latest_stable_skips_prereleases()
    {
        var index = ReleaseIndex.Parse(SampleIndex);

        var resolved = index.Resolve(KubernetesVersionSpec.LatestStable);

        Assert.Equal(KubernetesVersion.Parse("1.32.0"), resolved);
    }

    [Fact]
    public void Resolve_series_picks_highest_patch_in_series()
    {
        var index = ReleaseIndex.Parse(SampleIndex);

        var resolved = index.Resolve(KubernetesVersionSpec.ForSeries(1, 31));

        Assert.Equal(KubernetesVersion.Parse("1.31.4"), resolved);
    }

    [Fact]
    public void Resolve_missing_series_throws_with_request_and_index()
    {
        var index = ReleaseIndex.Parse(SampleIndex);

        var exception = Assert.Throws<ReleaseVersionNotFoundException>(
            () => index.Resolve(KubernetesVersionSpec.ForSeries(1, 12), "https://example.test/index.yaml"));

        Assert.Equal("1.12", exception.RequestedVersion);
        Assert.Equal("https://example.test/index.yaml", exception.IndexUrl);
    }

    [Fact]
    public void GetArchive_returns_matching_platform_archive()
    {
        var index = ReleaseIndex.Parse(SampleIndex);

        var archive = index.GetArchive(KubernetesVersion.Parse("1.32.0"), LinuxAmd64);

        Assert.Equal("envtest-v1.32.0-linux-amd64.tar.gz", archive.Name);
        Assert.Equal("bbb222", archive.Sha512Hash);
        Assert.Equal("https://example.test/envtest-v1.32.0-linux-amd64.tar.gz", archive.DownloadUrl);
    }

    [Fact]
    public void GetArchive_for_unpublished_platform_names_version_os_and_arch()
    {
        var index = ReleaseIndex.Parse(SampleIndex);
        var windowsArm64 = new ReleasePlatform("windows", "arm64");

        var exception = Assert.Throws<PlatformBinariesNotFoundException>(
            () => index.GetArchive(KubernetesVersion.Parse("1.31.4"), windowsArm64));

        Assert.Equal(KubernetesVersion.Parse("1.31.4"), exception.Version);
        Assert.Equal("windows", exception.Platform.OperatingSystem);
        Assert.Equal("arm64", exception.Platform.Architecture);
        Assert.Contains("1.31.4", exception.Message, StringComparison.Ordinal);
        Assert.Contains("windows", exception.Message, StringComparison.Ordinal);
        Assert.Contains("arm64", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetArchive_for_unknown_version_throws_version_not_found()
    {
        var index = ReleaseIndex.Parse(SampleIndex);

        Assert.Throws<ReleaseVersionNotFoundException>(
            () => index.GetArchive(KubernetesVersion.Parse("1.12.0"), LinuxAmd64));
    }

    [Theory]
    [InlineData("")]
    [InlineData("releases: 42")]
    [InlineData("not-releases: {}")]
    public void Parse_rejects_malformed_documents(string yaml)
    {
        Assert.Throws<FormatException>(() => ReleaseIndex.Parse(yaml));
    }
}
