using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Kubernetes.EnvTest.Provisioning;

/// <summary>
/// A semantic Kubernetes release version (for example <c>1.31.0</c> or
/// <c>1.35.0-alpha.3</c>), ordered by SemVer 2.0.0 precedence.
/// </summary>
/// <remarks>
/// Mirrors the version handling of upstream <c>setup-envtest</c>: a leading
/// <c>v</c> is accepted when parsing and build metadata (<c>+...</c>) is not
/// supported, because Kubernetes releases never carry it.
/// </remarks>
public sealed class KubernetesVersion :
    IComparable<KubernetesVersion>,
    IEquatable<KubernetesVersion>
{
    /// <summary>Initializes a new instance of the <see cref="KubernetesVersion"/> class.</summary>
    /// <param name="major">The major version component.</param>
    /// <param name="minor">The minor version component.</param>
    /// <param name="patch">The patch version component.</param>
    /// <param name="preRelease">The pre-release suffix without the leading dash (for example <c>alpha.3</c>), or <see langword="null"/> for a stable release.</param>
    public KubernetesVersion(int major, int minor, int patch, string? preRelease = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrEmpty(preRelease) ? null : preRelease;
    }

    /// <summary>Gets the major version component.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version component.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version component.</summary>
    public int Patch { get; }

    /// <summary>Gets the pre-release suffix (for example <c>alpha.3</c>), or <see langword="null"/> for a stable release.</summary>
    public string? PreRelease { get; }

    /// <summary>Gets a value indicating whether this is a pre-release (alpha/beta/rc) version.</summary>
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>Parses a version string such as <c>1.31.0</c>, <c>v1.31.0</c> or <c>v1.35.0-alpha.3</c>.</summary>
    /// <param name="value">The version string, optionally prefixed with <c>v</c>.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="FormatException">The string is not a valid Kubernetes semantic version.</exception>
    public static KubernetesVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out KubernetesVersion? version)
            ? version
            : throw new FormatException($"'{value}' is not a valid Kubernetes semantic version (expected e.g. '1.31.0' or 'v1.35.0-alpha.3').");
    }

    /// <summary>Attempts to parse a version string such as <c>1.31.0</c>, <c>v1.31.0</c> or <c>v1.35.0-alpha.3</c>.</summary>
    /// <param name="value">The version string, optionally prefixed with <c>v</c>.</param>
    /// <param name="version">The parsed version, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out KubernetesVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.StartsWith('v'))
        {
            span = span[1..];
        }

        string? preRelease = null;
        int dashIndex = span.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = span[(dashIndex + 1)..].ToString();
            span = span[..dashIndex];
            if (preRelease.Length == 0 || !IsValidPreRelease(preRelease))
            {
                return false;
            }
        }

        Span<Range> parts = stackalloc Range[4];
        int count = span.Split(parts, '.');
        if (count != 3)
        {
            return false;
        }

        Span<int> components = stackalloc int[3];
        for (int i = 0; i < 3; i++)
        {
            ReadOnlySpan<char> part = span[parts[i]];
            if (part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out components[i]))
            {
                return false;
            }
        }

        version = new KubernetesVersion(components[0], components[1], components[2], preRelease);
        return true;
    }

    /// <inheritdoc/>
    public int CompareTo(KubernetesVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <inheritdoc/>
    public bool Equals(KubernetesVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is KubernetesVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    /// <summary>Returns the version without a <c>v</c> prefix, for example <c>1.31.0</c>.</summary>
    public override string ToString() =>
        PreRelease is null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    /// <summary>Returns the version with a <c>v</c> prefix, for example <c>v1.31.0</c>, as used in the release index.</summary>
    public string ToTaggedString() => "v" + ToString();

    /// <summary>Compares two versions by SemVer precedence.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator <(KubernetesVersion? left, KubernetesVersion? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Compares two versions by SemVer precedence.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator >(KubernetesVersion? left, KubernetesVersion? right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>Compares two versions by SemVer precedence.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator <=(KubernetesVersion? left, KubernetesVersion? right) => !(left > right);

    /// <summary>Compares two versions by SemVer precedence.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator >=(KubernetesVersion? left, KubernetesVersion? right) => !(left < right);

    /// <summary>Compares two versions for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator ==(KubernetesVersion? left, KubernetesVersion? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Compares two versions for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator !=(KubernetesVersion? left, KubernetesVersion? right) => !(left == right);

    private static bool IsValidPreRelease(string preRelease)
    {
        foreach (string identifier in preRelease.Split('.'))
        {
            if (identifier.Length == 0)
            {
                return false;
            }

            foreach (char c in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int ComparePreRelease(string? left, string? right)
    {
        // SemVer 2.0.0 item 11: a version without a pre-release has higher
        // precedence than one with a pre-release.
        if (left is null)
        {
            return right is null ? 0 : 1;
        }

        if (right is null)
        {
            return -1;
        }

        string[] leftIdentifiers = left.Split('.');
        string[] rightIdentifiers = right.Split('.');
        int length = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);
        for (int i = 0; i < length; i++)
        {
            int result = ComparePreReleaseIdentifier(leftIdentifiers[i], rightIdentifiers[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static int ComparePreReleaseIdentifier(string left, string right)
    {
        bool leftIsNumeric = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long leftNumber);
        bool rightIsNumeric = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out long rightNumber);

        if (leftIsNumeric && rightIsNumeric)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        // Numeric identifiers always have lower precedence than alphanumeric ones.
        if (leftIsNumeric)
        {
            return -1;
        }

        if (rightIsNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }
}
