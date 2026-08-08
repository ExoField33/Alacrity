using System.Globalization;

internal static class ClientBuilderCommandLine
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                WriteUsage();
                return args.Length == 0 ? 1 : 0;
            }

            var command = args[0];
            if (string.Equals(command, "validate", StringComparison.OrdinalIgnoreCase))
            {
                var source = RequireOption(args, "--source");
                SupportedTerrariaBuildCatalog.ValidateSource(source);
                Console.WriteLine("Supported Terraria source: " + Path.GetFullPath(source));
                return 0;
            }

            if (string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase))
            {
                var source = RequireOption(args, "--source");
                SupportedTerrariaBuildCatalog.WriteInspection(source, Console.Out);
                return 0;
            }

            if (!string.Equals(command, "generate", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Unknown command: " + command);
                WriteUsage();
                return 1;
            }

            var sourcePath = RequireOption(args, "--source");
            var runtimePath = RequireOption(args, "--runtime");
            var outputPath = GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "client");
            var deploy = HasSwitch(args, "--deploy");
            var verbose = HasSwitch(args, "--verbose");
            var clean = HasSwitch(args, "--clean");
            if (clean && deploy)
            {
                throw new ClientBuildException("--clean is only valid for the pipeline-owned generated output; it cannot be used with --deploy.");
            }

            var result = ClientBuildPipeline.Generate(new ClientBuildOptions(sourcePath, runtimePath, outputPath, deploy, clean, verbose));
            Console.WriteLine("Generated Alacrity client: " + result.OutputDirectory);
            Console.WriteLine("Patch catalog: " + result.Manifest.PatchCatalogId);
            Console.WriteLine("Output hash: " + result.Manifest.OutputExecutableSha256);
            return 0;
        }
        catch (ClientBuildException exception)
        {
            Console.Error.WriteLine("Alacrity client generation failed: " + exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Alacrity client generation failed unexpectedly: " + exception);
            return 3;
        }
    }

    private static bool IsHelp(string value) => value == "--help" || value == "-h" || value == "help";

    private static bool HasSwitch(IReadOnlyList<string> args, string name)
    {
        for (var index = 1; index < args.Count; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string RequireOption(IReadOnlyList<string> args, string name)
    {
        return GetOption(args, name) ?? throw new ClientBuildException("Missing required option " + name + ".");
    }

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 1; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Alacrity.ClientBuilder");
        Console.WriteLine("  generate --source <clean Terraria.exe> --runtime <artifacts/runtime> [--output <directory>] [--deploy] [--clean] [--verbose]");
        Console.WriteLine("  validate --source <clean Terraria.exe>");
        Console.WriteLine("  inspect --source <Terraria.exe>");
        Console.WriteLine("\n--deploy updates only pipeline-owned runtime files in an explicit existing client directory.");
    }
}
