using System.Text;

/// <summary>Validated, immutable view of one runtime stage produced by RuntimeStaging.</summary>
internal sealed class RuntimeStage
{
    private readonly List<ClientBuildFile> files;

    private RuntimeStage(string directory, string buildConfiguration, List<ClientBuildFile> files)
    {
        Directory = directory;
        BuildConfiguration = buildConfiguration;
        this.files = files;
    }

    internal string Directory { get; }
    internal string BuildConfiguration { get; }
    internal IReadOnlyList<ClientBuildFile> Files => files;

    internal static RuntimeStage Load(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            throw new ClientBuildException("Runtime stage directory was not found: " + directory);
        }

        var manifestPath = Path.Combine(directory, "runtime-manifest.txt");
        if (!File.Exists(manifestPath))
        {
            throw new ClientBuildException("Runtime stage is missing runtime-manifest.txt. Build Alacrity.RuntimeStaging first.");
        }

        RequireFile(directory, "bin\\Alacrity.PluginUiRuntime.dll");
        RequireFile(directory, "bin\\Alacrity.PluginUiCoreBridge.dll");
        RequireFile(directory, "Alacrity.PluginSdk.dll");
        RequireFile(directory, "Alacrity.Core.dll");
        RequireFile(directory, "VERSION");

        var declaredHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? buildConfiguration = null;
        var lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("Configuration=", StringComparison.Ordinal))
            {
                if (buildConfiguration != null)
                {
                    throw new ClientBuildException("Runtime stage manifest declares more than one build configuration.");
                }

                buildConfiguration = lines[index].Substring("Configuration=".Length);
                continue;
            }

            var separator = lines[index].IndexOf('|');
            if (separator <= 0)
            {
                continue;
            }

            var relativePath = lines[index].Substring(0, separator).Replace('\\', '/');
            var hash = lines[index].Substring(separator + 1);
            if (declaredHashes.ContainsKey(relativePath))
            {
                throw new ClientBuildException("Runtime stage manifest declares a duplicate path: " + relativePath + ".");
            }

            declaredHashes.Add(relativePath, hash);
        }

        var files = new List<ClientBuildFile>();
        foreach (var filePath in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(directory, filePath);
            if (string.Equals(relativePath, "runtime-manifest.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hash = SupportedTerrariaBuildCatalog.ComputeSha256(filePath);
            var manifestRelativePath = relativePath.Replace('\\', '/');
            if (!declaredHashes.TryGetValue(manifestRelativePath, out var declaredHash) || !string.Equals(declaredHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new ClientBuildException("Runtime stage manifest does not verify " + relativePath + ". Rebuild the complete runtime stage.");
            }

            files.Add(new ClientBuildFile { Path = relativePath.Replace('\\', '/'), Sha256 = hash });
        }

        if (files.Count == 0)
        {
            throw new ClientBuildException("Runtime stage contained no deployable files.");
        }

        if (declaredHashes.Count != files.Count)
        {
            throw new ClientBuildException("Runtime stage manifest declares files that are missing from the staged directory.");
        }

        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        if (string.IsNullOrWhiteSpace(buildConfiguration))
        {
            throw new ClientBuildException("Runtime stage manifest is missing its build configuration.");
        }

        return new RuntimeStage(directory, buildConfiguration, files);
    }

    internal void CopyTo(string targetDirectory)
    {
        for (var index = 0; index < files.Count; index++)
        {
            var relativePath = files[index].Path.Replace('/', Path.DirectorySeparatorChar);
            var sourcePath = Path.Combine(Directory, relativePath);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            var targetParent = Path.GetDirectoryName(targetPath)!;
            System.IO.Directory.CreateDirectory(targetParent);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static void RequireFile(string directory, string relativePath)
    {
        if (!File.Exists(Path.Combine(directory, relativePath)))
        {
            throw new ClientBuildException("Runtime stage is missing required file " + relativePath + ".");
        }
    }
}
