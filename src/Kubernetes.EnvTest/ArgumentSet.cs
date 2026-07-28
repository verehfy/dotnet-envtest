namespace Kubernetes.EnvTest;

/// <summary>
/// Structured, overridable command-line flags for a control-plane process,
/// mirroring upstream envtest's <c>process.Arguments</c>. Each flag may append
/// to or replace the built-in defaults, be forced to name-only form
/// (<c>--flag</c>), or be suppressed entirely.
/// </summary>
public sealed class ArgumentSet
{
    private const int ModeDefaulted = 0;
    private const int ModeUser = 1;
    private const int ModeDontPass = 2;
    private const int ModePassAsName = 3;

    private readonly Dictionary<string, (int Mode, List<string> Values)> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// Appends values to a flag. When the flag has not been configured yet, the
    /// rendered output will start from the defaults and add these values.
    /// Multiple values render as <c>--key=v1 --key=v2 ...</c>.
    /// </summary>
    /// <param name="key">The flag name without leading dashes.</param>
    /// <param name="values">The values to append.</param>
    /// <returns>This instance, for chaining.</returns>
    public ArgumentSet Append(string key, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(values);

        if (_values.TryGetValue(key, out (int Mode, List<string> Values) existing) && existing.Mode is ModeDefaulted or ModeUser)
        {
            existing.Values.AddRange(values);
            _values[key] = existing;
        }
        else if (existing.Mode == ModePassAsName)
        {
            // Appending to a name-only flag keeps it name-only, matching upstream.
        }
        else
        {
            // Not set yet, or previously suppressed: appending to a suppressed
            // flag turns it into user values (ignoring defaults), as upstream does.
            int mode = existing.Mode == ModeDontPass ? ModeUser : ModeDefaulted;
            _values[key] = (mode, [.. values]);
        }

        return this;
    }

    /// <summary>
    /// Appends values to a flag without ever including the built-in defaults
    /// for that flag in the rendered output.
    /// </summary>
    /// <param name="key">The flag name without leading dashes.</param>
    /// <param name="values">The values to append.</param>
    /// <returns>This instance, for chaining.</returns>
    public ArgumentSet AppendNoDefaults(string key, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(values);

        if (_values.TryGetValue(key, out (int Mode, List<string> Values) existing) && existing.Mode is ModeUser or ModeDefaulted)
        {
            // Matching upstream: once a flag exists, AppendNoDefaults behaves
            // like Append and preserves how the flag tracks its defaults.
            existing.Values.AddRange(values);
            _values[key] = existing;
        }
        else if (existing.Mode == ModePassAsName)
        {
            // Keep name-only.
        }
        else
        {
            _values[key] = (ModeUser, [.. values]);
        }

        return this;
    }

    /// <summary>Replaces a flag's values entirely, ignoring defaults.</summary>
    /// <param name="key">The flag name without leading dashes.</param>
    /// <param name="values">The values; an empty list renders the flag as <c>--key</c> only.</param>
    /// <returns>This instance, for chaining.</returns>
    public ArgumentSet Set(string key, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(values);
        _values[key] = (ModeUser, [.. values]);
        return this;
    }

    /// <summary>Renders the flag as name-only (<c>--key</c>).</summary>
    /// <param name="key">The flag name without leading dashes.</param>
    /// <returns>This instance, for chaining.</returns>
    public ArgumentSet Enable(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = (ModePassAsName, []);
        return this;
    }

    /// <summary>Prevents the flag from being rendered at all, even when it has a default.</summary>
    /// <param name="key">The flag name without leading dashes.</param>
    /// <returns>This instance, for chaining.</returns>
    public ArgumentSet Disable(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = (ModeDontPass, []);
        return this;
    }

    /// <summary>
    /// Renders the argument set to strings suitable for a process start,
    /// merging in the given defaults: defaults for unconfigured flags are
    /// emitted as-is; configured flags interact with their default according to
    /// how they were configured (append, replace, name-only, or suppress).
    /// Output is sorted by flag name for determinism.
    /// </summary>
    /// <param name="defaults">The per-flag default values, or <see langword="null"/> for none.</param>
    /// <returns>The rendered <c>--key=value</c> strings.</returns>
    public IReadOnlyList<string> AsStrings(IReadOnlyDictionary<string, IReadOnlyList<string>>? defaults = null)
    {
        defaults ??= new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var keys = new SortedSet<string>(_values.Keys, StringComparer.Ordinal);
        keys.UnionWith(defaults.Keys);

        var result = new List<string>();
        foreach (string key in keys)
        {
            defaults.TryGetValue(key, out IReadOnlyList<string>? defaultValues);

            if (!_values.TryGetValue(key, out (int Mode, List<string> Values) entry))
            {
                AppendRendered(result, key, defaultValues ?? []);
                continue;
            }

            switch (entry.Mode)
            {
                case ModeDontPass:
                    break;
                case ModePassAsName:
                    result.Add("--" + key);
                    break;
                case ModeDefaulted:
                    AppendRendered(result, key, [.. defaultValues ?? [], .. entry.Values]);
                    break;
                default:
                    AppendRendered(result, key, entry.Values);
                    break;
            }
        }

        return result;
    }

    private static void AppendRendered(List<string> result, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            // A default with no values (or a flag fully cleared by the user)
            // renders as name-only, matching upstream's empty-slice semantics.
            result.Add("--" + key);
            return;
        }

        foreach (string value in values)
        {
            result.Add($"--{key}={value}");
        }
    }
}
