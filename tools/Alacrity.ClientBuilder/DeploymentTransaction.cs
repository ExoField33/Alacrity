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
    // Test seam for deterministic backup-copy failure coverage. Production retains File.Copy.
    internal static Action<string, string> BackupFileCopy = CopyBackupFile;
    // Test seam for deterministic restoration failures. It also keeps rollback's completion state
    // honest: a failed restore may be retried while its backups still exist.
    internal static Action<string, string> RestoreFileCopy = RestoreBackupFile;

    private readonly string outputDirectory;
    private readonly string backupDirectory;
    private readonly Dictionary<string, CaptureState> captures = new Dictionary<string, CaptureState>(StringComparer.OrdinalIgnoreCase);
    private bool completed;
    private bool rolledBack;

    internal DeploymentTransaction(string outputDirectory)
    {
        this.outputDirectory = outputDirectory;
        backupDirectory = Path.Combine(Path.GetDirectoryName(outputDirectory)!, ".alacrity-deployment-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
    }

    internal void Capture(string targetPath)
    {
        string relativePath = ClientBuildPaths.NormalizeRelativePath(Path.GetRelativePath(outputDirectory, targetPath), "Deployment target path");
        if (captures.ContainsKey(relativePath))
        {
            return;
        }

        if (!File.Exists(targetPath))
        {
            captures.Add(relativePath, CaptureState.DidNotExist);
            return;
        }

        string backupPath = ClientBuildPaths.ResolveUnderRoot(backupDirectory, relativePath, "Deployment backup path");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        // Do not publish capture state until the copy succeeds. A caller that sees a failure can
        // then safely leave the untouched original alone rather than treating it as recoverable.
        BackupFileCopy(targetPath, backupPath);
        captures.Add(relativePath, CaptureState.BackedUp);
    }

    internal void Commit()
    {
        completed = true;
    }

    internal void RollBack()
    {
        if (rolledBack)
        {
            return;
        }
        foreach (KeyValuePair<string, CaptureState> entry in captures)
        {
            string targetPath = ClientBuildPaths.ResolveUnderRoot(outputDirectory, entry.Key, "Deployment rollback path");
            if (entry.Value == CaptureState.DidNotExist)
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                continue;
            }

            string backupPath = ClientBuildPaths.ResolveUnderRoot(backupDirectory, entry.Key, "Deployment backup path");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            RestoreFileCopy(backupPath, targetPath);
        }

        rolledBack = true;
    }

    public void Dispose()
    {
        try
        {
            if (!completed)
            {
                try
                {
                    RollBack();
                }
                catch
                {
                    // Dispose commonly runs while an earlier deployment exception is unwinding.
                    // Preserve that original failure; the backup directory remains available until
                    // this finally block removes the transaction's temporary state.
                }
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

    private enum CaptureState
    {
        DidNotExist,
        BackedUp
    }

    private static void CopyBackupFile(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    private static void RestoreBackupFile(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }
}
