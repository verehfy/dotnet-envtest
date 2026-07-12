namespace Kubernetes.EnvTest;

/// <summary>Thrown when a control-plane child process fails to start or become ready.</summary>
public sealed class ProcessStartException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="ProcessStartException"/> class.</summary>
    /// <param name="processName">The short process name, for example <c>kube-apiserver</c>.</param>
    /// <param name="executablePath">The full path of the executable.</param>
    /// <param name="reason">Why the start failed.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public ProcessStartException(string processName, string executablePath, string reason, Exception? innerException = null)
        : base($"Failed to start {processName} ('{executablePath}'): {reason}", innerException!)
    {
        ProcessName = processName;
        ExecutablePath = executablePath;
    }

    /// <summary>Gets the short process name.</summary>
    public string ProcessName { get; }

    /// <summary>Gets the full path of the executable.</summary>
    public string ExecutablePath { get; }
}

/// <summary>Thrown when a control-plane child process cannot be stopped cleanly.</summary>
public sealed class ProcessStopException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="ProcessStopException"/> class.</summary>
    /// <param name="processName">The short process name, for example <c>etcd</c>.</param>
    /// <param name="executablePath">The full path of the executable.</param>
    /// <param name="reason">Why the stop failed.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public ProcessStopException(string processName, string executablePath, string reason, Exception? innerException = null)
        : base($"Failed to stop {processName} ('{executablePath}'): {reason}", innerException!)
    {
        ProcessName = processName;
        ExecutablePath = executablePath;
    }

    /// <summary>Gets the short process name.</summary>
    public string ProcessName { get; }

    /// <summary>Gets the full path of the executable.</summary>
    public string ExecutablePath { get; }
}

/// <summary>Thrown when the control plane could not be started after all retries.</summary>
public sealed class ControlPlaneStartException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="ControlPlaneStartException"/> class.</summary>
    /// <param name="attempts">How many start attempts were made.</param>
    /// <param name="innerException">The failure of the last attempt.</param>
    public ControlPlaneStartException(int attempts, Exception innerException)
        : base($"Failed to start the control plane; retried {attempts} times.", innerException)
    {
        Attempts = attempts;
    }

    /// <summary>Gets how many start attempts were made.</summary>
    public int Attempts { get; }
}

/// <summary>Thrown when CRDs cannot be read, created, or fail to appear in discovery in time.</summary>
public sealed class CrdInstallationException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="CrdInstallationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public CrdInstallationException(string message, Exception? innerException = null)
        : base(message, innerException!)
    {
    }
}

/// <summary>Thrown when webhook configurations cannot be read, created, or fail to register in time.</summary>
public sealed class WebhookInstallationException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="WebhookInstallationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public WebhookInstallationException(string message, Exception? innerException = null)
        : base(message, innerException!)
    {
    }
}

/// <summary>Thrown when the started API server does not become usable in time (for example, the default namespace never appears).</summary>
public sealed class ControlPlaneReadinessException : EnvTestException
{
    /// <summary>Initializes a new instance of the <see cref="ControlPlaneReadinessException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public ControlPlaneReadinessException(string message, Exception? innerException = null)
        : base(message, innerException!)
    {
    }
}
