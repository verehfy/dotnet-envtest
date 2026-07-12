using System.Net;
using System.Net.Sockets;

namespace Kubernetes.EnvTest.Internal;

/// <summary>
/// Suggests free localhost ports for control-plane processes, with a
/// file-based reservation cache (shared with concurrently running test
/// processes) so that a port handed out to one process is not re-suggested to
/// another within a two-minute window. Mirrors upstream envtest's
/// <c>internal/testing/addr</c>.
/// </summary>
internal static class AddressAllocator
{
    private const int PortConflictRetries = 100;
    private const string PortFilePrefix = "port-";
    private static readonly TimeSpan PortReserveTime = TimeSpan.FromMinutes(2);
    private static readonly Lazy<string> CacheDirectory = new(ResolveCacheDirectory);

    /// <summary>
    /// Suggests a free port on the given host (defaulting to <c>localhost</c>),
    /// returning the port and the host's resolved IP address.
    /// </summary>
    internal static async Task<ListenAddress> SuggestAsync(string? listenHost, CancellationToken cancellationToken)
    {
        listenHost ??= "localhost";
        if (listenHost.Length == 0)
        {
            listenHost = "localhost";
        }

        for (int attempt = 0; attempt < PortConflictRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(listenHost, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                throw new EnvTestException($"Unable to resolve host '{listenHost}' to an IP address.");
            }

            // Prefer IPv4 loopback like Go's ResolveTCPAddr does for "localhost".
            IPAddress address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];

            var listener = new TcpListener(address, 0);
            listener.Start();
            int port;
            try
            {
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }

            if (TryReservePort(port))
            {
                return new ListenAddress(address.ToString(), port);
            }
        }

        throw new EnvTestException($"No free ports found after {PortConflictRetries} retries.");
    }

    private static bool TryReservePort(int port)
    {
        string cacheDir = CacheDirectory.Value;
        CleanUpExpiredReservations(cacheDir);

        string path = Path.Combine(cacheDir, PortFilePrefix + port);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    private static void CleanUpExpiredReservations(string cacheDir)
    {
        foreach (string file in Directory.EnumerateFiles(cacheDir, PortFilePrefix + "*"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > PortReserveTime)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Another process may have deleted or recreated the file concurrently.
            }
            catch (UnauthorizedAccessException)
            {
                // Locked by another process on Windows; skip.
            }
        }
    }

    private static string ResolveCacheDirectory()
    {
        string baseDir;
        try
        {
            baseDir = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData,
                System.Environment.SpecialFolderOption.Create);
        }
        catch (PlatformNotSupportedException)
        {
            baseDir = Path.GetTempPath();
        }

        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.GetTempPath();
        }

        // Match upstream's cache directory name so concurrent Go and .NET test
        // runs share one reservation space where the base directories agree.
        string cacheDir = Path.Combine(baseDir, "kubebuilder-envtest");
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }
}
