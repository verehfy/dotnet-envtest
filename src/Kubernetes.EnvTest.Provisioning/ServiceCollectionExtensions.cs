using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kubernetes.EnvTest.Provisioning;

/// <summary>Dependency-injection registration for envtest binary provisioning.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBinaryProvisioner"/> and its options with the
    /// service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of <see cref="BinaryProvisionerOptions"/> (Kubernetes version, store directory, index URL, platform).</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddEnvTestBinaryProvisioning(
        this IServiceCollection services,
        Action<BinaryProvisionerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<BinaryProvisionerOptions>();
        }

        services.TryAddSingleton<IBinaryProvisioner, BinaryProvisioner>();
        return services;
    }
}
