internal enum DeploymentMutationPoint
{
    AfterCopy,
    AfterStaleCleanup,
    BeforeManifestCommit
}

/// <summary>
/// Records only files touched by one explicit deployment. If publishing fails after mutation
/// begins, the prior client state is restored before the builder reports the original failure.
/// </summary>
internal sealed class DeploymentTransaction : IDisposable
{
    private readonly string outputDirectory;
    private readonly string backupDirectory;
    private readonly Dictionary<string, bool> existed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private bool completed;

    internal DeploymentTransaction(string outputDirectory)
    {
        this.outputDirectory = outputDirectory;
        backupDirectory = Path.Combine(Path.GetDirectoryName(outputDirectory)!, ".alacrity-deployment-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
    }

    internal void Capture(string targetPath)
    {
        string relativePath = ClientBuildPaths.NormalizeRelativePath(Path.GetRelativePath(outputDirectory, targetPath), "Deployment target path");
        if (existed.ContainsKey(relativePath))
        {
            return;
        }

        bool fileExists = File.Exists(targetPath);
        existed.Add(relativePath, fileExists);
        if (!fileExists)
        {
            return;
        }

        string backupPath = ClientBuildPaths.ResolveUnderRoot(backupDirectory, relativePath, "Deployment backup path");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(targetPath, backupPath, overwrite: false);
    }

    internal void Commit()
    {
        completed = true;
    }

    internal void RollBack()
    {
        foreach (KeyValuePair<string, bool> entry in existed)
        {
            string targetPath = ClientBuildPaths.ResolveUnderRoot(outputDirectory, entry.Key, "Deployment rollback path");
            if (!entry.Value)
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                continue;
            }

            string backupPath = ClientBuildPaths.ResolveUnderRoot(backupDirectory, entry.Key, "Deployment backup path");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(backupPath, targetPath, overwrite: true);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!completed)
            {
                RollBack();
            }
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }
}
