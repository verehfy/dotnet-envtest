using Kubernetes.EnvTest.Provisioning;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class KubernetesVersionSpecTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("latest")]
    [InlineData("Latest")]
    public void Parse_maps_empty_and_latest_to_latest_stable(string? input)
    {
        var spec = KubernetesVersionSpec.Parse(input);

        Assert.Null(spec.Exact);
        Assert.Null(spec.SeriesMajor);
        Assert.True(spec.RequiresIndex);
    }

    [Theory]
    [InlineData("1.31", 1, 31)]
    [InlineData("v1.31", 1, 31)]
    public void Parse_maps_two_component_versions_to_series(string input, int major, int minor)
    {
        var spec = KubernetesVersionSpec.Parse(input);

        Assert.Null(spec.Exact);
        Assert.Equal(major, spec.SeriesMajor);
        Assert.Equal(minor, spec.SeriesMinor);
    }

    [Fact]
    public void Parse_maps_full_versions_to_exact()
    {
        var spec = KubernetesVersionSpec.Parse("v1.31.2");

        Assert.Equal(KubernetesVersion.Parse("1.31.2"), spec.Exact);
        Assert.False(spec.RequiresIndex);
    }

    [Theory]
    [InlineData("1.")]
    [InlineData(".31")]
    [InlineData("one.two")]
    [InlineData("1.31.x")]
    public void Parse_rejects_garbage(string input)
    {
        Assert.Throws<FormatException>(() => KubernetesVersionSpec.Parse(input));
    }
}
