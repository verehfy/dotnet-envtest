using System.CommandLine;

using Kubernetes.EnvTest;
using Kubernetes.EnvTest.Provisioning;
using Kubernetes.EnvTest.Tool;

using Microsoft.Extensions.Logging;

var versionArgument = new Argument<string?>("version")
{
    Description = "Kubernetes version to provision: exact (1.31.0), a release series (1.31), or 'latest' (default).",
    Arity = ArgumentArity.ZeroOrOne,
};

var binDirOption = new Option<string?>("--bin-dir")
{
    Description = "Store directory for the binaries. Defaults to the setup-envtest-compatible per-user store.",
};

var indexOption = new Option<string>("--index")
{
    Description = "URL of the envtest-releases.yaml index.",
    DefaultValueFactory = _ => ReleaseIndex.DefaultIndexUrl,
};

var osOption = new Option<string?>("--os")
{
    Description = "Target operating system (linux, darwin, windows). Defaults to the current OS.",
};

var archOption = new Option<string?>("--arch")
{
    Description = "Target architecture (amd64, arm64). Defaults to the current architecture.",
};

var printOption = new Option<PrintFormat>("--print", "-p")
{
    Description = "What to write to stdout: an overview, just the assets path, or an env-file line.",
    DefaultValueFactory = _ => PrintFormat.Overview,
};

var quietOption = new Option<bool>("--quiet", "-q")
{
    Description = "Suppress progress logging on stderr.",
};

var useCommand = new Command(
    "use",
    "Resolve a Kubernetes version, download the envtest binaries (kube-apiserver, etcd, kubectl) when missing, and print where they are.")
{
    versionArgument,
    binDirOption,
    indexOption,
    osOption,
    archOption,
    printOption,
    quietOption,
};

useCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string? versionRequest = parseResult.GetValue(versionArgument);
    string? binDir = parseResult.GetValue(binDirOption);
    string index = parseResult.GetValue(indexOption)!;
    string? os = parseResult.GetValue(osOption);
    string? arch = parseResult.GetValue(archOption);
    PrintFormat print = parseResult.GetValue(printOption);
    bool quiet = parseResult.GetValue(quietOption);

    ReleasePlatform current = ReleasePlatform.Current;
    var platform = new ReleasePlatform(os ?? current.OperatingSystem, arch ?? current.Architecture);

    var options = new BinaryProvisionerOptions
    {
        Version = versionRequest,
        IndexUrl = index,
        StoreDirectory = binDir,
        Platform = platform,
    };

    var provisioner = new BinaryProvisioner(
        options,
        new StderrLogger<BinaryProvisioner>(quiet ? LogLevel.None : LogLevel.Information));

    try
    {
        EnvTestBinaries binaries = await provisioner.EnsureBinariesAsync(cancellationToken).ConfigureAwait(false);
        switch (print)
        {
            case PrintFormat.Path:
                Console.WriteLine(binaries.Directory);
                break;
            case PrintFormat.Env:
                Console.WriteLine($"export KUBEBUILDER_ASSETS='{binaries.Directory}'");
                break;
            default:
                Console.WriteLine($"Version: v{binaries.Version}");
                Console.WriteLine($"OS/Arch: {binaries.Platform}");
                Console.WriteLine($"Path: {binaries.Directory}");
                break;
        }

        return 0;
    }
    catch (Exception ex) when (ex is EnvTestException or FormatException)
    {
        Console.Error.WriteLine($"setup-envtest: {ex.Message}");
        return 1;
    }
});

var rootCommand = new RootCommand(
    "Manages the Kubernetes control-plane binaries used by Kubernetes.EnvTest. "
    + "Shares its on-disk store and release index with the upstream Go setup-envtest tool.")
{
    useCommand,
};

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

/// <summary>The stdout formats of the <c>use</c> command.</summary>
internal enum PrintFormat
{
    /// <summary>Human-readable version, platform and path lines.</summary>
    Overview,

    /// <summary>Only the assets directory path.</summary>
    Path,

    /// <summary>A POSIX shell export line for KUBEBUILDER_ASSETS.</summary>
    Env,
}
