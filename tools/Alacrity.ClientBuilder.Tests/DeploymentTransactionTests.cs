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

    public void Dispose()
    {
        DeploymentTransaction.BackupFileCopy = (source, destination) => File.Copy(source, destination, overwrite: false);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
