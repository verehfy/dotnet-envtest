using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Kubernetes.EnvTest.Provisioning;

/// <summary>
/// A user-supplied version request: an exact release (<c>1.31.0</c>), a release
/// series (<c>1.31</c>, meaning "latest stable 1.31.x"), or the latest stable
/// release overall (empty or <c>latest</c>).
/// </summary>
/// <remarks>
/// Mirrors <c>parseKubernetesVersion</c> in upstream
/// <c>controller-runtime/pkg/envtest/binaries.go</c>.
/// </remarks>
public sealed class KubernetesVersionSpec
{
    private KubernetesVersionSpec(KubernetesVersion? exact, int? seriesMajor, int? seriesMinor)
    {
        Exact = exact;
        SeriesMajor = seriesMajor;
        SeriesMinor = seriesMinor;
    }

    /// <summary>Gets the spec selecting the latest stable release in the index.</summary>
    public static KubernetesVersionSpec LatestStable { get; } = new(null, null, null);

    /// <summary>Gets the exact requested version, or <see langword="null"/> when this spec is a series or "latest".</summary>
    public KubernetesVersion? Exact { get; }

    /// <summary>Gets the major component of a requested release series, or <see langword="null"/>.</summary>
    public int? SeriesMajor { get; }

    /// <summary>Gets the minor component of a requested release series, or <see langword="null"/>.</summary>
    public int? SeriesMinor { get; }

    /// <summary>Gets a value indicating whether this spec must be resolved against the release index (series or latest).</summary>
    public bool RequiresIndex => Exact is null;

    /// <summary>Creates a spec for an exact version.</summary>
    /// <param name="version">The exact version.</param>
    public static KubernetesVersionSpec ForExact(KubernetesVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new KubernetesVersionSpec(version, null, null);
    }

    /// <summary>Creates a spec for a release series, for example <c>1.31</c>.</summary>
    /// <param name="major">The series major version.</param>
    /// <param name="minor">The series minor version.</param>
    public static KubernetesVersionSpec ForSeries(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        return new KubernetesVersionSpec(null, major, minor);
    }

    /// <summary>Parses a version request string.</summary>
    /// <param name="value">
    /// <see langword="null"/>, empty or <c>latest</c> for the latest stable release; <c>1.31</c> or
    /// <c>v1.31</c> for a release series; or a full version such as <c>1.31.0</c>.
    /// </param>
    /// <returns>The parsed spec.</returns>
    /// <exception cref="FormatException">The string is neither a version, a series, nor <c>latest</c>.</exception>
    public static KubernetesVersionSpec Parse(string? value)
    {
        return TryParse(value, out KubernetesVersionSpec? spec)
            ? spec
            : throw new FormatException($"Could not parse '{value}' as a Kubernetes version (expected e.g. '1.31.0', '1.31' or 'latest').");
    }

    /// <summary>Attempts to parse a version request string; see <see cref="Parse"/>.</summary>
    /// <param name="value">The version request string.</param>
    /// <param name="spec">The parsed spec, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out KubernetesVersionSpec? spec)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            spec = LatestStable;
            return true;
        }

        string trimmed = value.Trim();
        if (KubernetesVersion.TryParse(trimmed, out KubernetesVersion? exact))
        {
            spec = ForExact(exact);
            return true;
        }

        spec = TryParseSeries(trimmed);
        return spec is not null;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Exact is not null)
        {
            return Exact.ToString();
        }

        return SeriesMajor is not null
            ? string.Create(CultureInfo.InvariantCulture, $"{SeriesMajor}.{SeriesMinor}")
            : "latest";
    }

    private static KubernetesVersionSpec? TryParseSeries(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        if (span.StartsWith('v'))
        {
            span = span[1..];
        }

        int dotIndex = span.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == span.Length - 1)
        {
            return null;
        }

        if (!int.TryParse(span[..dotIndex], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(span[(dotIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            return null;
        }

        return ForSeries(major, minor);
    }
}
