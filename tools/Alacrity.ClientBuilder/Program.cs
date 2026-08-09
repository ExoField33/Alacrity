/// <summary>Small command-line entry point for the authoritative client-generation pipeline.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        return ClientBuilderCommandLine.Run(args);
    }
}
