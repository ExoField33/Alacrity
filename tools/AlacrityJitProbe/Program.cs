using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static int Main(string[] args)
    {
        string root = args.Length == 0 ? AppDomain.CurrentDomain.BaseDirectory : args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
        {
            var name = new AssemblyName(eventArgs.Name).Name + ".dll";
            string candidate = Path.Combine(root, "bin", name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };

        var assembly = Assembly.LoadFrom(Path.Combine(root, "Alacrity.exe"));
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
}
