using BenchmarkDotNet.Attributes;

using Kubernetes.EnvTest;

namespace Kubernetes.EnvTest.Benchmarks;

/// <summary>
/// Benchmarks for the argument-rendering and certificate-generation work done
/// on every control-plane start.
/// </summary>
[MemoryDiagnoser]
public class ControlPlaneBenchmarks
{
    private static readonly Dictionary<string, IReadOnlyList<string>> ApiServerDefaults = new(StringComparer.Ordinal)
    {
        ["service-cluster-ip-range"] = ["10.0.0.0/24"],
        ["allow-privileged"] = ["true"],
        ["disable-admission-plugins"] = ["ServiceAccount"],
        ["cert-dir"] = ["/tmp/certs"],
        ["authorization-mode"] = ["RBAC"],
        ["secure-port"] = ["6443"],
        ["bind-address"] = ["127.0.0.1"],
        ["service-account-issuer"] = ["https://127.0.0.1:6443/"],
        ["service-account-key-file"] = ["/tmp/certs/sa-signer.crt"],
        ["service-account-signing-key-file"] = ["/tmp/certs/sa-signer.key"],
        ["insecure-port"] = ["0"],
        ["etcd-servers"] = ["http://127.0.0.1:2379"],
    };

    [Benchmark]
    public IReadOnlyList<string> RenderApiServerArguments()
    {
        var arguments = new ArgumentSet()
            .Append("enable-admission-plugins", "NamespaceLifecycle")
            .Disable("insecure-port")
            .Set("v", "2");
        return arguments.AsStrings(ApiServerDefaults);
    }

    [Benchmark]
    public int GenerateCaAndServingCertificate()
    {
        using var ca = new TinyCa();
        using CertPair serving = ca.CreateServingCertificate("localhost");
        return serving.CertificatePemBytes().Length;
    }

    [Benchmark]
    public int GenerateClientCertificate()
    {
        using var ca = new TinyCa();
        using CertPair client = ca.CreateClientCertificate(new User("admin", ["system:masters"]));
        return client.PrivateKeyPemBytes().Length;
    }
}
