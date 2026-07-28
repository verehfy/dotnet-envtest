// Aspire AppHost that provisions a real envtest control plane (etcd +
// kube-apiserver, downloading binaries on first use) and orchestrates a sample
// worker that consumes it through a kubeconfig — the same way a production
// service would consume a cluster.

using Kubernetes.EnvTest;

using Microsoft.Extensions.Logging;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging
    .AddConsole()
    .SetMinimumLevel(LogLevel.Information));

var environment = new TestEnvironment(loggerFactory)
{
    DownloadBinaryAssets = true,
    DownloadBinaryAssetsVersion = System.Environment.GetEnvironmentVariable("ENVTEST_K8S_VERSION"),
};
environment.CrdDirectoryPaths.Add(Path.Combine(AppContext.BaseDirectory, "TestData", "widgets-crd.yaml"));
environment.ErrorIfCrdPathMissing = true;

await environment.StartAsync();

string kubeConfigPath = Path.Combine(Path.GetTempPath(), $"envtest-apphost-{Guid.NewGuid():N}.kubeconfig");
await File.WriteAllBytesAsync(kubeConfigPath, environment.KubeConfig!);

try
{
    builder.AddProject<Projects.Kubernetes_EnvTest_SampleWorker>("sample-worker")
        .WithEnvironment("KUBECONFIG", kubeConfigPath);

    await builder.Build().RunAsync();
}
finally
{
    await environment.StopAsync();
    File.Delete(kubeConfigPath);
}
