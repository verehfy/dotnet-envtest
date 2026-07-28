namespace Kubernetes.EnvTest.Provisioning;

/// <summary>The resolved on-disk control-plane binaries for one Kubernetes version and platform.</summary>
/// <param name="Version">The Kubernetes version of the binaries.</param>
/// <param name="Platform">The OS/architecture the binaries were built for.</param>
/// <param name="Directory">The directory containing the binaries; suitable as a <c>KUBEBUILDER_ASSETS</c> value.</param>
/// <param name="ApiServerPath">The full path of the <c>kube-apiserver</c> binary.</param>
/// <param name="EtcdPath">The full path of the <c>etcd</c> binary.</param>
/// <param name="KubectlPath">The full path of the <c>kubectl</c> binary.</param>
public sealed record EnvTestBinaries(
    KubernetesVersion Version,
    ReleasePlatform Platform,
    string Directory,
    string ApiServerPath,
    string EtcdPath,
    string KubectlPath);
