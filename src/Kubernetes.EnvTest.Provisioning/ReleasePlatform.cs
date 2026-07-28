using System.Runtime.InteropServices;

namespace Kubernetes.EnvTest.Provisioning;

/// <summary>
/// An operating system / CPU architecture pair using the Go naming convention
/// used by the envtest release index (for example <c>linux/amd64</c>).
/// </summary>
/// <param name="OperatingSystem">The Go-style OS name: <c>linux</c>, <c>darwin</c> or <c>windows</c>.</param>
/// <param name="Architecture">The Go-style architecture name: <c>amd64</c> or <c>arm64</c>.</param>
public readonly record struct ReleasePlatform(string OperatingSystem, string Architecture)
{
    private static readonly Dictionary<string, ReleasePlatform> RidMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["win-x64"] = new ReleasePlatform("windows", "amd64"),
        ["win-arm64"] = new ReleasePlatform("windows", "arm64"),
        ["osx-x64"] = new ReleasePlatform("darwin", "amd64"),
        ["osx-arm64"] = new ReleasePlatform("darwin", "arm64"),
        ["linux-x64"] = new ReleasePlatform("linux", "amd64"),
        ["linux-arm64"] = new ReleasePlatform("linux", "arm64"),
    };

    /// <summary>Gets the platform of the current process.</summary>
    /// <exception cref="System.PlatformNotSupportedException">The current OS or architecture has no envtest binaries at all.</exception>
    public static ReleasePlatform Current
    {
        get
        {
            string os = true switch
            {
                _ when System.OperatingSystem.IsWindows() => "windows",
                _ when System.OperatingSystem.IsMacOS() => "darwin",
                _ when System.OperatingSystem.IsLinux() => "linux",
                _ => throw new System.PlatformNotSupportedException(
                    $"envtest binaries are not published for OS '{RuntimeInformation.OSDescription}'."),
            };

            string arch = RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "amd64",
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                var other => throw new System.PlatformNotSupportedException(
                    $"envtest binaries are not published for architecture '{other}'."),
            };

            return new ReleasePlatform(os, arch);
        }
    }

    /// <summary>Gets a value indicating whether executables on this platform carry an <c>.exe</c> suffix.</summary>
    public bool IsWindows => string.Equals(OperatingSystem, "windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>Maps a .NET runtime identifier (for example <c>linux-x64</c>) to a release platform.</summary>
    /// <param name="rid">One of the supported RIDs: <c>win-x64</c>, <c>win-arm64</c>, <c>osx-x64</c>, <c>osx-arm64</c>, <c>linux-x64</c>, <c>linux-arm64</c>.</param>
    /// <returns>The corresponding platform.</returns>
    /// <exception cref="ArgumentException">The RID is not one of the six supported values.</exception>
    public static ReleasePlatform FromRuntimeIdentifier(string rid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rid);
        return RidMap.TryGetValue(rid, out ReleasePlatform platform)
            ? platform
            : throw new ArgumentException(
                $"Unsupported runtime identifier '{rid}'. Supported: {string.Join(", ", RidMap.Keys)}.",
                nameof(rid));
    }

    /// <summary>Returns the archive-name suffix for this platform, for example <c>linux-amd64</c>.</summary>
    public string ToArchiveSuffix() => $"{OperatingSystem}-{Architecture}";

    /// <summary>Appends <c>.exe</c> to a binary name when this platform is Windows.</summary>
    /// <param name="name">The bare binary name, for example <c>kube-apiserver</c>.</param>
    public string GetBinaryFileName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return IsWindows ? name + ".exe" : name;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{OperatingSystem}/{Architecture}";
}
