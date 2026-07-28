using Kubernetes.EnvTest.Internal;

using Xunit;

namespace Kubernetes.EnvTest.Tests;

public class ManifestReaderTests
{
    [Fact]
    public void SplitDocuments_splits_on_separator_lines_only()
    {
        const string Content = """
            kind: A
            value: "--- not a separator inside a value"
            ---
            kind: B
            ---

            ---
            kind: C
            """;

        var documents = ManifestReader.SplitDocuments(Content).ToList();

        Assert.Equal(3, documents.Count);
        Assert.Contains("kind: A", documents[0], StringComparison.Ordinal);
        Assert.Contains("kind: B", documents[1], StringComparison.Ordinal);
        Assert.Contains("kind: C", documents[2], StringComparison.Ordinal);
    }

    [Fact]
    public void PeekType_reads_api_version_and_kind()
    {
        (string? apiVersion, string? kind) = ManifestReader.PeekType("""
            apiVersion: apiextensions.k8s.io/v1
            kind: CustomResourceDefinition
            metadata:
              name: widgets.example.com
            """);

        Assert.Equal("apiextensions.k8s.io/v1", apiVersion);
        Assert.Equal("CustomResourceDefinition", kind);
    }

    [Fact]
    public void ReadDocuments_skips_non_manifest_extensions()
    {
        string directory = Directory.CreateTempSubdirectory("manifest-reader-tests-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "crd.yaml"), "kind: A");
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "kind: B");
            File.WriteAllText(Path.Combine(directory, "crd.json"), """{"kind": "C"}""");

            var documents = ManifestReader.ReadDocuments(directory).ToList();

            Assert.Equal(2, documents.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
