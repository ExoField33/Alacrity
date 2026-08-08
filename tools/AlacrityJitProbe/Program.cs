using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static Assembly terrariaAssembly;

    private static int Main(string[] args)
    {
        string root = args.Length == 0 ? AppDomain.CurrentDomain.BaseDirectory : args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
        {
            var name = new AssemblyName(eventArgs.Name).Name + ".dll";
            string candidate = Path.Combine(root, "bin", name);
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }

            return LoadEmbeddedTerrariaDependency(name);
        };

        var assembly = Assembly.LoadFrom(Path.Combine(root, "Alacrity.exe"));
        terrariaAssembly = assembly;
        var targets = new[]
        {
            new { Type = "Terraria.Main", Methods = new[] { "GetInputText", "DrawPlayerChat" } },
            new { Type = "Terraria.UI.Chat.TextSnippet", Methods = new[] { "OnHover", "OnClick", "GetVisibleColor" } },
            new { Type = "Terraria.UI.Chat.ChatManager", Methods = new[] { "ParseMessage" } }
        };
        foreach (var target in targets)
        {
            Type type = assembly.GetType(target.Type, true);
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(method => target.Methods.Contains(method.Name)).OrderBy(method => method.MetadataToken))
            {
                if (method.ContainsGenericParameters || method.GetMethodBody() == null)
                    continue;
                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(type.FullName + "::" + method.Name + " token=0x" + method.MetadataToken.ToString("X8"));
                    Console.Error.WriteLine(exception);
                    return 1;
                }
            }
        }

        Console.WriteLine("All non-generic methods prepared.");
        return 0;
    }

    private static Assembly LoadEmbeddedTerrariaDependency(string requestedFileName)
    {
        if (terrariaAssembly == null || !string.Equals(requestedFileName, "ReLogic.dll", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string resourceName = terrariaAssembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".ReLogic.dll", StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            return null;
        }

        using (Stream stream = terrariaAssembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                return null;
            }

            if (stream.Length > int.MaxValue)
            {
                throw new InvalidOperationException("The embedded ReLogic resource is too large to load into a managed assembly image.");
            }

            var bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("The embedded ReLogic resource ended before it could be loaded.");
                }

                offset += read;
            }

            return Assembly.Load(bytes);
        }
    }
}
