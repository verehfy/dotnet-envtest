using k8s;
using k8s.Models;

using Xunit;

namespace Kubernetes.EnvTest.IntegrationTests;

/// <summary>
/// Direct integration tests of <see cref="TestEnvironment"/> without the
/// Aspire layer: start, use, provision users, and stop a real control plane.
/// Binaries are downloaded into the shared setup-envtest store on first run.
/// </summary>
public class TestEnvironmentTests
{
    [Fact]
    public async Task Start_use_and_stop_a_control_plane()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMinutes(10));

        var environment = new TestEnvironment
        {
            DownloadBinaryAssets = true,
            DownloadBinaryAssetsVersion = System.Environment.GetEnvironmentVariable("ENVTEST_K8S_VERSION"),
        };

        KubernetesClientConfiguration config = await environment.StartAsync(cancellation.Token);
        try
        {
            Assert.NotNull(environment.KubeConfig);
            Assert.NotEmpty(environment.KubeConfig);

            using var client = new k8s.Kubernetes(config);
            V1Namespace defaultNamespace = await client.CoreV1.ReadNamespaceAsync("default", cancellationToken: cancellation.Token);
            Assert.Equal("default", defaultNamespace.Metadata.Name);

            // A separately provisioned user can reach the API server too.
            AuthenticatedUser viewer = await environment.AddUserAsync(
                new User("viewer", ["system:masters"]),
                cancellationToken: cancellation.Token);
            using var viewerClient = viewer.CreateKubernetesClient();
            await viewerClient.CoreV1.ListNamespaceAsync(cancellationToken: cancellation.Token);

            // The bundled kubectl works against the environment via kubeconfig.
            Kubectl kubectl = await viewer.GetKubectlAsync(cancellation.Token);
            KubectlResult result = await kubectl.RunAsync(["get", "namespaces"], cancellation.Token);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("default", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            await environment.StopAsync(cancellation.Token);
        }
    }
}
