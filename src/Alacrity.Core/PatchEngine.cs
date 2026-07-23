using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

public sealed class Sha256PatchVerifier : IPatchVerifier
{
    public string ComputeSha256(byte[] contents)
    {
        if (contents == null)
            throw new ArgumentNullException(nameof(contents));
        using (var sha256 = SHA256.Create())
            return BitConverter.ToString(sha256.ComputeHash(contents)).Replace("-", string.Empty).ToLowerInvariant();
    }

    public PatchVerificationResult Verify(byte[] contents, string expectedSha256)
    {
        var actual = ComputeSha256(contents);
        return new PatchVerificationResult(string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase), expectedSha256, actual);
    }
}

public sealed class InMemoryPatchJournal : IPatchJournal
{
    private readonly Dictionary<PatchId, PatchTransactionRecord> records = new Dictionary<PatchId, PatchTransactionRecord>();

    public PatchTransactionRecord? Get(PatchId id)
    {
        records.TryGetValue(id, out var record);
        return record;
    }

    public IReadOnlyList<PatchTransactionRecord> GetAll()
    {
        return new List<PatchTransactionRecord>(records.Values);
    }

    public void Record(PatchTransactionRecord record)
    {
        records[record.Id] = record;
    }
}

internal sealed class PatchRegistryAuthority
{
}

internal sealed class PatchRegistry
{
    private readonly IPatchFileStore files;
    private readonly IPatchVerifier verifier;
    private readonly IPatchJournal journal;
    private readonly PatchRegistryAuthority authority;
    private readonly Dictionary<PatchId, RegisteredPatchDefinition> definitions = new Dictionary<PatchId, RegisteredPatchDefinition>();
    private readonly HashSet<string> reservedPaths = new HashSet<string>(StringComparer.Ordinal);
    private readonly object gate = new object();

