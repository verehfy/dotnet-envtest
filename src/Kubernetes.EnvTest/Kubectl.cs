using System.Diagnostics;

using Kubernetes.EnvTest.Internal;

namespace Kubernetes.EnvTest;

/// <summary>A thin wrapper around the <c>kubectl</c> binary.</summary>
public sealed class Kubectl
{
    /// <summary>
    /// Gets or sets the path of the <c>kubectl</c> binary. When unset it is
    /// resolved from <c>TEST_ASSET_KUBECTL</c>, <c>KUBEBUILDER_ASSETS</c>, or
    /// the default assets directory.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets the flags prepended to every invocation, for example a
    /// <c>--kubeconfig=...</c> pointing at the test control plane.
    /// </summary>
    public IList<string> Options { get; } = [];

    /// <summary>Runs kubectl with the preconfigured options plus the given arguments.</summary>
    /// <param name="arguments">The kubectl arguments, for example <c>get</c>, <c>pods</c>.</param>
    /// <param name="cancellationToken">Kills the process when cancelled.</param>
    /// <returns>The exit code together with captured stdout and stderr.</returns>
    /// <exception cref="ProcessStartException">The binary could not be launched.</exception>
    public async Task<KubectlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string path = Path ??= BinPathFinder.Find("kubectl", null);
        var startInfo = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string option in Options)
        {
            startInfo.ArgumentList.Add(option);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process();
        process.StartInfo = startInfo;
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ProcessStartException("kubectl", path, ex.Message, ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            throw;
        }

        return new KubectlResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    /// <summary>Runs kubectl with the given arguments; see <see cref="RunAsync(IReadOnlyList{string}, CancellationToken)"/>.</summary>
    /// <param name="arguments">The kubectl arguments.</param>
    public Task<KubectlResult> RunAsync(params string[] arguments) => RunAsync(arguments, CancellationToken.None);
}

/// <summary>The outcome of a <see cref="Kubectl"/> invocation.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
public sealed record KubectlResult(int ExitCode, string StandardOutput, string StandardError);
