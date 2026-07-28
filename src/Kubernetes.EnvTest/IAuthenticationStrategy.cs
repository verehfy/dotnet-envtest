using k8s;

namespace Kubernetes.EnvTest;

/// <summary>
/// Knows how to configure the API server for a particular authentication
/// method and provision users under it (upstream envtest's <c>Authn</c>).
/// Methods are called in order: <see cref="Configure"/>, <see cref="StartAsync"/>,
/// <see cref="AddUserAsync"/> (zero or more times), <see cref="StopAsync"/>.
/// </summary>
public interface IAuthenticationStrategy
{
    /// <summary>
    /// Provides the API server working directory and configures the server's
    /// arguments to use this authenticator. Called before the server starts.
    /// </summary>
    /// <param name="workingDirectory">The API server's certificate directory.</param>
    /// <param name="arguments">The API server's argument set to amend.</param>
    void Configure(string workingDirectory, ArgumentSet arguments);

    /// <summary>Starts the authenticator; called just before the API server starts.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions a user, returning a copy of the base configuration set up to
    /// authenticate as that user. Only valid while the authenticator is running.
    /// </summary>
    /// <param name="user">The user to provision.</param>
    /// <param name="baseConfiguration">Optional base client configuration whose settings are preserved.</param>
    /// <param name="cancellationToken">Cancels the provisioning.</param>
    /// <returns>The user's client configuration (host and CA data may still be incomplete until the server is started).</returns>
    Task<KubernetesClientConfiguration> AddUserAsync(
        User user,
        KubernetesClientConfiguration? baseConfiguration = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the authenticator; called during control-plane teardown.</summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
