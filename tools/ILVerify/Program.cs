// See https://aka.ms/new-console-template for more information
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text;
using ILVerify;

if (args.Length is not 1 and not 2 || (args.Length == 2 && args[1] != "--invoke-main"))
    throw new ArgumentException("Expected one assembly path and an optional --invoke-main flag.");

using var resolver = new DirectoryResolver(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var target = resolver.Open(args[0]);
AssemblyNameInfo systemModuleName = FindSystemModuleName(target.GetMetadataReader());
var verifier = new Verifier(resolver);
verifier.SetSystemModuleName(systemModuleName);
var output = new StringBuilder();
output.AppendLine($"ILVerify target={Path.GetFullPath(args[0])}");
output.AppendLine($"ILVerify system-module={DescribeAssemblyName(systemModuleName)}");
AppendMetadataShape(target, output);
foreach (VerificationResult result in verifier.Verify(target))
{
    var reader = target.GetMetadataReader();
    var typeHandle = GetMember<TypeDefinitionHandle>(result, "Type");
    var methodHandle = GetMember<MethodDefinitionHandle>(result, "Method");
    string typeName = NameOfType(reader, typeHandle);
    string methodName = NameOfMethod(reader, methodHandle);
    output.AppendLine(
        $"ILVERIFY result-code={Format(GetMember<object?>(result, "Code"))} " +
        $"type={typeName} method={methodName} token=0x{MetadataTokens.GetToken(methodHandle):X8} " +
        $"il-offset={FormatMember(result, "Offset")} " +
        $"found={FormatMember(result, "Found")} expected={FormatMember(result, "Expected")} " +
        $"error-arguments={FormatMember(result, "ErrorArguments")} args={FormatMember(result, "Args")} " +
        $"message={FormatMember(result, "Message")}");
}
Console.Write(output.ToString());
AppendReport(args[0], output.ToString());
if (args.Length == 2)
{
    Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
    Type main = assembly.GetType("TileStorageTransformFixture.Main", throwOnError: true)!;
    main.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { 2, 2 });
    Console.WriteLine("ILVerify invocation=Main.Initialize passed");
}

static AssemblyNameInfo FindSystemModuleName(MetadataReader reader)
{
    foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
    {
        AssemblyReference reference = reader.GetAssemblyReference(handle);
        string name = reader.GetString(reference.Name);
        if (string.Equals(name, "System.Runtime", StringComparison.Ordinal))
        {
            var identity = new AssemblyName
            {
                Name = name,
                Version = reference.Version,
                CultureName = reference.Culture.IsNil ? null : reader.GetString(reference.Culture)
            };
            byte[] keyOrToken = reader.GetBlobBytes(reference.PublicKeyOrToken);
            if ((reference.Flags & AssemblyFlags.PublicKey) != 0) identity.SetPublicKey(keyOrToken);
            else identity.SetPublicKeyToken(keyOrToken);
            return AssemblyNameInfo.Parse(identity.FullName!.AsSpan());
        }
    }

    return AssemblyNameInfo.Parse(typeof(object).Assembly.FullName!.AsSpan());
}

static string DescribeAssemblyName(AssemblyNameInfo assemblyName) => assemblyName.FullName ?? assemblyName.Name ?? "<unnamed>";

static void AppendMetadataShape(PEReader target, StringBuilder output)
{
    MetadataReader reader = target.GetMetadataReader();
    foreach (TypeReferenceHandle handle in reader.TypeReferences)
    {
        TypeReference type = reader.GetTypeReference(handle);
        output.AppendLine($"ILVERIFY typeref token=0x{MetadataTokens.GetToken(handle):X8} name={reader.GetString(type.Namespace)}.{reader.GetString(type.Name)} scope={MetadataTokens.GetToken(type.ResolutionScope):X8}");
    }

    foreach (MemberReferenceHandle handle in reader.MemberReferences)
    {
        MemberReference member = reader.GetMemberReference(handle);
        output.AppendLine($"ILVERIFY memberref token=0x{MetadataTokens.GetToken(handle):X8} name={reader.GetString(member.Name)} parent={MetadataTokens.GetToken(member.Parent):X8} signature={Convert.ToHexString(reader.GetBlobBytes(member.Signature))}");
    }

    foreach (FieldDefinitionHandle handle in reader.FieldDefinitions)
    {
        FieldDefinition field = reader.GetFieldDefinition(handle);
        output.AppendLine($"ILVERIFY field token=0x{MetadataTokens.GetToken(handle):X8} name={reader.GetString(field.Name)} signature={Convert.ToHexString(reader.GetBlobBytes(field.Signature))}");
    }

    for (int row = 1; row <= reader.GetTableRowCount(TableIndex.TypeSpec); row++)
    {
        TypeSpecificationHandle handle = MetadataTokens.TypeSpecificationHandle(row);
        TypeSpecification specification = reader.GetTypeSpecification(handle);
        output.AppendLine($"ILVERIFY typespec token=0x{MetadataTokens.GetToken(handle):X8} signature={Convert.ToHexString(reader.GetBlobBytes(specification.Signature))}");
    }

    foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        if (method.RelativeVirtualAddress == 0) continue;
        MethodBodyBlock body = target.GetMethodBody(method.RelativeVirtualAddress);
        output.AppendLine($"ILVERIFY method token=0x{MetadataTokens.GetToken(handle):X8} name={reader.GetString(method.Name)} signature={Convert.ToHexString(reader.GetBlobBytes(method.Signature))} il={Convert.ToHexString(body.GetILBytes() ?? Array.Empty<byte>())}");
    }
}

