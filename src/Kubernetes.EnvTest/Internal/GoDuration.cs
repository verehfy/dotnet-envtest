using System.Globalization;

namespace Kubernetes.EnvTest.Internal;

/// <summary>
/// Parses Go <c>time.Duration</c> strings (for example <c>20s</c>, <c>1m30s</c>,
/// <c>300ms</c>) so that the <c>KUBEBUILDER_CONTROLPLANE_START_TIMEOUT</c> /
/// <c>..._STOP_TIMEOUT</c> environment variables stay compatible with the Go
/// implementation.
/// </summary>
internal static class GoDuration
{
    private static readonly Dictionary<string, double> UnitMilliseconds = new(StringComparer.Ordinal)
    {
        ["ns"] = 0.000001,
        ["us"] = 0.001,
        ["µs"] = 0.001,
        ["μs"] = 0.001,
        ["ms"] = 1,
        ["s"] = 1000,
        ["m"] = 60_000,
        ["h"] = 3_600_000,
    };

    /// <summary>Parses a Go duration string.</summary>
    /// <param name="value">The duration string, for example <c>1m30s</c>.</param>
    /// <returns>The parsed duration.</returns>
    /// <exception cref="FormatException">The string is not a valid Go duration.</exception>
    internal static TimeSpan Parse(string value)
    {
        return TryParse(value, out TimeSpan result)
            ? result
            : throw new FormatException($"'{value}' is not a valid Go duration (expected e.g. '20s', '1m30s' or '300ms').");
    }

    /// <summary>Attempts to parse a Go duration string.</summary>
    internal static bool TryParse(string? value, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        bool negative = false;
        if (span.StartsWith('-') || span.StartsWith('+'))
        {
            negative = span[0] == '-';
            span = span[1..];
        }

        // Go treats a bare "0" (with optional sign) as valid.
        if (span.Equals("0", StringComparison.Ordinal))
        {
            return true;
        }

        if (span.IsEmpty)
        {
            return false;
        }

        double totalMilliseconds = 0;
        while (!span.IsEmpty)
        {
            int index = 0;
            while (index < span.Length && (char.IsAsciiDigit(span[index]) || span[index] == '.'))
            {
                index++;
            }

            if (index == 0)
            {
                return false;
            }

            if (!double.TryParse(span[..index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double magnitude))
            {
                return false;
            }

            span = span[index..];
            int unitLength = 0;
            while (unitLength < span.Length && !char.IsAsciiDigit(span[unitLength]) && span[unitLength] != '.')
            {
                unitLength++;
            }

            if (unitLength == 0)
            {
                return false;
            }

            if (!UnitMilliseconds.TryGetValue(span[..unitLength].ToString(), out double factor))
            {
                return false;
            }

            span = span[unitLength..];
            totalMilliseconds += magnitude * factor;
        }

        result = TimeSpan.FromMilliseconds(negative ? -totalMilliseconds : totalMilliseconds);
        return true;
    }
}