    internal PatchRegistry(IPatchFileStore files, IPatchVerifier verifier, IPatchJournal journal, PatchRegistryAuthority authority)
    {
        this.files = files ?? throw new ArgumentNullException(nameof(files));
        this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    /// <summary>Creates a plugin-scoped patch capability for an authorized host caller.</summary>
    internal IPatchEngine ForOwner(PatchRegistryAuthority authority, PluginId owner)
    {
        if (!ReferenceEquals(this.authority, authority))
            throw new UnauthorizedAccessException("Only the host authority that created this registry may issue patch-owner capabilities.");
        if (string.IsNullOrWhiteSpace(owner.Value))
            throw new ArgumentException("A plugin owner is required.", nameof(owner));
        return new OwnedPatchEngine(this, owner);
    }

    private void Register(PluginId owner, PatchDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        lock (gate)
        {
            if (owner != definition.Owner)
                throw new UnauthorizedAccessException("Plugin does not own patch " + definition.Id + ".");
            if (definitions.ContainsKey(definition.Id))
                throw new InvalidOperationException("Patch ID is already registered: " + definition.Id);

            var replacement = verifier.Verify(definition.ReplacementContents, definition.ExpectedPatchedSha256);
            if (!replacement.IsMatch)
                throw new ArgumentException("Replacement contents do not match the declared patched hash.", nameof(definition));

            var targetIdentity = GetPathIdentity(definition.TargetPath);
            var registeredDefinition = new RegisteredPatchDefinition(definition, GetHostBackupPath(definition));
            var backupIdentity = GetPathIdentity(registeredDefinition.BackupPath);
            if (string.Equals(targetIdentity, backupIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("Patch target and backup resolve to the same managed file.");
            if (reservedPaths.Contains(targetIdentity))
                throw new InvalidOperationException("Patch target is already reserved by another registered patch: " + definition.TargetPath);
            if (reservedPaths.Contains(backupIdentity))
                throw new InvalidOperationException("Host-derived patch backup is already reserved by another registered patch.");

            definitions.Add(definition.Id, registeredDefinition);
            reservedPaths.Add(targetIdentity);
            reservedPaths.Add(backupIdentity);
        }
    }

    private PatchTransactionRecord Apply(PluginId owner, PatchId id)
    {
        lock (gate)
        {
            var definition = GetDefinition(id);
            EnsureOwner(owner, definition);
            EnsureTargetExists(definition);

            var current = files.ReadAllBytes(definition.TargetPath);
            var original = verifier.Verify(current, definition.ExpectedOriginalSha256);
            if (!original.IsMatch)
            {
                var alreadyApplied = verifier.Verify(current, definition.ExpectedPatchedSha256);
                if (alreadyApplied.IsMatch)
                {
                    if (!files.Exists(definition.BackupPath))
                        return Fail(definition, "Target is already patched but its verified rollback backup is missing.");
                    var backup = verifier.Verify(files.ReadAllBytes(definition.BackupPath), definition.ExpectedOriginalSha256);
                    return backup.IsMatch
                        ? Record(definition, PatchTransactionState.Applied)
                        : Fail(definition, "Target is already patched but its rollback backup failed verification.");
                }
                return Fail(definition, "Original verification failed. Expected " + original.ExpectedSha256 + ", got " + original.ActualSha256 + ".");
            }

            Record(definition, PatchTransactionState.VerifiedOriginal);
            var targetWriteAttempted = false;
            try
            {
                EnsureBackup(definition, current);
                Record(definition, PatchTransactionState.BackupCreated);
                Record(definition, PatchTransactionState.Writing);
                targetWriteAttempted = true;
                if (!files.TryWriteAtomically(definition.TargetPath, current, definition.ReplacementContents))
                {
                    targetWriteAttempted = false;
                    throw new InvalidOperationException("Patch target changed after original verification; no replacement was written.");
                }

                var patched = verifier.Verify(files.ReadAllBytes(definition.TargetPath), definition.ExpectedPatchedSha256);
                if (!patched.IsMatch)
                    throw new InvalidOperationException("Patched verification failed. Expected " + patched.ExpectedSha256 + ", got " + patched.ActualSha256 + ".");

                return Record(definition, PatchTransactionState.Applied);
            }
            catch (Exception exception)
            {
                if (!targetWriteAttempted)
                    return Fail(definition, exception.Message);

                var recoveryError = TryRestoreOriginal(definition);
                return recoveryError == null
                    ? Fail(definition, exception.Message + " The verified original content was restored.")
                    : RecoveryFail(definition, exception.Message + " Recovery failed: " + recoveryError);
            }
        }
    }

    private PatchTransactionRecord Rollback(PluginId owner, PatchId id)
    {
        lock (gate)
        {
            var definition = GetDefinition(id);
            EnsureOwner(owner, definition);
            var targetExists = files.Exists(definition.TargetPath);
            byte[]? current = null;
            byte[] backup;
            try
            {
                if (!files.Exists(definition.BackupPath))
                    return targetExists
                        ? Fail(definition, "No verified backup exists for rollback.")
                        : RecoveryFail(definition, "The target and its verified rollback backup are both missing.");

                backup = files.ReadAllBytes(definition.BackupPath);
                var backupVerification = verifier.Verify(backup, definition.ExpectedOriginalSha256);
                if (!backupVerification.IsMatch)
                    return targetExists
                        ? Fail(definition, "Backup verification failed.")
                        : RecoveryFail(definition, "The target is missing and its rollback backup failed verification.");
            }
            catch (Exception exception)
            {
                return targetExists
                    ? Fail(definition, "Unable to read the rollback backup: " + exception.Message)
                    : RecoveryFail(definition, "The target is missing and the rollback backup could not be read: " + exception.Message);
            }

            if (targetExists)
            {
                try
                {
                    current = files.ReadAllBytes(definition.TargetPath);
                    var patched = verifier.Verify(current, definition.ExpectedPatchedSha256);
                    if (!patched.IsMatch)
                    {
                        var original = verifier.Verify(current, definition.ExpectedOriginalSha256);
                        if (original.IsMatch)
                            return Record(definition, PatchTransactionState.RolledBack);
                        return Fail(definition, "Rollback refused because the target is neither the expected patched nor original content.");
                    }
                }
                catch (Exception exception)
                {
                    return RecoveryFail(definition, "Unable to inspect the rollback target: " + exception.Message);
                }
            }

            try
            {
                Record(definition, PatchTransactionState.RollingBack);
                if (!files.TryWriteAtomically(definition.TargetPath, current, backup))
                    throw new InvalidOperationException("Patch target changed during rollback; no rollback content was written.");
                var restored = verifier.Verify(files.ReadAllBytes(definition.TargetPath), definition.ExpectedOriginalSha256);
                if (!restored.IsMatch)
                    throw new InvalidOperationException("Restored target verification failed.");
                return Record(definition, PatchTransactionState.RolledBack);
            }
            catch (Exception exception)
            {
                return ClassifyRollbackFailure(definition, exception);
            }
        }
    }

    internal IReadOnlyList<PatchRecoveryResult> RecoverIncompleteTransactions()
    {
        lock (gate)
        {
            var results = new List<PatchRecoveryResult>();
            foreach (var record in journal.GetAll())
            {
                if (!IsIncomplete(record.State) || !definitions.TryGetValue(record.Id, out var definition))
                    continue;

                results.Add(Recover(definition, record));
            }

            return results;
        }
    }

    private PatchTransactionRecord? GetStatus(PluginId owner, PatchId id)
    {
        lock (gate)
        {
            if (!definitions.TryGetValue(id, out var definition))
                return null;
            EnsureOwner(owner, definition);
            return journal.Get(id);
        }
    }

    private RegisteredPatchDefinition GetDefinition(PatchId id)
    {
        if (!definitions.TryGetValue(id, out var definition))
            throw new KeyNotFoundException("Patch is not registered: " + id);
        return definition;
    }

    private static void EnsureOwner(PluginId owner, RegisteredPatchDefinition definition)
    {
        if (owner != definition.Owner)
            throw new UnauthorizedAccessException("Plugin does not own patch " + definition.Id + ".");
    }

    private void EnsureTargetExists(RegisteredPatchDefinition definition)
    {
        if (!files.Exists(definition.TargetPath))
            throw new InvalidOperationException("Patch target does not exist: " + definition.TargetPath);
    }

    private string GetPathIdentity(string path)
    {
        var identity = files.GetPathIdentity(path);
        if (string.IsNullOrWhiteSpace(identity))
            throw new InvalidOperationException("The patch file store returned an invalid path identity for " + path + ".");
        return identity;
    }

    private void EnsureBackup(RegisteredPatchDefinition definition, byte[] verifiedOriginal)
    {
        if (!files.Exists(definition.BackupPath))
            files.Copy(definition.TargetPath, definition.BackupPath, false);

        var backup = files.ReadAllBytes(definition.BackupPath);
        var backupVerification = verifier.Verify(backup, definition.ExpectedOriginalSha256);
        var verifiedSnapshotHash = verifier.ComputeSha256(verifiedOriginal);
        if (!backupVerification.IsMatch ||
            !string.Equals(backupVerification.ActualSha256, verifiedSnapshotHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Rollback backup does not match the verified original snapshot.");
    }

    private string? TryRestoreOriginal(RegisteredPatchDefinition definition)
    {
        try
        {
            byte[]? observed = null;
            if (files.Exists(definition.TargetPath))
            {
                observed = files.ReadAllBytes(definition.TargetPath);
                var currentVerification = verifier.Verify(observed, definition.ExpectedOriginalSha256);
                if (currentVerification.IsMatch)
                    return null;
            }

            if (!files.Exists(definition.BackupPath))
                return "The rollback backup is missing.";

            var backup = files.ReadAllBytes(definition.BackupPath);
            var backupVerification = verifier.Verify(backup, definition.ExpectedOriginalSha256);
            if (!backupVerification.IsMatch)
                return "The rollback backup failed verification.";

            if (!files.TryWriteAtomically(definition.TargetPath, observed, backup))
                return "The target changed during recovery; no recovery content was written.";
            var restored = verifier.Verify(files.ReadAllBytes(definition.TargetPath), definition.ExpectedOriginalSha256);
            return restored.IsMatch ? null : "The restored target failed verification.";
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private PatchTransactionRecord ClassifyRollbackFailure(RegisteredPatchDefinition definition, Exception exception)
    {
        PatchTransactionState state;
        string detail;
        try
        {
            if (!files.Exists(definition.TargetPath))
            {
                state = PatchTransactionState.RecoveryFailed;
                detail = "The target is missing after rollback failed.";
            }
            else
            {
                var current = files.ReadAllBytes(definition.TargetPath);
                if (verifier.Verify(current, definition.ExpectedOriginalSha256).IsMatch)
                {
                    state = PatchTransactionState.RolledBack;
                    detail = string.Empty;
                }
                else if (verifier.Verify(current, definition.ExpectedPatchedSha256).IsMatch)
                {
                    state = PatchTransactionState.Failed;
                    detail = "The verified patched target remains installed.";
                }
                else
                {
                    state = PatchTransactionState.RecoveryFailed;
                    detail = "The target is in an unrecognized state.";
                }
            }
        }
        catch (Exception inspectionException)
        {
            state = PatchTransactionState.RecoveryFailed;
            detail = "Final-state inspection failed: " + inspectionException.Message;
        }

        if (state == PatchTransactionState.RolledBack)
            return Record(definition, PatchTransactionState.RolledBack);
        if (state == PatchTransactionState.Failed)
            return Fail(definition, exception.Message + " " + detail);
        return RecoveryFail(definition, exception.Message + " " + detail);
    }

    private PatchTransactionRecord Record(RegisteredPatchDefinition definition, PatchTransactionState state)
    {
        var record = new PatchTransactionRecord(definition.Id, definition.Owner, state);
        journal.Record(record);
        return record;
    }

    private PatchTransactionRecord Fail(RegisteredPatchDefinition definition, string error)
    {
        var record = new PatchTransactionRecord(definition.Id, definition.Owner, PatchTransactionState.Failed, error);
        journal.Record(record);
        return record;
    }

    private PatchTransactionRecord RecoveryFail(RegisteredPatchDefinition definition, string error)
    {
        var record = new PatchTransactionRecord(definition.Id, definition.Owner, PatchTransactionState.RecoveryFailed, error);
        journal.Record(record);
        return record;
    }

    private PatchRecoveryResult Recover(RegisteredPatchDefinition definition, PatchTransactionRecord record)
    {
        try
        {
            var target = Inspect(definition.TargetPath, definition.ExpectedOriginalSha256, definition.ExpectedPatchedSha256);
            var backup = Inspect(definition.BackupPath, definition.ExpectedOriginalSha256, definition.ExpectedPatchedSha256);

            if (target == PatchContentState.Original)
                return new PatchRecoveryResult(definition.Id, Record(definition, PatchTransactionState.RolledBack), "Target already contains the verified original.");
            if (target == PatchContentState.Patched && backup == PatchContentState.Original)
                return new PatchRecoveryResult(definition.Id, Record(definition, PatchTransactionState.Applied), "Target and verified rollback backup are intact.");
            if (target == PatchContentState.Missing && backup == PatchContentState.Original)
            {
                if (!files.TryWriteAtomically(definition.TargetPath, null, files.ReadAllBytes(definition.BackupPath)))
                    return new PatchRecoveryResult(definition.Id, RecoveryFail(definition, "Recovery target changed before the verified backup could be restored."), "Recovery was not applied.");

                var restored = Inspect(definition.TargetPath, definition.ExpectedOriginalSha256, definition.ExpectedPatchedSha256);
                return restored == PatchContentState.Original
                    ? new PatchRecoveryResult(definition.Id, Record(definition, PatchTransactionState.RolledBack), "Missing target restored from the verified backup.")
                    : new PatchRecoveryResult(definition.Id, RecoveryFail(definition, "Recovered target did not match the verified original."), "Recovery verification failed.");
            }

            return new PatchRecoveryResult(definition.Id, RecoveryFail(definition, "Incomplete transaction has an unknown or unverifiable target state."), "Plugin activation must remain blocked until manual recovery.");
        }
        catch (Exception exception)
        {
            return new PatchRecoveryResult(definition.Id, RecoveryFail(definition, "Recovery inspection failed: " + exception.Message), "Plugin activation must remain blocked until manual recovery.");
        }
    }

    private PatchContentState Inspect(string path, string originalHash, string patchedHash)
    {
        if (!files.Exists(path))
            return PatchContentState.Missing;

        var contents = files.ReadAllBytes(path);
        if (verifier.Verify(contents, originalHash).IsMatch)
            return PatchContentState.Original;
        if (verifier.Verify(contents, patchedHash).IsMatch)
            return PatchContentState.Patched;
        return PatchContentState.Unknown;
    }

    private static bool IsIncomplete(PatchTransactionState state)
    {
        return state == PatchTransactionState.VerifiedOriginal ||
               state == PatchTransactionState.BackupCreated ||
               state == PatchTransactionState.Writing ||
               state == PatchTransactionState.RollingBack;
    }

    private enum PatchContentState
    {
        Missing,
        Original,
        Patched,
        Unknown
    }

    private sealed class OwnedPatchEngine : IPatchEngine
    {
        private readonly PatchRegistry registry;

        public OwnedPatchEngine(PatchRegistry registry, PluginId owner)
        {
            this.registry = registry;
            Owner = owner;
        }

        public PluginId Owner { get; }

        public void Register(PatchDefinition definition) => registry.Register(Owner, definition);

        public PatchTransactionRecord Apply(PatchId id) => registry.Apply(Owner, id);

        public PatchTransactionRecord Rollback(PatchId id) => registry.Rollback(Owner, id);

        public PatchTransactionRecord? GetStatus(PatchId id) => registry.GetStatus(Owner, id);
    }

    private static string GetHostBackupPath(PatchDefinition definition)
    {
        return ".alacrity-backups/" + definition.Owner.Value + "/" + definition.Id.Value + ".bak";
    }

    /// <summary>Core-only transaction data, including the host-controlled rollback location.</summary>
    private sealed class RegisteredPatchDefinition
    {
        public RegisteredPatchDefinition(PatchDefinition definition, string backupPath)
        {
            Definition = definition;
            BackupPath = backupPath;
        }

        private PatchDefinition Definition { get; }
        public PatchId Id => Definition.Id;
        public PluginId Owner => Definition.Owner;
        public string TargetPath => Definition.TargetPath;
        public string BackupPath { get; }
        public string ExpectedOriginalSha256 => Definition.ExpectedOriginalSha256;
        public byte[] ReplacementContents => Definition.ReplacementContents;
        public string ExpectedPatchedSha256 => Definition.ExpectedPatchedSha256;
    }
}

/// <summary>Host-owned entry point that issues plugin-scoped patch capabilities.</summary>
public sealed class PatchHost
{
    private readonly PatchRegistryAuthority authority = new PatchRegistryAuthority();
    private readonly PatchRegistry registry;

    public PatchHost(IPatchFileStore files, IPatchVerifier verifier, IPatchJournal journal)
    {
        registry = new PatchRegistry(files, verifier, journal, authority);
    }

    public IPatchEngine ForPlugin(PluginId pluginId)
    {
        return registry.ForOwner(authority, pluginId);
    }

    /// <summary>
    /// Reconciles incomplete registered transactions before their plugins can activate. Unknown
    /// content is never overwritten and is reported as unresolved recovery.
    /// </summary>
    public IReadOnlyList<PatchRecoveryResult> RecoverIncompleteTransactions()
    {
        return registry.RecoverIncompleteTransactions();
    }

    public static PatchHost CreateManaged(string patchRoot, string journalPath)
    {
        return new PatchHost(new ManagedPatchFileStore(patchRoot), new Sha256PatchVerifier(), new FilePatchJournal(journalPath));
    }
}

/// <summary>Host diagnostic outcome for one incomplete patch transaction.</summary>
public sealed class PatchRecoveryResult
{
    internal PatchRecoveryResult(PatchId id, PatchTransactionRecord record, string detail)
    {
        Id = id;
        Record = record;
        Detail = detail;
    }

    /// <summary>Recovered patch identifier.</summary>
    public PatchId Id { get; }
    /// <summary>Journal state produced by recovery.</summary>
    public PatchTransactionRecord Record { get; }
    /// <summary>Human-readable host diagnostic.</summary>
    public string Detail { get; }
    /// <summary>Whether recovery reached a known-safe state.</summary>
    public bool IsResolved => Record.State == PatchTransactionState.Applied || Record.State == PatchTransactionState.RolledBack;
}
