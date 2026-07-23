using System;
using System.Collections.Generic;

namespace Alacrity.PluginSdk;

/// <summary>Stable identifier for one centrally owned patch.</summary>
public readonly struct PatchId : IEquatable<PatchId>
{
    /// <summary>Maximum number of characters in a package-safe patch identifier.</summary>
    public const int MaximumLength = 96;

    /// <summary>Creates a patch identifier.</summary>
    public PatchId(string value)
    {
        if (!TryValidate(value, out var error))
            throw new ArgumentException(error, nameof(value));
        Value = value;
    }

    /// <summary>Canonical patch identifier text.</summary>
    public string Value { get; }

    /// <summary>Whether this instance contains a valid non-default identifier.</summary>
    public bool IsValid => TryValidate(Value, out _);

    /// <summary>Parses a package-safe patch identifier.</summary>
    public static PatchId Parse(string value) => new PatchId(value);

    /// <summary>Attempts to parse a package-safe patch identifier.</summary>
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

    /// <summary>Compares two patch identifiers.</summary>
    public static bool operator ==(PatchId left, PatchId right) => left.Equals(right);

    /// <summary>Compares two patch identifiers for inequality.</summary>
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

/// <summary>
/// Immutable declaration of a full-file patch owned by one plugin. The host derives all backup
/// locations; plugin code never receives control of rollback paths.
/// </summary>
public sealed class PatchDefinition
{
    /// <summary>Creates a patch declaration. Contents are copied defensively.</summary>
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

    /// <summary>Patch identifier.</summary>
    public PatchId Id { get; }
    /// <summary>Plugin that owns registration, application, and rollback.</summary>
    public PluginId Owner { get; }
    /// <summary>Target file path within the host-managed patch root.</summary>
    public string TargetPath { get; }
    /// <summary>Hash required before application.</summary>
    public string ExpectedOriginalSha256 { get; }
    /// <summary>Replacement bytes written by the transaction.</summary>
    public byte[] ReplacementContents => (byte[])replacement.Clone();
    /// <summary>Hash required after application.</summary>
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

/// <summary>File operations needed by the patch engine.</summary>
public interface IPatchFileStore
{
    /// <summary>Returns a stable canonical comparison key for a managed path and rejects paths outside the managed root.</summary>
    string GetPathIdentity(string path);
    /// <summary>Checks whether a managed path exists.</summary>
    bool Exists(string path);
    /// <summary>Reads a complete file snapshot.</summary>
    byte[] ReadAllBytes(string path);
    /// <summary>Atomically writes only when the current file exactly matches the expected snapshot; a null expectation requires the path to remain absent.</summary>
    bool TryWriteAtomically(string path, byte[]? expectedContents, byte[] contents);
    /// <summary>Copies a file to a backup path.</summary>
    void Copy(string sourcePath, string destinationPath, bool overwrite);
}

/// <summary>Result of comparing a file snapshot to a declared patch hash.</summary>
public sealed class PatchVerificationResult
{
    /// <summary>Creates a verification result.</summary>
    public PatchVerificationResult(bool isMatch, string expectedSha256, string actualSha256)
    {
        IsMatch = isMatch;
        ExpectedSha256 = expectedSha256;
        ActualSha256 = actualSha256;
    }

    /// <summary>Whether expected and actual hashes match.</summary>
    public bool IsMatch { get; }
    /// <summary>Declared hash.</summary>
    public string ExpectedSha256 { get; }
    /// <summary>Observed hash.</summary>
    public string ActualSha256 { get; }
}

/// <summary>Verification service kept separate from file mutation.</summary>
public interface IPatchVerifier
{
    /// <summary>Computes the canonical SHA-256 representation.</summary>
    string ComputeSha256(byte[] contents);
    /// <summary>Compares content to an expected hash.</summary>
    PatchVerificationResult Verify(byte[] contents, string expectedSha256);
}

/// <summary>Journal state for one patch transaction.</summary>
public enum PatchTransactionState
{
    /// <summary>No transaction has been recorded.</summary>
    None,
    /// <summary>Original content was verified.</summary>
    VerifiedOriginal,
    /// <summary>Backup was created and verified.</summary>
    BackupCreated,
    /// <summary>Replacement write is in progress.</summary>
    Writing,
    /// <summary>Replacement content was verified.</summary>
    Applied,
    /// <summary>Rollback is in progress.</summary>
    RollingBack,
    /// <summary>Original content was restored and verified.</summary>
    RolledBack,
    /// <summary>Transaction failed and requires inspection.</summary>
    Failed,
    /// <summary>Transaction failed and the engine could not restore or verify a known-safe target state.</summary>
    RecoveryFailed
}

/// <summary>Immutable journal snapshot for diagnostics and recovery.</summary>
public sealed class PatchTransactionRecord
{
    /// <summary>Creates a journal snapshot.</summary>
    public PatchTransactionRecord(PatchId id, PluginId owner, PatchTransactionState state, string? error = null)
    {
        Id = id;
        Owner = owner;
        State = state;
        Error = error;
    }

    /// <summary>Patch identifier.</summary>
    public PatchId Id { get; }
    /// <summary>Recorded owner.</summary>
    public PluginId Owner { get; }
    /// <summary>Latest transaction state.</summary>
    public PatchTransactionState State { get; }
    /// <summary>Failure detail, when present.</summary>
    public string? Error { get; }
}

/// <summary>Storage boundary for transaction records.</summary>
public interface IPatchJournal
{
    /// <summary>Gets the latest record for a patch.</summary>
    PatchTransactionRecord? Get(PatchId id);
    /// <summary>Returns immutable snapshots used by host startup recovery.</summary>
    IReadOnlyList<PatchTransactionRecord> GetAll();
    /// <summary>Publishes a new immutable transaction record.</summary>
    void Record(PatchTransactionRecord record);
}

/// <summary>Owner-bound transaction capability for one plugin's patches.</summary>
public interface IPatchEngine
{
    /// <summary>Plugin identity bound to this capability by the host.</summary>
    PluginId Owner { get; }
    /// <summary>Registers a patch and rejects ownership/target conflicts.</summary>
    void Register(PatchDefinition definition);
    /// <summary>Applies an owned patch.</summary>
    PatchTransactionRecord Apply(PatchId id);
    /// <summary>Rolls an owned patch back.</summary>
    PatchTransactionRecord Rollback(PatchId id);
    /// <summary>Returns the journaled state for a patch.</summary>
    PatchTransactionRecord? GetStatus(PatchId id);
}
