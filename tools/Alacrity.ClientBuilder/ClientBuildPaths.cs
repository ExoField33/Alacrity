/// <summary>Validates manifest-derived paths before the builder copies, hashes, or deletes files.</summary>
internal static class ClientBuildPaths
{
    internal static string ResolveUnderRoot(string rootDirectory, string declaredPath, string description)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A root directory is required.", nameof(rootDirectory));
        }

        if (string.IsNullOrWhiteSpace(declaredPath))
        {
            throw new ClientBuildException(description + " is empty.");
        }

        string normalized = declaredPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new ClientBuildException(description + " must be a relative path: " + declaredPath + ".");
        }

        string root = Path.GetFullPath(rootDirectory);
        string candidate = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsStrictlyUnderRoot(candidate, root))
        {
            throw new ClientBuildException(description + " escapes its root directory: " + declaredPath + ".");
        }

        return candidate;
    }

    internal static string NormalizeRelativePath(string declaredPath, string description)
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-path-root");
        string candidate = ResolveUnderRoot(root, declaredPath, description);
        return Path.GetRelativePath(root, candidate).Replace('\\', '/');
    }

    private static bool IsStrictlyUnderRoot(string candidatePath, string rootPath)
    {
        string candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
