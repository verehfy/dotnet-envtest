namespace Kubernetes.EnvTest;

/// <summary>An API group / kind pair, for example (<c>example.com</c>, <c>Widget</c>).</summary>
/// <param name="Group">The API group; empty for the core group.</param>
/// <param name="Kind">The kind name.</param>
public readonly record struct GroupKind(string Group, string Kind)
{
    /// <inheritdoc/>
    public override string ToString() => Group.Length == 0 ? Kind : $"{Kind}.{Group}";
}
