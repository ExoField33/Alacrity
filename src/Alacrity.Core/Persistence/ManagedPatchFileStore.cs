using System;
using System.IO;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host file store that confines patch paths to one non-reparse-point directory tree.</summary>
public sealed class ManagedPatchFileStore : IPatchFileStore
{
    private readonly object gate = new object();
    private readonly string root;

    public ManagedPatchFileStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A managed patch root is required.", nameof(rootPath));

        root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The managed patch root does not exist: " + root);
        EnsureNotReparsePoint(root);
    }

    public string GetPathIdentity(string path)
    {
        return Resolve(path).ToUpperInvariant();
    }

    public bool Exists(string path)
    {
        return File.Exists(Resolve(path));
    }

    public byte[] ReadAllBytes(string path)
    {
        return File.ReadAllBytes(Resolve(path));
    }

    public bool TryWriteAtomically(string path, byte[]? expectedContents, byte[] contents)
    {
        if (contents == null)
            throw new ArgumentNullException(nameof(contents));

        lock (gate)
        {
            string target = Resolve(path);
            EnsureExistingParent(target);
            byte[]? current = File.Exists(target) ? File.ReadAllBytes(target) : null;
            if (!SnapshotsMatch(current, expectedContents))
                return false;

            string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".alacrity-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, contents);
                if (current == null)
                    File.Move(temporary, target);
                else
                    File.Replace(temporary, target, null);
                return true;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    public void Copy(string sourcePath, string destinationPath, bool overwrite)
    {
        lock (gate)
        {
            string source = Resolve(sourcePath);
            string destination = Resolve(destinationPath);
            EnsureExistingParent(destination);
            File.Copy(source, destination, overwrite);
        }
    }

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new UnauthorizedAccessException("Patch paths must be non-empty paths relative to the managed patch root.");

        string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Patch paths must remain inside the managed patch root.");

        EnsureExistingSegmentsAreNotReparsePoints(candidate);
        return candidate;
    }

    private void EnsureExistingSegmentsAreNotReparsePoints(string candidate)
    {
        EnsureNotReparsePoint(root);
        string relative = candidate.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string current = root;
        foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
                EnsureNotReparsePoint(current);
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Reparse points are not permitted in the managed patch tree: " + path);
    }

    private static void EnsureExistingParent(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("Patch parent directory does not exist: " + parent);
    }

    private static bool SnapshotsMatch(byte[]? current, byte[]? expected)
    {
        if (current == null || expected == null)
            return current == null && expected == null;
        if (current.Length != expected.Length)
            return false;
        for (int i = 0; i < current.Length; i++)
        {
            if (current[i] != expected[i])
                return false;
        }
        return true;
    }
}
