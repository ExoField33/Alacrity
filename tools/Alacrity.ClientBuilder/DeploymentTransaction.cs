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
    private TransactionState state;

    internal DeploymentTransaction(string outputDirectory)
    {
        this.outputDirectory = outputDirectory;
        backupDirectory = Path.Combine(Path.GetDirectoryName(outputDirectory)!, ".alacrity-deployment-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        state = TransactionState.Active;
    }

    /// <summary>Recovery material retained after a failed rollback.</summary>
    internal string RecoveryDirectory => backupDirectory;

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
        if (state == TransactionState.RollbackFailed)
        {
            throw new InvalidOperationException("A deployment with failed rollback cannot be committed. Recover from '" + backupDirectory + "'.");
        }

        state = TransactionState.Committed;
    }

    internal void RollBack()
    {
        if (state == TransactionState.RolledBack)
        {
            return;
        }

        try
        {
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

            state = TransactionState.RolledBack;
        }
        catch
        {
            state = TransactionState.RollbackFailed;
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            if (state == TransactionState.Active)
            {
                try
                {
                    RollBack();
                }
                catch
                {
                    // Dispose commonly runs while an earlier deployment exception is unwinding.
                    // RollBack records RollbackFailed so recovery material survives this path.
                }
            }
        }
        finally
        {
            if ((state == TransactionState.Committed || state == TransactionState.RolledBack) && Directory.Exists(backupDirectory))
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

    private enum TransactionState
    {
        Active,
        Committed,
        RolledBack,
        RollbackFailed
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
