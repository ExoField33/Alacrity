using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace AlacrityTerraria
{
    /// <summary>
    /// Validates the builder-owned deployment manifest before the facade loads the mutable core
    /// bridge. Runtime staging validates build inputs; this validates the final deployed copy.
    /// </summary>
    internal static class ClientManifestIntegrity
    {
        private const int SupportedFormatVersion = 1;

        internal static bool TryValidate(string clientDirectory, string expectedHandshake, out string diagnostic)
        {
            try
            {
                string manifestPath = Path.Combine(clientDirectory, "alacrity-client-manifest.json");
                if (!File.Exists(manifestPath))
                {
                    diagnostic = "Client manifest is missing. Rebuild and deploy the complete Alacrity client.";
                    return false;
                }

                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(manifestPath)) as Dictionary<string, object>;
                if (root == null)
                {
                    diagnostic = "Client manifest has an invalid root object.";
                    return false;
                }

                if (!TryReadInt(root, "formatVersion", out int formatVersion))
                {
                    diagnostic = "Client manifest formatVersion is missing or malformed. Rebuild and deploy the complete Alacrity client.";
                    return false;
                }

                if (formatVersion != SupportedFormatVersion)
                {
                    diagnostic = "Client manifest formatVersion " + formatVersion + " is unsupported; this client requires formatVersion " + SupportedFormatVersion + ". Rebuild and deploy the complete Alacrity client.";
                    return false;
                }

                if (!TryReadString(root, "bridgeHandshake", out string handshake) || !string.Equals(handshake, expectedHandshake, StringComparison.Ordinal))
                {
                    diagnostic = "Client manifest bridge handshake does not match this facade. Rebuild/copy Alacrity assemblies together.";
                    return false;
                }

                if (!TryReadString(root, "outputExecutableSha256", out string executableHash))
                {
                    diagnostic = "Client manifest does not contain the generated executable hash.";
                    return false;
                }

                string executablePath = Path.Combine(clientDirectory, "Alacrity.exe");
                if (!File.Exists(executablePath) || !string.Equals(ComputeSha256(executablePath), executableHash, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostic = "Alacrity.exe failed integrity validation. Rebuild and deploy the complete client.";
                    return false;
                }

                if (!root.TryGetValue("runtimeFiles", out object filesValue) || !(filesValue is IEnumerable files))
                {
                    diagnostic = "Client manifest does not contain its runtime file integrity list.";
                    return false;
                }

                foreach (object entry in files)
                {
                    if (!(entry is Dictionary<string, object> file) ||
                        !TryReadString(file, "Path", out string relativePath) ||
                        !TryReadString(file, "Sha256", out string expectedHash))
                    {
                        diagnostic = "Client manifest contains an invalid runtime file entry.";
                        return false;
                    }

                    string path = ResolveUnderClientDirectory(clientDirectory, relativePath);
                    if (!File.Exists(path) || !string.Equals(ComputeSha256(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostic = "Client runtime file failed integrity validation: " + relativePath + ". Rebuild and deploy the complete client.";
                        return false;
                    }
                }

                diagnostic = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "Client manifest validation failed: " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool TryReadString(IDictionary<string, object> values, string name, out string value)
        {
            if (values.TryGetValue(name, out object raw) && raw is string text && !string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool TryReadInt(IDictionary<string, object> values, string name, out int value)
        {
            if (!values.TryGetValue(name, out object raw) || raw == null)
            {
                value = 0;
                return false;
            }

            if (raw is int integer)
            {
                value = integer;
                return true;
            }

            if (raw is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                value = (int)longValue;
                return true;
            }

            if (raw is decimal decimalValue && decimal.Truncate(decimalValue) == decimalValue && decimalValue >= int.MinValue && decimalValue <= int.MaxValue)
            {
                value = (int)decimalValue;
                return true;
            }

            value = 0;
            return false;
        }

        private static string ResolveUnderClientDirectory(string clientDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException("Runtime file path is invalid.");
            }

            string root = Path.GetFullPath(clientDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Path.Combine(clientDirectory, relativePath));
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Runtime file path escapes the client directory.");
            }

            return resolved;
        }

        private static string ComputeSha256(string path)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
