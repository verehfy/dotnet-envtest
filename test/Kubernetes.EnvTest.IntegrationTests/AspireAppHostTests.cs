using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Kubernetes.EnvTest.IntegrationTests;

/// <summary>
/// End-to-end test: the Aspire AppHost provisions a real envtest control plane
/// (downloading Kubernetes binaries on first run), installs a CRD, and
/// orchestrates a sample worker that talks to the cluster through a
/// kubeconfig. Success is the worker running to completion with exit code 0.
/// </summary>
public class AspireAppHostTests
{
    [Fact]
    public async Task Sample_worker_completes_against_the_envtest_control_plane()
    {
        var timeout = TimeSpan.FromMinutes(10);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(timeout);

        IDistributedApplicationTestingBuilder appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<Projects.Kubernetes_EnvTest_AppHost>(cancellation.Token);

        await using DistributedApplication app = await appHost.BuildAsync(cancellation.Token);
        await app.StartAsync(cancellation.Token);

        ResourceNotificationService notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        ResourceEvent terminal = await notifications.WaitForResourceAsync(
            "sample-worker",
            resourceEvent => resourceEvent.Snapshot.State?.Text == KnownResourceStates.Finished
                             || resourceEvent.Snapshot.State?.Text == KnownResourceStates.FailedToStart
                             || resourceEvent.Snapshot.State?.Text == KnownResourceStates.Exited,
            cancellation.Token);

        Assert.Equal(KnownResourceStates.Finished, terminal.Snapshot.State?.Text);
        Assert.Equal(0, terminal.Snapshot.ExitCode);
    }
}
