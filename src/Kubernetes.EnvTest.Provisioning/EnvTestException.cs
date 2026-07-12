// Note: this type intentionally lives in the Kubernetes.EnvTest namespace even
// though it ships in the Kubernetes.EnvTest.Provisioning assembly, so that
// consumers of either package can catch every library error with a single
// `catch (EnvTestException)` without importing the provisioning namespace.

namespace Kubernetes.EnvTest;

/// <summary>
/// Base type for all exceptions thrown by the Kubernetes.EnvTest libraries.
/// Catch this to handle any envtest-specific failure; more specific subclasses
/// carry structured context (versions, paths, process names) for diagnostics.
/// </summary>
public class EnvTestException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EnvTestException"/> class.</summary>
    public EnvTestException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EnvTestException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public EnvTestException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EnvTestException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EnvTestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
