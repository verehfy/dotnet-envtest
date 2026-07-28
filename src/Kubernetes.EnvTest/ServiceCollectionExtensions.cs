using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kubernetes.EnvTest;

/// <summary>Dependency-injection registration for the envtest environment.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TestEnvironment"/> as a transient service wired to
    /// the container's <see cref="ILoggerFactory"/>. Each resolution returns a
    /// fresh, unstarted environment; the caller owns its lifecycle
    /// (<see cref="TestEnvironment.StartAsync"/> / <see cref="TestEnvironment.StopAsync"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration applied to each created environment (K8s version, binary paths, CRD paths, ...).</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddKubernetesEnvTest(
        this IServiceCollection services,
        Action<TestEnvironment>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient(provider =>
        {
            var environment = new TestEnvironment(provider.GetService<ILoggerFactory>());
            configure?.Invoke(environment);
            return environment;
        });

        return services;
    }
}
