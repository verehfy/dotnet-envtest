namespace Kubernetes.EnvTest;

/// <summary>A host address and port a control-plane process listens on.</summary>
/// <param name="Address">The host name or IP address, for example <c>127.0.0.1</c>.</param>
/// <param name="Port">The TCP port.</param>
public sealed record ListenAddress(string Address, int Port)
{
    /// <summary>Returns the joined <c>host:port</c> pair, bracketing IPv6 addresses.</summary>
    public string HostPort() =>
        Address.Contains(':', StringComparison.Ordinal) ? $"[{Address}]:{Port}" : $"{Address}:{Port}";

    /// <summary>Builds a URI for this address with the given scheme and path.</summary>
    /// <param name="scheme">The URI scheme, for example <c>https</c>.</param>
    /// <param name="path">The absolute path, for example <c>/healthz</c>; may be empty.</param>
    public Uri ToUri(string scheme, string path = "") =>
        new($"{scheme}://{HostPort()}{(path.Length == 0 || path.StartsWith('/') ? path : "/" + path)}");
}
