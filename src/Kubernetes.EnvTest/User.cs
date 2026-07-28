namespace Kubernetes.EnvTest;

/// <summary>A Kubernetes user to provision for authentication purposes.</summary>
/// <param name="Name">The user name.</param>
/// <param name="Groups">The groups the user belongs to, for example <c>system:masters</c>.</param>
public sealed record User(string Name, IReadOnlyList<string> Groups)
{
    /// <summary>Initializes a new instance of the <see cref="User"/> record with no groups.</summary>
    /// <param name="name">The user name.</param>
    public User(string name)
        : this(name, [])
    {
    }
}
