using System.Text.RegularExpressions;

using k8s;

namespace Kubernetes.EnvTest.Internal;

/// <summary>Reads multi-document YAML/JSON Kubernetes manifest files.</summary>
internal static partial class ManifestReader
{
    private static readonly HashSet<string> ManifestExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json",
        ".yaml",
        ".yml",
    };

    /// <summary>
    /// Expands a path (file or directory) into manifest files, then splits each
    /// file into YAML documents. Non-manifest extensions are skipped.
    /// </summary>
    internal static IEnumerable<string> ReadDocuments(string path)
    {
        string[] files;
        if (Directory.Exists(path))
        {
            files = [.. Directory.EnumerateFiles(path).Order(StringComparer.Ordinal)];
        }
        else
        {
            files = [path];
        }

        foreach (string file in files)
        {
            if (!ManifestExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            foreach (string document in SplitDocuments(File.ReadAllText(file)))
            {
                yield return document;
            }
        }
    }

    /// <summary>Splits a YAML stream on <c>---</c> separator lines, dropping empty documents.</summary>
    internal static IEnumerable<string> SplitDocuments(string content)
    {
        foreach (string document in DocumentSeparatorPattern().Split(content))
        {
            if (!string.IsNullOrWhiteSpace(document))
            {
                yield return document;
            }
        }
    }

    /// <summary>Reads the apiVersion/kind of a document without fully deserializing it.</summary>
    internal static (string? ApiVersion, string? Kind) PeekType(string document)
    {
        try
        {
            KubernetesObject metadata = KubernetesYaml.Deserialize<KubernetesObject>(document);
            return (metadata?.ApiVersion, metadata?.Kind);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    [GeneratedRegex(@"^---\s*$", RegexOptions.Multiline)]
    private static partial Regex DocumentSeparatorPattern();
}
