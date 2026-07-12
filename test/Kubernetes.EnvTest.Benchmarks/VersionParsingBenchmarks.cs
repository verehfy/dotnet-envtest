using BenchmarkDotNet.Attributes;

using Kubernetes.EnvTest.Provisioning;

namespace Kubernetes.EnvTest.Benchmarks;

/// <summary>Benchmarks for Kubernetes version parsing and comparison.</summary>
[MemoryDiagnoser]
public class VersionParsingBenchmarks
{
    private static readonly KubernetesVersion Left = KubernetesVersion.Parse("1.31.4-alpha.3");
    private static readonly KubernetesVersion Right = KubernetesVersion.Parse("1.31.4-beta.1");

    [Benchmark]
    public KubernetesVersion ParseStable() => KubernetesVersion.Parse("v1.31.4");

    [Benchmark]
    public KubernetesVersion ParsePreRelease() => KubernetesVersion.Parse("v1.35.0-alpha.3");

    [Benchmark]
    public int ComparePreReleases() => Left.CompareTo(Right);

    [Benchmark]
    public KubernetesVersionSpec ParseSpecSeries() => KubernetesVersionSpec.Parse("1.31");
}
