using System.Text.Json;
using Mono.Cecil;

/// <summary>Builds a complete client only from a clean audited source and one coherent staged runtime.</summary>
internal static class ClientBuildPipeline
{
    private const string ClientManifestName = "alacrity-client-manifest.json";

    internal static ClientBuildResult Generate(ClientBuildOptions options)
    {
        var source = SupportedTerrariaBuildCatalog.ValidateSource(options.SourceExecutablePath);
        var stage = RuntimeStage.Load(options.RuntimeStageDirectory);
        var handshake = BridgeAbiCatalog.ValidateRuntimeFacade(stage.Directory, source);
        ValidateOutput(options);

        var temporaryDirectory = CreateTemporaryDirectory(options.OutputDirectory);
        try
        {
            if (!options.DeployIntoExistingDirectory)
            {
                CopyVanillaDistribution(options.SourceExecutablePath, temporaryDirectory, options.OutputDirectory, options.Verbose);
            }

            var temporarySource = Path.Combine(temporaryDirectory, "Terraria.exe");
            File.Copy(options.SourceExecutablePath, temporarySource, overwrite: true);
            stage.CopyTo(temporaryDirectory);
            var temporaryOutput = Path.Combine(temporaryDirectory, "Alacrity.exe");

            List<ClientPatchResult> patchResults;
            using (var resolver = Program.CreateResolver(temporarySource))
            using (var module = ModuleDefinition.ReadModule(temporarySource, new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = false,
                ReadingMode = ReadingMode.Deferred
            }))
            {
                patchResults = PermanentPatchCatalog.ApplyAll(module, temporarySource);
                module.Write(temporaryOutput);
            }

            using (var resolver = Program.CreateResolver(temporaryOutput))
            using (var patched = ModuleDefinition.ReadModule(temporaryOutput, new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = false,
                ReadingMode = ReadingMode.Deferred
            }))
            {
                BridgeAbiCatalog.ValidatePatchedExecutable(patched, temporaryDirectory);
            }

            var manifest = new ClientBuildManifest
            {
                SourceTerrariaVersion = source.Version,
                SourceTerrariaSha256 = source.Sha256,
                OutputExecutableSha256 = SupportedTerrariaBuildCatalog.ComputeSha256(temporaryOutput),
                PatchCatalogId = source.PatchCatalogId,
                BridgeHandshake = handshake,
                BuildConfiguration = stage.BuildConfiguration,
                SourceRevision = GetSourceRevision(),
                RuntimeFiles = new List<ClientBuildFile>(stage.Files)
            };
            for (var index = 0; index < patchResults.Count; index++)
            {
                manifest.AppliedPatches.Add(patchResults[index].PatchId);
            }

