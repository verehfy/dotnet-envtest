using System.Text;

using BenchmarkDotNet.Attributes;

using Kubernetes.EnvTest.Provisioning;

namespace Kubernetes.EnvTest.Benchmarks;

/// <summary>
/// Benchmarks for release-index parsing and version resolution — the hot path
/// of every provisioning call (and of CI pre-warm steps invoking the CLI).
/// </summary>
[MemoryDiagnoser]
public class ReleaseIndexBenchmarks
{
    private string _indexYaml = string.Empty;
    private ReleaseIndex _index = null!;

    [Params(10, 100)]
    public int ReleaseCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder("releases:\n");
        for (int i = 0; i < ReleaseCount; i++)
        {
            string version = $"v1.{20 + (i / 10)}.{i % 10}";
            builder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {version}:");
            foreach (string platform in (string[])["linux-amd64", "linux-arm64", "darwin-amd64", "darwin-arm64", "windows-amd64", "windows-arm64"])
            {
                builder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"    envtest-{version}-{platform}.tar.gz:");
                builder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"      hash: {new string('a', 128)}");
                builder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"      selfLink: https://example.test/envtest-{version}-{platform}.tar.gz");
            }
        }

        _indexYaml = builder.ToString();
        _index = ReleaseIndex.Parse(_indexYaml);
    }

    [Benchmark]
    public ReleaseIndex Parse() => ReleaseIndex.Parse(_indexYaml);

    [Benchmark]
    public KubernetesVersion ResolveLatestStable() => _index.Resolve(KubernetesVersionSpec.LatestStable);

    [Benchmark]
    public ReleaseArchive GetArchive() =>
        _index.GetArchive(_index.Resolve(KubernetesVersionSpec.LatestStable), new ReleasePlatform("linux", "amd64"));
}
