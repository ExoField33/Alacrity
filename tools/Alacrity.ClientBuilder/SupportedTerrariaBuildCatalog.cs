using Mono.Cecil;
using System.Security.Cryptography;

internal sealed class SupportedTerrariaBuild
{
    internal SupportedTerrariaBuild(string id, string version, string sha256, string patchCatalogId)
    {
        Id = id;
        Version = version;
        Sha256 = sha256;
        PatchCatalogId = patchCatalogId;
    }

    internal string Id { get; }
    internal string Version { get; }
    internal string Sha256 { get; }
    internal string PatchCatalogId { get; }
}

internal static class SupportedTerrariaBuildCatalog
{
    internal const string Terraria1456Sha256 = "A89A24C6531D88A972662821044ACF1B3B5817621DD6C81D4BD7523BC4BBDDA9";

    private static readonly SupportedTerrariaBuild[] Builds =
    {
        new SupportedTerrariaBuild("terraria-1.4.5.6-steam-win32", "1.4.5.6", Terraria1456Sha256, PermanentPatchCatalog.Identity)
    };

    internal static SupportedTerrariaBuild ValidateSource(string sourceExecutablePath)
    {
        if (!File.Exists(sourceExecutablePath))
        {
            throw new ClientBuildException("Clean Terraria executable was not found: " + sourceExecutablePath);
        }

        var hash = ComputeSha256(sourceExecutablePath);
        for (var index = 0; index < Builds.Length; index++)
        {
            if (string.Equals(Builds[index].Sha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                using var module = ModuleDefinition.ReadModule(sourceExecutablePath);
                var version = module.Assembly.Name.Version?.ToString() ?? "unknown";
                if (!string.Equals(version, Builds[index].Version, StringComparison.Ordinal))
                {
                    throw new ClientBuildException("Terraria source hash matched " + Builds[index].Id + " but the assembly version was " + version + ", expected " + Builds[index].Version + ". Reacquire a clean supported executable.");
                }

                return Builds[index];
            }
        }

        throw new ClientBuildException("Unsupported clean Terraria source SHA-256 " + hash + ". This builder only patches the audited Terraria 1.4.5.6 source build.");
    }

    internal static void WriteInspection(string sourceExecutablePath, TextWriter writer)
    {
        if (!File.Exists(sourceExecutablePath))
        {
            throw new ClientBuildException("Terraria executable was not found: " + sourceExecutablePath);
        }

        using var module = ModuleDefinition.ReadModule(sourceExecutablePath);
        var hash = ComputeSha256(sourceExecutablePath);
        writer.WriteLine("Assembly: " + module.Assembly.Name.Name);
        writer.WriteLine("Version: " + module.Assembly.Name.Version);
        writer.WriteLine("SHA-256: " + hash);
        writer.WriteLine("Supported: " + (FindByHash(hash) != null ? "yes" : "no"));
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return Convert.ToHexString(algorithm.ComputeHash(stream));
    }

    private static SupportedTerrariaBuild? FindByHash(string hash)
    {
        for (var index = 0; index < Builds.Length; index++)
        {
            if (string.Equals(Builds[index].Sha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                return Builds[index];
            }
        }

        return null;
    }
}
