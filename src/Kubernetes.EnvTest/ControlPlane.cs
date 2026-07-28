using k8s;

namespace Kubernetes.EnvTest;

/// <summary>
/// Starts and stops the test control plane: an <see cref="Etcd"/> instance and
/// a <see cref="ApiServer"/> wired to it.
/// </summary>
public sealed class ControlPlane
{
    /// <summary>Gets or sets the API server; created on demand.</summary>
    public ApiServer? ApiServer { get; set; }

    /// <summary>Gets or sets the etcd instance; created on demand.</summary>
    public Etcd? Etcd { get; set; }

    /// <summary>
    /// Gets or sets the path of the <c>kubectl</c> binary used by
    /// <see cref="AuthenticatedUser.GetKubectlAsync"/>.
    /// </summary>
    public string? KubectlPath { get; set; }

    /// <summary>Returns the API server, initializing it if necessary.</summary>
    public ApiServer GetApiServer() => ApiServer ??= new ApiServer();

    /// <summary>Returns the etcd instance, initializing it if necessary.</summary>
    public Etcd GetEtcd() => Etcd ??= new Etcd();

    /// <summary>Starts etcd, then the API server. On failure both are torn down.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Etcd etcd = GetEtcd();
        await etcd.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ApiServer apiServer = GetApiServer();
            apiServer.EtcdUri = etcd.ClientUri;
            await apiServer.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await SwallowStopErrorsAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Stops the API server and etcd, cleaning up their data directories.</summary>
    /// <param name="cancellationToken">Cancels waiting for graceful stops.</param>
    /// <exception cref="AggregateException">One or both components failed to stop.</exception>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<Exception>();

        if (ApiServer is not null)
        {
            try
            {
                await ApiServer.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(ex);
            }
        }

        if (Etcd is not null)
        {
            try
            {
                await Etcd.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Failed to stop the control plane cleanly.", errors);
        }
    }

    /// <summary>
    /// Provisions a new user in the cluster using the API server's
    /// authentication strategy (defaulted when the server starts).
    /// </summary>
    /// <param name="user">The user to provision.</param>
    /// <param name="baseConfiguration">Optional base client configuration whose settings are preserved.</param>
    /// <param name="cancellationToken">Cancels the provisioning.</param>
    /// <returns>The provisioned user; its connection details are valid once the API server is running.</returns>
    public async Task<AuthenticatedUser> AddUserAsync(
        User user,
        KubernetesClientConfiguration? baseConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        IAuthenticationStrategy? authentication = GetApiServer().SecureServing.Authentication;
        if (authentication is null)
        {
            throw new InvalidOperationException(
                "No API server authentication is configured yet. The API server defaults one when started; "
                + "start the control plane first or set SecureServing.Authentication explicitly.");
        }

        KubernetesClientConfiguration configuration = await authentication
            .AddUserAsync(user, baseConfiguration, cancellationToken)
            .ConfigureAwait(false);
        return new AuthenticatedUser(configuration, this);
    }

    private async Task SwallowStopErrorsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AggregateException)
        {
            // The original start failure is more interesting.
        }
    }
}
