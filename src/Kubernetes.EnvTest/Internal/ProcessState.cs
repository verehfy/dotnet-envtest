using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Kubernetes.EnvTest.Internal;

/// <summary>
/// Manages the lifecycle of one control-plane child process: start, readiness
/// health-checking, version-adaptive flag discovery, and platform-safe
/// teardown (SIGTERM with a kill fallback on Unix; process-tree kill on
/// Windows, which has no equivalent of SIGTERM for console children).
/// </summary>
internal sealed partial class ProcessState
{
    private static readonly HttpClient HealthCheckClient = new(new SocketsHttpHandler
    {
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            // The control plane serves with a freshly generated self-signed CA
            // on 127.0.0.1; readiness checks intentionally skip verification,
            // as upstream does.
#pragma warning disable CA5359 // Intentional for local-only health checks.
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
#pragma warning restore CA5359
        },
    })
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private Process? _process;
    private Task<int>? _exitTask;
    private bool _ready;

    /// <summary>Gets or sets the full path of the executable.</summary>
    public required string Path { get; set; }

    /// <summary>Gets or sets the process arguments.</summary>
    public IReadOnlyList<string> Args { get; set; } = [];

    /// <summary>Gets or sets the readiness health-check URL; a 200 response marks the process ready.</summary>
    public Uri? HealthCheckUri { get; set; }

    /// <summary>Gets or sets the health-check poll interval; defaults to 100 ms.</summary>
    public TimeSpan HealthCheckPollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets or sets the maximum time to wait for readiness.</summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Gets or sets the maximum time to wait for graceful termination.</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Gets or sets the working directory holding certificates or data; created as a temp directory when unset.</summary>
    public string? Dir { get; set; }

    /// <summary>Gets a value indicating whether <see cref="Dir"/> was created by this instance and is removed on stop.</summary>
    public bool DirNeedsCleaning { get; private set; }

    /// <summary>Gets a value indicating whether the process has exited.</summary>
    public bool HasExited => _exitTask is { IsCompleted: true };

    /// <summary>Defaults the working directory (creating a temp directory when unset) and timeout fields.</summary>
    public void Initialize()
    {
        if (string.IsNullOrEmpty(Dir))
        {
            Dir = Directory.CreateTempSubdirectory("k8s_test_framework_").FullName;
            DirNeedsCleaning = true;
        }
        else
        {
            Directory.CreateDirectory(Dir);
        }

        if (StartTimeout <= TimeSpan.Zero)
        {
            StartTimeout = TimeSpan.FromSeconds(20);
        }

        if (StopTimeout <= TimeSpan.Zero)
        {
            StopTimeout = TimeSpan.FromSeconds(20);
        }
    }

    /// <summary>
    /// Checks the executable's <c>--help</c> output for the presence of a flag,
    /// used for version-adaptive argument construction (for example
    /// <c>--insecure-port</c> disappeared in Kubernetes 1.24).
    /// </summary>
    /// <param name="flag">The flag name without leading dashes.</param>
    /// <param name="cancellationToken">Cancels waiting for the help output.</param>
    public async Task<bool> CheckFlagAsync(string flag, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(Path, "--help")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process();
        process.StartInfo = startInfo;
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ProcessStartException(
                System.IO.Path.GetFileName(Path),
                Path,
                $"unable to run '{Path} --help' to check for flag '--{flag}': {ex.Message}",
                ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string combined = await stdoutTask.ConfigureAwait(false) + await stderrTask.ConfigureAwait(false);

        return Regex.IsMatch(combined, $@"(?m)^\s*--{Regex.Escape(flag)}\b");
    }

    /// <summary>
    /// Starts the process and waits until its health check reports 200 OK.
    /// </summary>
    /// <param name="stdout">Optional writer receiving the child's stdout.</param>
    /// <param name="stderr">Optional writer receiving the child's stderr.</param>
    /// <param name="cancellationToken">Cancels the wait (the child is killed).</param>
    /// <exception cref="ProcessStartException">The process failed to start, exited early, or missed its readiness deadline.</exception>
    public async Task StartAsync(TextWriter? stdout, TextWriter? stderr, CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }

        string processName = System.IO.Path.GetFileName(Path);
        var startInfo = new ProcessStartInfo(Path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = stdout is not null,
            RedirectStandardError = stderr is not null,
        };
        foreach (string arg in Args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new ProcessStartException(processName, Path, "process reuse reported by the runtime.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new ProcessStartException(processName, Path, ex.Message, ex);
        }

        _process = process;
        if (stdout is not null)
        {
            _ = PumpAsync(process.StandardOutput, stdout);
        }

        if (stderr is not null)
        {
            _ = PumpAsync(process.StandardError, stderr);
        }

        _exitTask = WaitForExitCodeAsync(process);

        using var pollerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task readyTask = PollHealthCheckUntilOkAsync(pollerCancellation.Token);
        Task timeoutTask = Task.Delay(StartTimeout, pollerCancellation.Token);

        Task completed = await Task.WhenAny(readyTask, _exitTask, timeoutTask).ConfigureAwait(false);
        if (completed == readyTask && readyTask.IsCompletedSuccessfully)
        {
            await pollerCancellation.CancelAsync().ConfigureAwait(false);
            _ready = true;
            return;
        }

        await pollerCancellation.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (completed == _exitTask)
        {
            throw new ProcessStartException(
                processName,
                Path,
                "the process exited before becoming ready (it may have failed to start, or stopped unexpectedly); "
                + "enable AttachControlPlaneOutput to see its output.");
        }

        // Timed out: try to tear the process down, mirroring upstream.
        TerminateGracefully();
        throw new ProcessStartException(
            processName,
            Path,
            $"timed out after {StartTimeout.TotalSeconds:F0}s waiting for the process to become ready.");
    }

    /// <summary>
    /// Stops the process: SIGTERM on Unix (falling back to a process-tree kill
    /// after <see cref="StopTimeout"/>), or an immediate process-tree kill on
    /// Windows. Cleans up the temp working directory when owned.
    /// </summary>
    /// <exception cref="ProcessStopException">The process could not be signalled or missed the stop deadline.</exception>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_process is null || _exitTask is null || HasExited)
            {
                return;
            }

            string processName = System.IO.Path.GetFileName(Path);
            try
            {
                TerminateGracefully();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                if (!_process.HasExited)
                {
                    throw new ProcessStopException(processName, Path, $"unable to signal the process to stop: {ex.Message}", ex);
                }
            }

            Task timeoutTask = Task.Delay(StopTimeout, cancellationToken);
            Task completed = await Task.WhenAny(_exitTask, timeoutTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed != _exitTask)
            {
                KillTree();
                throw new ProcessStopException(
                    processName,
                    Path,
                    $"timed out after {StopTimeout.TotalSeconds:F0}s waiting for the process to stop; it was forcibly killed.");
            }

            _ready = false;
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            if (DirNeedsCleaning && Dir is not null)
            {
                try
                {
                    Directory.Delete(Dir, recursive: true);
                }
                catch (IOException)
                {
                    // Best effort, matching upstream's ignored os.RemoveAll error.
                }
                catch (UnauthorizedAccessException)
                {
                    // Windows can transiently hold locks on just-exited children's files.
                }
            }
        }
    }

    private static async Task<int> WaitForExitCodeAsync(Process process)
    {
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, TextWriter writer)
    {
        try
        {
            char[] buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                await writer.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
            // The process exited and streams were torn down.
        }
        catch (IOException)
        {
            // Broken pipe on teardown.
        }
    }

    private void TerminateGracefully()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows has no SIGTERM equivalent for arbitrary console
            // processes; kill the whole tree so no etcd/apiserver children
            // outlive the test run.
            _process.Kill(entireProcessTree: true);
        }
        else
        {
            SendSigterm(_process.Id);
        }
    }

    private void KillTree()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the kill.
        }
    }

    private static void SendSigterm(int processId)
    {
        const int Sigterm = 15;
        if (Kill(processId, Sigterm) != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            // ESRCH (3): the process already exited.
            if (error != 3)
            {
                throw new System.ComponentModel.Win32Exception(error);
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);

    private async Task PollHealthCheckUntilOkAsync(CancellationToken cancellationToken)
    {
        if (HealthCheckUri is null)
        {
            throw new InvalidOperationException("HealthCheckUri must be configured before starting the process.");
        }

        TimeSpan interval = HealthCheckPollInterval > TimeSpan.Zero
            ? HealthCheckPollInterval
            : TimeSpan.FromMilliseconds(100);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await HealthCheckClient
                    .GetAsync(HealthCheckUri, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not up yet.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Per-request timeout; keep polling.
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }
}