static T GetMember<T>(object instance, string name)
{
    object? value = instance.GetType().GetProperty(name)?.GetValue(instance)
        ?? instance.GetType().GetField(name)?.GetValue(instance);
    return value is T typed ? typed : default!;
}

static string FormatMember(object instance, string name) => Format(GetMember<object?>(instance, name));

static string NameOfType(MetadataReader reader, TypeDefinitionHandle handle)
{
    if (handle.IsNil) return "<nil-type>";
    try { TypeDefinition value = reader.GetTypeDefinition(handle); return reader.GetString(value.Namespace) + "." + reader.GetString(value.Name); } catch (BadImageFormatException) { return $"<invalid-type:0x{MetadataTokens.GetToken(handle):X8}>"; }
}

static string NameOfMethod(MetadataReader reader, MethodDefinitionHandle handle)
{
    if (handle.IsNil) return "<nil-method>";
    try { return reader.GetString(reader.GetMethodDefinition(handle).Name); } catch (BadImageFormatException) { return $"<invalid-method:0x{MetadataTokens.GetToken(handle):X8}>"; }
}

static string Format(object? value)
{
    if (value is null) return "<null>";
    if (value is System.Collections.IEnumerable values && value is not string)
        return "[" + string.Join(",", values.Cast<object?>().Select(Format)) + "]";
    if (value.GetType().Namespace == "ILVerify")
        return value.ToString() is { Length: > 0 } text && text != "{}" ? text : value.GetType().Name;
    return value.ToString() ?? "<null>";
}

static void AppendReport(string targetPath, string output)
{
    string? directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
    if (directory is null) return;
    string reportPath = Path.Combine(directory, "tile-transform-metadata-report.txt");
    if (!File.Exists(reportPath)) return;
    File.AppendAllText(reportPath, Environment.NewLine + output);
}

sealed class DirectoryResolver : IResolver, IDisposable
{
    private readonly Dictionary<string, PEReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _directories;

    public DirectoryResolver(string targetDirectory)
    {
        _directories = new[] { targetDirectory }
            .Concat(FindReferenceDirectories())
            .Concat(FindRuntimeDirectories())
            .Append(RuntimeEnvironment.GetRuntimeDirectory())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public PEReader ResolveAssembly(AssemblyNameInfo assemblyName) => ResolveAssemblyIdentity(assemblyName);
    public PEReader ResolveModule(AssemblyNameInfo assemblyName, string moduleName) => Resolve(moduleName);
    public PEReader Open(string path) => Add(path);

    private PEReader Resolve(string? name)
    {
        string fileName = (name ?? throw new FileNotFoundException("Assembly name missing."));
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) fileName += ".dll";
        foreach (string directory in _directories)
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return Add(candidate);
        }
        throw new FileNotFoundException($"Could not resolve {name}.");
    }

    private PEReader ResolveAssemblyIdentity(AssemblyNameInfo expected)
    {
        string fileName = (expected.Name ?? throw new FileNotFoundException("Assembly name missing.")) + ".dll";
        foreach (string directory in _directories)
        {
            string candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate)) continue;
            AssemblyNameInfo actual = AssemblyNameInfo.Parse(AssemblyName.GetAssemblyName(candidate).FullName!.AsSpan());
            if (MatchesIdentity(actual, expected)) return Add(candidate);
        }

        string candidates = string.Join(", ", _directories
            .Select(directory => Path.Combine(directory, fileName))
            .Where(File.Exists)
            .Select(candidate => candidate + "=" + DescribeIdentity(AssemblyNameInfo.Parse(AssemblyName.GetAssemblyName(candidate).FullName!.AsSpan()))));
        throw new FileNotFoundException($"Could not resolve assembly identity {DescribeIdentity(expected)}. Candidates: {candidates}");
    }

    private static string DescribeIdentity(AssemblyNameInfo identity)
    {
        return "{" + string.Join(",", identity.GetType().GetProperties()
            .Select(property => property.Name + "=" + property.GetValue(identity))) + "}";
    }

    private static bool MatchesIdentity(AssemblyNameInfo actual, AssemblyNameInfo expected)
    {
        return string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)
            && actual.Version == expected.Version
            && string.Equals(NormalizeCulture(actual.CultureName), NormalizeCulture(expected.CultureName), StringComparison.OrdinalIgnoreCase)
            && actual.Flags == expected.Flags
            && actual.PublicKeyOrToken.AsSpan().SequenceEqual(expected.PublicKeyOrToken.AsSpan());
    }

    private static string NormalizeCulture(string? culture) => string.IsNullOrEmpty(culture) || string.Equals(culture, "neutral", StringComparison.OrdinalIgnoreCase) ? string.Empty : culture;

    private PEReader Add(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (_readers.TryGetValue(fullPath, out PEReader? reader)) return reader;
        reader = new PEReader(File.OpenRead(fullPath));
        _readers.Add(fullPath, reader);
        return reader;
    }

    private static IEnumerable<string> FindReferenceDirectories()
    {
        string dotnetRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        string packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packRoot)) yield break;
        foreach (string versionDirectory in Directory.GetDirectories(packRoot))
        {
            string refDirectory = Path.Combine(versionDirectory, "ref");
            if (!Directory.Exists(refDirectory)) continue;
            foreach (string frameworkDirectory in Directory.GetDirectories(refDirectory))
                yield return frameworkDirectory;
        }
    }

    private static IEnumerable<string> FindRuntimeDirectories()
    {
        string dotnetRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        string sharedRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
        if (!Directory.Exists(sharedRoot)) yield break;
        foreach (string runtimeDirectory in Directory.GetDirectories(sharedRoot))
            yield return runtimeDirectory;
    }

    public void Dispose() { foreach (PEReader reader in _readers.Values) reader.Dispose(); }
}
