using System.Text.Json.Serialization;

internal sealed class ClientBuildException : InvalidOperationException
{
    internal ClientBuildException(string message)
        : base(message)
    {
    }
}

internal sealed class ClientBuildOptions
{
    internal ClientBuildOptions(string sourceExecutablePath, string runtimeStageDirectory, string outputDirectory, bool deployIntoExistingDirectory, bool cleanOutput, bool verbose)
    {
        SourceExecutablePath = Path.GetFullPath(sourceExecutablePath);
        RuntimeStageDirectory = Path.GetFullPath(runtimeStageDirectory);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        DeployIntoExistingDirectory = deployIntoExistingDirectory;
        CleanOutput = cleanOutput;
        Verbose = verbose;
    }

    internal string SourceExecutablePath { get; }
    internal string RuntimeStageDirectory { get; }
    internal string OutputDirectory { get; }
    internal bool DeployIntoExistingDirectory { get; }
    internal bool CleanOutput { get; }
    internal bool Verbose { get; }
}

internal sealed class ClientBuildResult
{
    internal ClientBuildResult(string outputDirectory, ClientBuildManifest manifest)
    {
        OutputDirectory = outputDirectory;
        Manifest = manifest;
    }

    internal string OutputDirectory { get; }
    internal ClientBuildManifest Manifest { get; }
}

internal sealed class ClientBuildManifest
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("sourceTerrariaVersion")]
    public string SourceTerrariaVersion { get; set; } = string.Empty;

    [JsonPropertyName("sourceTerrariaSha256")]
    public string SourceTerrariaSha256 { get; set; } = string.Empty;

    [JsonPropertyName("outputExecutableSha256")]
    public string OutputExecutableSha256 { get; set; } = string.Empty;

    [JsonPropertyName("patchCatalogId")]
    public string PatchCatalogId { get; set; } = string.Empty;

    [JsonPropertyName("appliedPatches")]
    public List<string> AppliedPatches { get; set; } = new List<string>();

    [JsonPropertyName("bridgeHandshake")]
    public string BridgeHandshake { get; set; } = string.Empty;

    [JsonPropertyName("buildConfiguration")]
    public string BuildConfiguration { get; set; } = string.Empty;

    [JsonPropertyName("runtimeFiles")]
    public List<ClientBuildFile> RuntimeFiles { get; set; } = new List<ClientBuildFile>();

    [JsonPropertyName("sourceRevision")]
    public string? SourceRevision { get; set; }
}

internal sealed class ClientBuildFile
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}
