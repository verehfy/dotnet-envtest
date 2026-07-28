using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class ArgumentSetTests
{
    private static Dictionary<string, IReadOnlyList<string>> Defaults() => new(StringComparer.Ordinal)
    {
        ["some-flag"] = ["default-value"],
        ["other-flag"] = ["val1", "val2"],
    };

    [Fact]
    public void AsStrings_renders_defaults_for_unconfigured_flags()
    {
        var arguments = new ArgumentSet();

        var rendered = arguments.AsStrings(Defaults());

        Assert.Equal(["--other-flag=val1", "--other-flag=val2", "--some-flag=default-value"], rendered);
    }

    [Fact]
    public void Append_adds_values_on_top_of_defaults()
    {
        var arguments = new ArgumentSet().Append("some-flag", "extra");

        var rendered = arguments.AsStrings(Defaults());

        Assert.Contains("--some-flag=default-value", rendered);
        Assert.Contains("--some-flag=extra", rendered);
    }

    [Fact]
    public void AppendNoDefaults_suppresses_defaults_for_that_flag()
    {
        var arguments = new ArgumentSet().AppendNoDefaults("some-flag", "only-this");

        var rendered = arguments.AsStrings(Defaults());

        Assert.Contains("--some-flag=only-this", rendered);
        Assert.DoesNotContain("--some-flag=default-value", rendered);
    }

    [Fact]
    public void Set_replaces_defaults_entirely()
    {
        var arguments = new ArgumentSet().Set("other-flag", "replacement");

        var rendered = arguments.AsStrings(Defaults());

        Assert.Contains("--other-flag=replacement", rendered);
        Assert.DoesNotContain("--other-flag=val1", rendered);
    }

    [Fact]
    public void Disable_removes_the_flag_even_with_defaults()
    {
        var arguments = new ArgumentSet().Disable("some-flag");

        var rendered = arguments.AsStrings(Defaults());

        Assert.DoesNotContain(rendered, arg => arg.StartsWith("--some-flag", StringComparison.Ordinal));
    }

    [Fact]
    public void Enable_renders_flag_as_name_only()
    {
        var arguments = new ArgumentSet().Enable("v");

        var rendered = arguments.AsStrings(null);

        Assert.Equal(["--v"], rendered);
    }

    [Fact]
    public void Append_after_disable_starts_from_user_values()
    {
        var arguments = new ArgumentSet().Disable("some-flag").Append("some-flag", "revived");

        var rendered = arguments.AsStrings(Defaults());

        Assert.Equal(["--other-flag=val1", "--other-flag=val2", "--some-flag=revived"], rendered);
    }

    [Fact]
    public void Output_is_sorted_by_flag_name_for_determinism()
    {
        var arguments = new ArgumentSet().Set("zeta", "1").Set("alpha", "2");

        var rendered = arguments.AsStrings(null);

        Assert.Equal(["--alpha=2", "--zeta=1"], rendered);
    }

    [Fact]
    public void Empty_default_value_list_renders_name_only()
    {
        var defaults = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["bare"] = [],
        };

        var rendered = new ArgumentSet().AsStrings(defaults);

        Assert.Equal(["--bare"], rendered);
    }
}
