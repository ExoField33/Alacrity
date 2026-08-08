using System;
using System.Collections.Generic;

namespace Alacrity.PluginSdk;

/// Stable identifier for one centrally owned patch.
public readonly struct PatchId : IEquatable<PatchId>
{
    /// Maximum number of characters in a package-safe patch identifier.
    public const int MaximumLength = 96;

    /// Creates a patch identifier.
    public PatchId(string value)
    {
        if (!TryValidate(value, out var error))
            throw new ArgumentException(error, nameof(value));
        Value = value;
    }

    /// Canonical patch identifier text.
    public string Value { get; }

    /// Whether this instance contains a valid non-default identifier.
    public bool IsValid => TryValidate(Value, out _);

    /// Parses a package-safe patch identifier.
    public static PatchId Parse(string value) => new PatchId(value);

    /// Attempts to parse a package-safe patch identifier.
    public static bool TryParse(string? value, out PatchId id)
    {
        if (TryValidate(value, out _))
        {
            id = new PatchId(value!);
            return true;
        }

        id = default;
        return false;
    }

    /// <inheritdoc />
    public bool Equals(PatchId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PatchId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// Compares two patch identifiers.
    public static bool operator ==(PatchId left, PatchId right) => left.Equals(right);

    /// Compares two patch identifiers for inequality.
    public static bool operator !=(PatchId left, PatchId right) => !left.Equals(right);

    private static bool TryValidate(string? value, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A patch ID is required.";
            return false;
        }

        var text = value!;
        if (text.Length > MaximumLength)
        {
            error = "Patch IDs cannot exceed " + MaximumLength + " characters.";
            return false;
        }

        if (text[0] == '.' || text[0] == '-' || text[text.Length - 1] == '.' || text[text.Length - 1] == '-')
        {
            error = "Patch IDs cannot begin or end with a separator.";
            return false;
        }

        var previousWasSeparator = false;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            var separator = character == '-' || character == '.';
            if ((character < 'a' || character > 'z') && (character < '0' || character > '9') && !separator)
            {
                error = "Patch IDs may contain lowercase letters, digits, '-' and '.'.";
                return false;
            }

            if (separator && previousWasSeparator)
            {
                error = "Patch IDs cannot contain empty segments.";
                return false;
            }

            previousWasSeparator = separator;
        }

        error = string.Empty;
        return true;
    }
}

/// 
/// Immutable declaration of a full-file patch owned by one plugin. The host derives all backup
/// locations; plugin code never receives control of rollback paths.
/// 
public sealed class PatchDefinition
{
    /// Creates a patch declaration. Contents are copied defensively.
    public PatchDefinition(
        PatchId id,
        PluginId owner,
        string targetPath,
        string expectedOriginalSha256,
        byte[] replacementContents,
        string expectedPatchedSha256)
    {
        Id = id;
        Owner = owner;
        TargetPath = RequirePath(targetPath, nameof(targetPath));
        ExpectedOriginalSha256 = RequireHash(expectedOriginalSha256, nameof(expectedOriginalSha256));
        ExpectedPatchedSha256 = RequireHash(expectedPatchedSha256, nameof(expectedPatchedSha256));
        replacement = replacementContents == null ? throw new ArgumentNullException(nameof(replacementContents)) : (byte[])replacementContents.Clone();
    }

    /// Patch identifier.
    public PatchId Id { get; }
    /// Plugin that owns registration, application, and rollback.
    public PluginId Owner { get; }
    /// Target file path within the host-managed patch root.
    public string TargetPath { get; }
    /// Hash required before application.
    public string ExpectedOriginalSha256 { get; }
    /// Replacement bytes written by the transaction.
    public byte[] ReplacementContents => (byte[])replacement.Clone();
    /// Hash required after application.
    public string ExpectedPatchedSha256 { get; }

    private readonly byte[] replacement;