            WriteClientManifest(temporaryDirectory, manifest);
            Publish(temporaryDirectory, options, manifest);
            return new ClientBuildResult(options.OutputDirectory, manifest);
        }
        finally
        {
            if (System.IO.Directory.Exists(temporaryDirectory))
            {
                System.IO.Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static void ValidateOutput(ClientBuildOptions options)
    {
        if (PathsEqual(options.SourceExecutablePath, Path.Combine(options.OutputDirectory, "Terraria.exe")))
        {
            throw new ClientBuildException("The clean Terraria.exe source cannot be the pipeline output Terraria.exe.");
        }

        if (!options.DeployIntoExistingDirectory)
        {
            var artifactsRoot = Path.Combine(Environment.CurrentDirectory, "artifacts");
            if (!IsSubdirectory(options.OutputDirectory, artifactsRoot))
            {
                throw new ClientBuildException("Non-deployment output must remain under artifacts. Use --deploy only for an explicit client directory.");
            }

            return;
        }

        if (!System.IO.Directory.Exists(options.OutputDirectory))
        {
            throw new ClientBuildException("Deployment output directory must already exist: " + options.OutputDirectory);
        }
    }

    private static string CreateTemporaryDirectory(string outputDirectory)
    {
        var parent = Path.GetDirectoryName(outputDirectory);
        if (string.IsNullOrEmpty(parent))
        {
            parent = Environment.CurrentDirectory;
        }

        System.IO.Directory.CreateDirectory(parent);
        var temporaryDirectory = Path.Combine(parent, ".alacrity-client-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temporaryDirectory);
        return temporaryDirectory;
    }

    private static void CopyVanillaDistribution(string sourceExecutablePath, string temporaryDirectory, string outputDirectory, bool verbose)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceExecutablePath)!;
        var excludedPrefixes = new List<string>();
        AddExcludedPrefix(sourceDirectory, outputDirectory, excludedPrefixes);
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot != null)
        {
            AddExcludedPrefix(sourceDirectory, repositoryRoot, excludedPrefixes);
        }

        foreach (var sourcePath in System.IO.Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            if (relativePath.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                IsExcluded(relativePath, excludedPrefixes))
            {
                continue;
            }

            var targetPath = Path.Combine(temporaryDirectory, relativePath);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
            if (verbose)
            {
                Console.WriteLine("Copied vanilla file: " + relativePath);
            }
        }
    }

    private static void Publish(string temporaryDirectory, ClientBuildOptions options, ClientBuildManifest manifest)
    {
        if (!options.DeployIntoExistingDirectory)
        {
            if (System.IO.Directory.Exists(options.OutputDirectory))
            {
                if (!options.CleanOutput && !File.Exists(Path.Combine(options.OutputDirectory, ClientManifestName)))
                {
                    throw new ClientBuildException("Refusing to replace a directory that is not pipeline-owned: " + options.OutputDirectory);
                }

                System.IO.Directory.Delete(options.OutputDirectory, recursive: true);
            }

            System.IO.Directory.Move(temporaryDirectory, options.OutputDirectory);
            return;
        }

        PublishDeployment(temporaryDirectory, options.OutputDirectory, manifest);
    }

    /// <summary>Publishes a validated temporary client into an explicit existing deployment directory.</summary>
    internal static void PublishDeployment(string temporaryDirectory, string outputDirectory, ClientBuildManifest manifest)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        // Snapshot and validate the old ownership before the new manifest can overwrite it.
        ClientBuildManifest? previousManifest = ReadPreviousDeploymentManifest(outputDirectory);
        ValidateManifestPaths(outputDirectory, manifest, "New deployment manifest");
        CopyPipelineFiles(temporaryDirectory, outputDirectory, manifest);
        RemovePreviouslyOwnedFiles(outputDirectory, previousManifest, manifest);
        WriteClientManifest(outputDirectory, manifest);
        System.IO.Directory.CreateDirectory(Path.Combine(outputDirectory, "data"));
    }

    internal static void RemovePreviouslyOwnedFiles(string outputDirectory, ClientBuildManifest currentManifest)
    {
        RemovePreviouslyOwnedFiles(outputDirectory, ReadPreviousDeploymentManifest(outputDirectory), currentManifest);
    }

    private static ClientBuildManifest? ReadPreviousDeploymentManifest(string outputDirectory)
    {
        string previousManifestPath = Path.Combine(outputDirectory, ClientManifestName);
        if (!File.Exists(previousManifestPath))
        {
            return null;
        }

        try
        {
            ClientBuildManifest? previous = JsonSerializer.Deserialize<ClientBuildManifest>(File.ReadAllText(previousManifestPath));
            if (previous == null || previous.RuntimeFiles == null)
            {
                throw new ClientBuildException("Existing deployment manifest is missing its runtime-file ownership list. Remove only alacrity-client-manifest.json after checking the client directory.");
            }

            ValidateManifestPaths(outputDirectory, previous, "Existing deployment manifest");
            return previous;
        }
        catch (JsonException exception)
        {
            throw new ClientBuildException("Existing deployment manifest is malformed. Remove only alacrity-client-manifest.json after checking the client directory. " + exception.Message);
        }
    }

    private static void RemovePreviouslyOwnedFiles(string outputDirectory, ClientBuildManifest? previousManifest, ClientBuildManifest currentManifest)
    {
        if (previousManifest == null)
        {
            return;
        }

        ValidateManifestPaths(outputDirectory, currentManifest, "New deployment manifest");
        for (int index = 0; index < previousManifest.RuntimeFiles.Count; index++)
        {
            string relativePath = ClientBuildPaths.NormalizeRelativePath(previousManifest.RuntimeFiles[index].Path, "Existing deployment manifest path");
            if (!IsPipelineOwnedRuntimePath(relativePath) || ContainsRuntimeFile(currentManifest, relativePath))
            {
                continue;
            }

            string candidate = ClientBuildPaths.ResolveUnderRoot(outputDirectory, relativePath, "Existing deployment manifest path");
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private static void ValidateManifestPaths(string rootDirectory, ClientBuildManifest manifest, string description)
    {
        for (int index = 0; index < manifest.RuntimeFiles.Count; index++)
        {
            ClientBuildPaths.ResolveUnderRoot(rootDirectory, manifest.RuntimeFiles[index].Path, description + " path");
        }
    }

    private static bool ContainsRuntimeFile(ClientBuildManifest manifest, string relativePath)
    {
        for (var index = 0; index < manifest.RuntimeFiles.Count; index++)
        {
            string current = ClientBuildPaths.NormalizeRelativePath(manifest.RuntimeFiles[index].Path, "New deployment manifest path");
            if (string.Equals(current, relativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPipelineOwnedRuntimePath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.StartsWith("Alacrity.", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "AlacrityBootstrapRuntime.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyPipelineFiles(string temporaryDirectory, string outputDirectory, ClientBuildManifest manifest)
    {
        var files = new List<string> { "Alacrity.exe" };
        for (var index = 0; index < manifest.RuntimeFiles.Count; index++)
        {
            files.Add(manifest.RuntimeFiles[index].Path.Replace('/', Path.DirectorySeparatorChar));
        }

        for (var index = 0; index < files.Count; index++)
        {
            string relativePath = ClientBuildPaths.NormalizeRelativePath(files[index], "Pipeline output path");
            string sourcePath = ClientBuildPaths.ResolveUnderRoot(temporaryDirectory, relativePath, "Pipeline output path");
            string targetPath = ClientBuildPaths.ResolveUnderRoot(outputDirectory, relativePath, "Pipeline output path");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static void WriteClientManifest(string directory, ClientBuildManifest manifest)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string path = Path.Combine(directory, ClientManifestName);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, options));
        try
        {
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? GetSourceRevision()
    {
        var value = Environment.GetEnvironmentVariable("ALACRITY_SOURCE_REVISION");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void AddExcludedPrefix(string sourceDirectory, string candidateDirectory, ICollection<string> prefixes)
    {
        if (IsSubdirectory(candidateDirectory, sourceDirectory))
        {
            prefixes.Add(Path.GetRelativePath(sourceDirectory, candidateDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
        }
    }

    private static bool IsExcluded(string relativePath, IReadOnlyList<string> prefixes)
    {
        for (var index = 0; index < prefixes.Count; index++)
        {
            if (relativePath.StartsWith(prefixes[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(directory.FullName, "BuildAlacrityClient.bat")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsSubdirectory(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
