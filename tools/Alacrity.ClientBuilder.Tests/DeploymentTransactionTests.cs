using Xunit;

public sealed class DeploymentTransactionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alacrity.DeploymentTransaction.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BackupFailureDoesNotRecordOrModifyTheExistingFile()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "Alacrity.dll");
        File.WriteAllText(target, "original");
        DeploymentTransaction.BackupFileCopy = (_, _) => throw new IOException("injected backup failure");
        try
        {
            using var transaction = new DeploymentTransaction(directory);
            Assert.Throws<IOException>(() => transaction.Capture(target));
            Assert.Equal("original", File.ReadAllText(target));
        }
        finally
        {
            DeploymentTransaction.BackupFileCopy = (source, destination) => File.Copy(source, destination, overwrite: false);
        }
    }

    [Fact]
    public void RollbackIsIdempotentAndCommitPreventsDisposeRollback()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "Alacrity.dll");
        File.WriteAllText(target, "original");
        using (var transaction = new DeploymentTransaction(directory))
        {
            transaction.Capture(target);
            File.WriteAllText(target, "changed");
            transaction.RollBack();
            transaction.RollBack();
            Assert.Equal("original", File.ReadAllText(target));
        }

        using (var transaction = new DeploymentTransaction(directory))
        {
            transaction.Capture(target);
            File.WriteAllText(target, "committed");
            transaction.Commit();
        }

        Assert.Equal("committed", File.ReadAllText(target));
    }

    [Fact]
    public void FailedRollbackRemainsRetryableAndDoesNotClaimCompletion()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "Alacrity.dll");
        File.WriteAllText(target, "original");
        using var transaction = new DeploymentTransaction(directory);
        transaction.Capture(target);
        File.WriteAllText(target, "changed");
        DeploymentTransaction.RestoreFileCopy = (_, _) => throw new IOException("injected restore failure");
        try
        {
            Assert.Throws<IOException>(() => transaction.RollBack());
            Assert.Equal("changed", File.ReadAllText(target));
        }
        finally
        {
            DeploymentTransaction.RestoreFileCopy = (source, destination) => File.Copy(source, destination, overwrite: true);
        }

        transaction.RollBack();
        transaction.RollBack();
        Assert.Equal("original", File.ReadAllText(target));
    }

    [Fact]
    public void FailedRollbackKeepsRecoveryBackupsAfterDispose()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "Alacrity.dll");
        File.WriteAllText(target, "original");
        var transaction = new DeploymentTransaction(directory);
        transaction.Capture(target);
        File.WriteAllText(target, "changed");
        string recovery = transaction.RecoveryDirectory;
        DeploymentTransaction.RestoreFileCopy = (_, _) => throw new IOException("injected restore failure");
        try
        {
            Assert.Throws<IOException>(() => transaction.RollBack());
            transaction.Dispose();
            Assert.True(Directory.Exists(recovery));
            Assert.True(File.Exists(Path.Combine(recovery, "Alacrity.dll")));
        }
        finally
        {
            DeploymentTransaction.RestoreFileCopy = (source, destination) => File.Copy(source, destination, overwrite: true);
            if (Directory.Exists(recovery))
            {
                Directory.Delete(recovery, recursive: true);
            }
        }
    }

    [Fact]
    public void PublishDeploymentRetainsBothMutationAndRollbackDiagnostics()
    {
        string output = Path.Combine(directory, "client");
        string temporary = Path.Combine(directory, "temporary");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(temporary);
        File.WriteAllText(Path.Combine(output, "Alacrity.exe"), "old executable");
        File.WriteAllText(Path.Combine(temporary, "Alacrity.exe"), "new executable");
        var manifest = new ClientBuildManifest
        {
            OutputExecutableSha256 = SupportedTerrariaBuildCatalog.ComputeSha256(Path.Combine(temporary, "Alacrity.exe")),
            BridgeHandshake = "3|2|3|1.4.5.6"
        };

        ClientBuildPipeline.DeploymentMutationFailureInjector = point =>
        {
            if (point == DeploymentMutationPoint.AfterCopy)
            {
                throw new ClientBuildException("injected mutation failure");
            }
        };
        DeploymentTransaction.RestoreFileCopy = (_, _) => throw new IOException("injected rollback failure");
        try
        {
            ClientBuildException exception = Assert.Throws<ClientBuildException>(() =>
                ClientBuildPipeline.PublishDeployment(temporary, output, manifest));
            Assert.Contains("injected mutation failure", exception.Message, StringComparison.Ordinal);
            Assert.Contains("injected rollback failure", exception.Message, StringComparison.Ordinal);
            Assert.IsType<AggregateException>(exception.InnerException);
        }
        finally
        {
            ClientBuildPipeline.DeploymentMutationFailureInjector = null;
            DeploymentTransaction.RestoreFileCopy = (source, destination) => File.Copy(source, destination, overwrite: true);
        }
    }

    public void Dispose()
    {
        DeploymentTransaction.BackupFileCopy = (source, destination) => File.Copy(source, destination, overwrite: false);
        DeploymentTransaction.RestoreFileCopy = (source, destination) => File.Copy(source, destination, overwrite: true);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
