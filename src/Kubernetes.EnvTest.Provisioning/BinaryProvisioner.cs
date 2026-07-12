using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kubernetes.EnvTest.Provisioning;

/// <summary>
/// Default <see cref="IBinaryProvisioner"/>: resolves versions against the
/// <c>envtest-releases.yaml</c> index, downloads archives over HTTPS, verifies
/// their SHA-512 checksum, and extracts them into the setup-envtest-compatible
/// binary store.
/// </summary>
public sealed partial class BinaryProvisioner : IBinaryProvisioner
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly BinaryProvisionerOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<BinaryProvisioner> _logger;

    /// <summary>Initializes a new instance of the <see cref="BinaryProvisioner"/> class.</summary>
    /// <param name="options">The provisioning options; defaults to latest stable version in the shared store.</param>
    /// <param name="logger">Diagnostic logger; defaults to a no-op logger.</param>
    /// <param name="httpClient">The HTTP client used for the index and archive downloads; a shared internal client is used when omitted.</param>
    public BinaryProvisioner(
        BinaryProvisionerOptions? options = null,
        ILogger<BinaryProvisioner>? logger = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? new BinaryProvisionerOptions();
        _logger = logger ?? NullLogger<BinaryProvisioner>.Instance;
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <summary>Initializes a new instance of the <see cref="BinaryProvisioner"/> class for dependency injection.</summary>
    /// <param name="options">The provisioning options.</param>
    /// <param name="logger">Diagnostic logger.</param>
    public BinaryProvisioner(IOptions<BinaryProvisionerOptions> options, ILogger<BinaryProvisioner> logger)
        : this(options?.Value, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    /// <inheritdoc/>
    public async Task<EnvTestBinaries> EnsureBinariesAsync(CancellationToken cancellationToken = default)
    {
        KubernetesVersionSpec spec = KubernetesVersionSpec.Parse(_options.Version);
        ReleasePlatform platform = _options.Platform ?? ReleasePlatform.Current;
        string storeRoot = _options.StoreDirectory ?? BinaryStore.GetDefaultDirectory();

        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["KubernetesVersion"] = spec.ToString(),
            ["Platform"] = platform.ToString(),
        });

        ReleaseIndex? index = null;
        KubernetesVersion version;
        if (spec.Exact is not null)
        {
            version = spec.Exact;
        }
        else
        {
            // Series and "latest" requests need the index before we can even
            // know which store directory to probe.
            index = await GetReleaseIndexAsync(cancellationToken).ConfigureAwait(false);
            version = index.Resolve(spec, _options.IndexUrl);
            LogResolvedVersion(spec, version);
        }

        EnvTestBinaries? cached = BinaryStore.TryGetBinaries(storeRoot, version, platform);
        if (cached is not null)
        {
            LogUsingCachedBinaries(version, platform, cached.Directory);
            return cached;
        }

        index ??= await GetReleaseIndexAsync(cancellationToken).ConfigureAwait(false);
        ReleaseArchive archive = index.GetArchive(version, platform, _options.IndexUrl);

        string directory = BinaryStore.GetVersionDirectory(storeRoot, version, platform);
        await DownloadAndExtractAsync(archive, directory, platform, cancellationToken).ConfigureAwait(false);

        EnvTestBinaries binaries = BinaryStore.TryGetBinaries(storeRoot, version, platform)
            ?? throw new BinaryDownloadException(
                archive.DownloadUrl,
                $"archive did not contain the expected kube-apiserver, etcd and kubectl binaries (extracted to '{directory}').");

        return binaries;
    }

    /// <inheritdoc/>
    public async Task<ReleaseIndex> GetReleaseIndexAsync(CancellationToken cancellationToken = default)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        string body;
        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(new Uri(_options.IndexUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ReleaseIndexException(_options.IndexUrl, $"got status {(int)response.StatusCode} ({response.StatusCode}).");
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReleaseIndexException(_options.IndexUrl, ex.Message, ex);
        }

        ReleaseIndex index;
        try
        {
            index = ReleaseIndex.Parse(body);
        }
        catch (Exception ex) when (ex is FormatException or YamlDotNet.Core.YamlException)
        {
            throw new ReleaseIndexException(_options.IndexUrl, $"unable to parse index: {ex.Message}", ex);
        }

        if (_options.LogTimings)
        {
            long elapsedMilliseconds = GetElapsedMilliseconds(startTimestamp);
            LogFetchedIndexTimed(_options.IndexUrl, elapsedMilliseconds);
        }
        else
        {
            LogFetchedIndex(_options.IndexUrl);
        }

        return index;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kubernetes.EnvTest.Provisioning");
        return client;
    }

    private static long GetElapsedMilliseconds(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private async Task DownloadAndExtractAsync(
        ReleaseArchive archive,
        string directory,
        ReleasePlatform platform,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);

        // Download to a temporary file first so a half-written archive can
        // never corrupt the store, verifying the SHA-512 hash as we stream.
        string tempArchivePath = Path.Combine(directory, $".{archive.Name}.{System.Environment.ProcessId}.tmp");
        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await DownloadToFileAsync(archive, tempArchivePath, cancellationToken).ConfigureAwait(false);
            if (_options.LogTimings)
            {
                long downloadMilliseconds = GetElapsedMilliseconds(startTimestamp);
                LogDownloadedArchive(archive.Name, downloadMilliseconds);
            }

            startTimestamp = Stopwatch.GetTimestamp();
            await ExtractArchiveAsync(tempArchivePath, directory, platform, cancellationToken).ConfigureAwait(false);
            if (_options.LogTimings)
            {
                long extractMilliseconds = GetElapsedMilliseconds(startTimestamp);
                LogExtractedArchive(archive.Name, extractMilliseconds);
            }
        }
        finally
        {
            File.Delete(tempArchivePath);
        }
    }

    private async Task DownloadToFileAsync(ReleaseArchive archive, string targetPath, CancellationToken cancellationToken)
    {
        LogDownloadingArchive(archive.Name, archive.DownloadUrl);

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(new Uri(archive.DownloadUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new BinaryDownloadException(
                    archive.DownloadUrl,
                    $"got status {(int)response.StatusCode} ({response.StatusCode}).");
            }

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            using (FileStream output = File.Create(targetPath))
            {
                using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            string actualHash = Convert.ToHexStringLower(hasher.GetHashAndReset());
            if (!actualHash.Equals(archive.Sha512Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new ChecksumMismatchException(archive.Name, archive.Sha512Hash, actualHash);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new BinaryDownloadException(archive.DownloadUrl, ex.Message, ex);
        }
        catch (IOException ex)
        {
            throw new BinaryDownloadException(archive.DownloadUrl, ex.Message, ex);
        }
    }

    private async Task ExtractArchiveAsync(
        string archivePath,
        string directory,
        ReleasePlatform platform,
        CancellationToken cancellationToken)
    {
        using FileStream archiveStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        while (await tarReader.GetNextEntryAsync(cancellationToken: cancellationToken).ConfigureAwait(false) is { } entry)
        {
            if (entry.EntryType is not TarEntryType.RegularFile and not TarEntryType.V7RegularFile)
            {
                continue;
            }

            // Flatten the archive's internal directory layout (matching
            // upstream), and cap permissions at r-x.
            string fileName = Path.GetFileName(entry.Name.Replace('\\', '/').TrimEnd('/'));
            if (fileName.Length == 0)
            {
                continue;
            }

            string targetPath = Path.Combine(directory, fileName);
            FileStreamOptions createOptions = new()
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!platform.IsWindows && !System.OperatingSystem.IsWindows())
            {
                createOptions.UnixCreateMode =
                    (UnixFileMode)((int)(UnixFileMode.UserRead | UnixFileMode.UserExecute
                                         | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                         | UnixFileMode.OtherRead | UnixFileMode.OtherExecute)
                                   & (int)entry.Mode);
            }

            try
            {
                using FileStream output = new(targetPath, createOptions);
                if (entry.DataStream is not null)
                {
                    await entry.DataStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                // Another process extracted this file concurrently; keep theirs.
                LogSkippedConcurrentFile(fileName);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolved Kubernetes version request {VersionRequest} to {KubernetesVersion}")]
    private partial void LogResolvedVersion(KubernetesVersionSpec versionRequest, KubernetesVersion kubernetesVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Using cached envtest binaries for Kubernetes {KubernetesVersion} ({Platform}) from {Directory}")]
    private partial void LogUsingCachedBinaries(KubernetesVersion kubernetesVersion, ReleasePlatform platform, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched release index from {IndexUrl} in {ElapsedMilliseconds} ms")]
    private partial void LogFetchedIndexTimed(string indexUrl, long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched release index from {IndexUrl}")]
    private partial void LogFetchedIndex(string indexUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading envtest binaries archive {ArchiveName} from {Url}")]
    private partial void LogDownloadingArchive(string archiveName, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded {ArchiveName} in {ElapsedMilliseconds} ms")]
    private partial void LogDownloadedArchive(string archiveName, long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {ArchiveName} in {ElapsedMilliseconds} ms")]
    private partial void LogExtractedArchive(string archiveName, long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping {FileName}: created concurrently by another process")]
    private partial void LogSkippedConcurrentFile(string fileName);
}
