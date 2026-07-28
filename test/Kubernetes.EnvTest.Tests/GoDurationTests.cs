using Kubernetes.EnvTest.Internal;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class GoDurationTests
{
    [Theory]
    [InlineData("20s", 20_000)]
    [InlineData("300ms", 300)]
    [InlineData("1m30s", 90_000)]
    [InlineData("2h", 7_200_000)]
    [InlineData("1.5s", 1_500)]
    [InlineData("0", 0)]
    [InlineData("-10s", -10_000)]
    [InlineData("1h2m3s", 3_723_000)]
    public void Parse_handles_go_duration_syntax(string input, double expectedMilliseconds)
    {
        var duration = GoDuration.Parse(input);

        Assert.Equal(expectedMilliseconds, duration.TotalMilliseconds, precision: 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("20")]
    [InlineData("s")]
    [InlineData("20x")]
    [InlineData("twenty seconds")]
    public void Parse_rejects_invalid_durations(string input)
    {
        Assert.Throws<FormatException>(() => GoDuration.Parse(input));
    }
}
