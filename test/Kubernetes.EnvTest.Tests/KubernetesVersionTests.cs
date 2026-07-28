using Kubernetes.EnvTest.Provisioning;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class KubernetesVersionTests
{
    [Theory]
    [InlineData("1.31.0", 1, 31, 0, null)]
    [InlineData("v1.31.0", 1, 31, 0, null)]
    [InlineData("v1.35.0-alpha.3", 1, 35, 0, "alpha.3")]
    [InlineData("1.28.4-rc.1", 1, 28, 4, "rc.1")]
    [InlineData("0.0.1", 0, 0, 1, null)]
    public void Parse_accepts_valid_versions(string input, int major, int minor, int patch, string? preRelease)
    {
        var version = KubernetesVersion.Parse(input);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(preRelease, version.PreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.31")]
    [InlineData("1.31.x")]
    [InlineData("1.31.0.4")]
    [InlineData("1.031.0")]
    [InlineData("1.31.0-")]
    [InlineData("1.31.0-alpha..3")]
    [InlineData("not-a-version")]
    public void TryParse_rejects_invalid_versions(string input)
    {
        Assert.False(KubernetesVersion.TryParse(input, out _));
        Assert.Throws<FormatException>(() => KubernetesVersion.Parse(input));
    }

    [Theory]
    [InlineData("1.30.0", "1.31.0")]
    [InlineData("1.31.0", "1.31.1")]
    [InlineData("1.31.9", "2.0.0")]
    [InlineData("1.31.0-alpha.1", "1.31.0")]
    [InlineData("1.31.0-alpha.1", "1.31.0-alpha.2")]
    [InlineData("1.31.0-alpha.2", "1.31.0-beta.1")]
    [InlineData("1.31.0-alpha", "1.31.0-alpha.1")]
    [InlineData("1.31.0-1", "1.31.0-alpha")]
    public void CompareTo_orders_by_semver_precedence(string lower, string higher)
    {
        var lowerVersion = KubernetesVersion.Parse(lower);
        var higherVersion = KubernetesVersion.Parse(higher);

        Assert.True(lowerVersion < higherVersion);
        Assert.True(higherVersion > lowerVersion);
    }

    [Fact]
    public void Equality_ignores_v_prefix_formatting()
    {
        Assert.Equal(KubernetesVersion.Parse("v1.31.0"), KubernetesVersion.Parse("1.31.0"));
        Assert.NotEqual(KubernetesVersion.Parse("1.31.0"), KubernetesVersion.Parse("1.31.0-alpha.1"));
    }

    [Fact]
    public void ToString_and_ToTaggedString_format_consistently()
    {
        var version = KubernetesVersion.Parse("v1.35.0-alpha.3");

        Assert.Equal("1.35.0-alpha.3", version.ToString());
        Assert.Equal("v1.35.0-alpha.3", version.ToTaggedString());
    }
}
