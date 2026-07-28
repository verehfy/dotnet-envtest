// A minimal "consumer" application used by the Aspire integration tests: it
// connects to the envtest-provided cluster exactly like a real workload would
// (via the KUBECONFIG env var), exercises core resources and the CRD the
// AppHost installed, and exits 0 on success.

using k8s;
using k8s.Models;

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
CancellationToken token = cancellation.Token;

try
{
    string kubeConfigPath = Environment.GetEnvironmentVariable("KUBECONFIG")
        ?? throw new InvalidOperationException("KUBECONFIG is not set.");

    KubernetesClientConfiguration configuration =
        await KubernetesClientConfiguration.BuildConfigFromConfigFileAsync(new FileInfo(kubeConfigPath));
    using var client = new Kubernetes(configuration);

    // 1. Core API is reachable.
    V1NamespaceList namespaces = await client.CoreV1.ListNamespaceAsync(cancellationToken: token);
    Console.WriteLine($"sample-worker: found {namespaces.Items.Count} namespaces");

    // 2. Writes work end to end.
    var configMap = new V1ConfigMap
    {
        Metadata = new V1ObjectMeta { Name = "envtest-sample", NamespaceProperty = "default" },
        Data = new Dictionary<string, string> { ["hello"] = "from-envtest" },
    };
    await client.CoreV1.CreateNamespacedConfigMapAsync(configMap, "default", cancellationToken: token);
    V1ConfigMap fetched = await client.CoreV1.ReadNamespacedConfigMapAsync("envtest-sample", "default", cancellationToken: token);
    if (fetched.Data?["hello"] != "from-envtest")
    {
        throw new InvalidOperationException("ConfigMap round-trip returned unexpected data.");
    }

    // 3. The CRD installed by the AppHost serves custom resources.
    object widget = new
    {
        apiVersion = "example.envtest.io/v1",
        kind = "Widget",
        metadata = new { name = "sample-widget", @namespace = "default" },
        spec = new { size = 3 },
    };
    await client.CustomObjects.CreateNamespacedCustomObjectAsync(
        widget, "example.envtest.io", "v1", "default", "widgets", cancellationToken: token);
    object stored = await client.CustomObjects.GetNamespacedCustomObjectAsync(
        "example.envtest.io", "v1", "default", "widgets", "sample-widget", cancellationToken: token);
    Console.WriteLine($"sample-worker: widget stored: {stored is not null}");

    Console.WriteLine("SAMPLE-WORKER-SUCCESS");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"sample-worker: FAILED: {ex}");
    return 1;
}