    private static string RequirePath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty path is required.", parameterName);
        return value;
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            throw new ArgumentException("A SHA-256 hash is required.", parameterName);

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
                throw new ArgumentException("A SHA-256 hash must contain hexadecimal characters only.", parameterName);
        }

        return value.ToLowerInvariant();
    }
}

/// File operations needed by the patch engine.
public interface IPatchFileStore
{
    /// Returns a stable canonical comparison key for a managed path and rejects paths outside the managed root.
    string GetPathIdentity(string path);
    /// Checks whether a managed path exists.
    bool Exists(string path);
    /// Reads a complete file snapshot.
    byte[] ReadAllBytes(string path);
    /// Atomically writes only when the current file exactly matches the expected snapshot; a null expectation requires the path to remain absent.
    bool TryWriteAtomically(string path, byte[]? expectedContents, byte[] contents);
    /// Copies a file to a backup path.
    void Copy(string sourcePath, string destinationPath, bool overwrite);
}

/// Result of comparing a file snapshot to a declared patch hash.
public sealed class PatchVerificationResult
{
    /// Creates a verification result.
    public PatchVerificationResult(bool isMatch, string expectedSha256, string actualSha256)
    {
        IsMatch = isMatch;
        ExpectedSha256 = expectedSha256;
        ActualSha256 = actualSha256;
    }

    /// Whether expected and actual hashes match.
    public bool IsMatch { get; }
    /// Declared hash.
    public string ExpectedSha256 { get; }
    /// Observed hash.
    public string ActualSha256 { get; }
}

/// Verification service kept separate from file mutation.
public interface IPatchVerifier
{
    /// Computes the canonical SHA-256 representation.
    string ComputeSha256(byte[] contents);
    /// Compares content to an expected hash.
    PatchVerificationResult Verify(byte[] contents, string expectedSha256);
}

/// Journal state for one patch transaction.
public enum PatchTransactionState
{
    /// No transaction has been recorded.
    None,
    /// Original content was verified.
    VerifiedOriginal,
    /// Backup was created and verified.
    BackupCreated,
    /// Replacement write is in progress.
    Writing,
    /// Replacement content was verified.
    Applied,
    /// Rollback is in progress.
    RollingBack,
    /// Original content was restored and verified.
    RolledBack,
    /// Transaction failed and requires inspection.
    Failed,
    /// Transaction failed and the engine could not restore or verify a known-safe target state.
    RecoveryFailed
}

/// Immutable journal snapshot for diagnostics and recovery.
public sealed class PatchTransactionRecord
{
    /// Creates a journal snapshot.
    public PatchTransactionRecord(PatchId id, PluginId owner, PatchTransactionState state, string? error = null)
    {
        Id = id;
        Owner = owner;
        State = state;
        Error = error;
    }

    /// Patch identifier.
    public PatchId Id { get; }
    /// Recorded owner.
    public PluginId Owner { get; }
    /// Latest transaction state.
    public PatchTransactionState State { get; }
    /// Failure detail, when present.
    public string? Error { get; }
}

/// Storage boundary for transaction records.
public interface IPatchJournal
{
    /// Gets the latest record for a patch.
    PatchTransactionRecord? Get(PatchId id);
    /// Returns immutable snapshots used by host startup recovery.
    IReadOnlyList<PatchTransactionRecord> GetAll();
    /// Publishes a new immutable transaction record.
    void Record(PatchTransactionRecord record);
}

/// Owner-bound transaction capability for one plugin's patches.
public interface IPatchEngine
{
    /// Plugin identity bound to this capability by the host.
    PluginId Owner { get; }
    /// Registers a patch and rejects ownership/target conflicts.
    void Register(PatchDefinition definition);
    /// Applies an owned patch.
    PatchTransactionRecord Apply(PatchId id);
    /// Rolls an owned patch back.
    PatchTransactionRecord Rollback(PatchId id);
    /// Returns the journaled state for a patch.
    PatchTransactionRecord? GetStatus(PatchId id);
}
