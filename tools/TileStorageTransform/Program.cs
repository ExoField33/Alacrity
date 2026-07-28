using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    private const string SupportedTerrariaHash = "A89A24C6531D88A972662821044ACF1B3B5817621DD6C81D4BD7523BC4BBDDA9";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 5 && string.Equals(args[0], "--copy", StringComparison.Ordinal))
                return CreateCopyOnlyOutput(args[1], args[2], args[3], args[4]);
            if (args.Length != 3)
                throw new ArgumentException("Usage: TileStorageTransform <Terraria.exe> <audit.json> <plan.json> | --copy <Terraria.exe> <audit.json> <plan.json> <output.exe>");

            string executablePath = Path.GetFullPath(args[0]);
            string auditPath = Path.GetFullPath(args[1]);
            string planPath = Path.GetFullPath(args[2]);
            VerifyInput(executablePath, auditPath);

            TileStorageAuditSnapshot audit = JsonSerializer.Deserialize<TileStorageAuditSnapshot>(File.ReadAllText(auditPath))
                ?? throw new InvalidOperationException("Tile-storage audit JSON was empty or invalid.");
            string hash = ComputeSha256(executablePath);
            if (!string.Equals(hash, audit.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The audit does not belong to the supplied Terraria.exe.");

            TileTransformationPlan plan = TileTransformationPlanner.CreatePlan(audit);
            string? directory = Path.GetDirectoryName(planPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("The plan output path must include a directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine($"Wrote {planPath}");
            if (!plan.CanTransform)
            {
                Console.Error.WriteLine("Tile transformation is blocked: " + string.Join("; ", plan.Blockers));
                return 2;
            }

            Console.WriteLine("Tile transformation plan is ready for a copy-only transformer.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Tile transformation planning failed: " + exception.Message);
            return 1;
        }
    }

    private static void VerifyInput(string executablePath, string auditPath)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Terraria.exe was not found.", executablePath);
        if (!string.Equals(Path.GetFileName(executablePath), "Terraria.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The input file must be named Terraria.exe.");
        if (!File.Exists(auditPath))
            throw new FileNotFoundException("Tile-storage audit report was not found.", auditPath);
        if (!string.Equals(ComputeSha256(executablePath), SupportedTerrariaHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The executable hash is not the verified Terraria 1.4.5.6 target.");
    }

    private static int CreateCopyOnlyOutput(string executableArgument, string auditArgument, string planArgument, string outputArgument)
    {
        string executablePath = Path.GetFullPath(executableArgument);
        string auditPath = Path.GetFullPath(auditArgument);
        string planPath = Path.GetFullPath(planArgument);
        string outputPath = Path.GetFullPath(outputArgument);
        VerifyInput(executablePath, auditPath);
        if (!File.Exists(planPath))
            throw new FileNotFoundException("Tile transformation plan was not found.", planPath);

        TileTransformationPlan plan = JsonSerializer.Deserialize<TileTransformationPlan>(File.ReadAllText(planPath))
            ?? throw new InvalidOperationException("Tile transformation plan was empty or invalid.");
        string sourceHash = ComputeSha256(executablePath);
        if (!string.Equals(plan.InputSha256, sourceHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The transformation plan does not belong to the supplied Terraria.exe.");
        if (!plan.CanTransform)
            throw new InvalidOperationException("The transformation plan is not ready. No output executable was created.");

        throw new InvalidOperationException("No verified tile IL lowerer is registered. No output executable was created.");
    }

    private static string ComputeSha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}

public sealed class TileStorageAuditSnapshot
{
    public string Sha256 { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, int> TileArrayGetStrategies { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<TileArrayGetFlowSnapshot> TileArrayGetFlows { get; init; } = Array.Empty<TileArrayGetFlowSnapshot>();
    public IReadOnlyList<TileMemberInstructionSnapshot> TileFieldInstructions { get; init; } = Array.Empty<TileMemberInstructionSnapshot>();
    public IReadOnlyList<TileMemberInstructionSnapshot> TileMethodInstructions { get; init; } = Array.Empty<TileMemberInstructionSnapshot>();
    public IReadOnlyList<TileMemberInstructionSnapshot> TileConstructorInstructions { get; init; } = Array.Empty<TileMemberInstructionSnapshot>();
    public IReadOnlyList<TileMemberInstructionSnapshot> TileBoxInstructions { get; init; } = Array.Empty<TileMemberInstructionSnapshot>();
    public IReadOnlyList<TileStoreSnapshot> TileStores { get; init; } = Array.Empty<TileStoreSnapshot>();
    public IReadOnlyList<TileNullCheckSnapshot> TileNullChecks { get; init; } = Array.Empty<TileNullCheckSnapshot>();
    public IReadOnlyList<string> UnsupportedTileArrayCalls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TileStoreSnapshot> UnclassifiedTileStores { get; init; } = Array.Empty<TileStoreSnapshot>();
    public IReadOnlyList<TileStackFlowSnapshot> TileStackFlows { get; init; } = Array.Empty<TileStackFlowSnapshot>();
    public IReadOnlyList<TileLocalAliasFlowSnapshot> TileLocalAliasFlows { get; init; } = Array.Empty<TileLocalAliasFlowSnapshot>();
    public IReadOnlyList<string> TileReferenceSignatures { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TileSignatureContractSnapshot> TileSignatureContracts { get; init; } = Array.Empty<TileSignatureContractSnapshot>();
    public IReadOnlyList<TileSignatureCallSnapshot> TileSignatureCalls { get; init; } = Array.Empty<TileSignatureCallSnapshot>();
    public IReadOnlyList<string> TileReferenceFields { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int> TileRuntimeTypeUses { get; init; } = new Dictionary<string, int>();

    public int GetStrategyCount(string strategy)
    {
        return TileArrayGetStrategies.TryGetValue(strategy, out int count) ? count : 0;
    }
}

public sealed class TileArrayGetFlowSnapshot
{
    public string Location { get; init; } = string.Empty;
    public string Consumer { get; init; } = string.Empty;
    public string Strategy { get; init; } = string.Empty;
}

public sealed class TileMemberInstructionSnapshot
{
    public string Location { get; init; } = string.Empty;
    public string OpCode { get; init; } = string.Empty;
    public string Member { get; init; } = string.Empty;
}

public sealed class TileStoreSnapshot
{
    public string Location { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Producer { get; init; } = string.Empty;
}

public sealed class TileNullCheckSnapshot
{
    public string Location { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string BranchOffset { get; init; } = string.Empty;
}

public sealed class TileStackFlowSnapshot
{
    public string Location { get; init; } = string.Empty;
    public IReadOnlyList<string> Outcomes { get; init; } = Array.Empty<string>();
    public bool StateLimitReached { get; init; }
}

public sealed class TileLocalAliasFlowSnapshot
{
    public string Location { get; init; } = string.Empty;
    public int Local { get; init; }
    public IReadOnlyList<string> UseKinds { get; init; } = Array.Empty<string>();
}

public sealed class TileSignatureContractSnapshot
{
    public string Method { get; init; } = string.Empty;
    public bool ReturnsTile { get; init; }
    public IReadOnlyList<TileParameterContractSnapshot> TileParameters { get; init; } = Array.Empty<TileParameterContractSnapshot>();
    public bool HasDirectTileFieldWrite { get; init; }
    public bool CallsTileMutator { get; init; }
    public bool HasNullLiteral { get; init; }
}

public sealed class TileSignatureCallSnapshot
{
    public string Location { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool CarriesTileReturn { get; init; }
    public int TileParameterCount { get; init; }
}

public sealed class TileParameterContractSnapshot
{
    public int Index { get; init; }
    public bool IsByReference { get; init; }
    public bool IsOut { get; init; }
    public bool IsIn { get; init; }
}

public static class TileTransformationPlanner
{
    public static TileTransformationPlan CreatePlan(TileStorageAuditSnapshot audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        TileLoweringPreflight preflight = TileLoweringPreflight.Evaluate(audit);
        var operations = new List<TileTransformationOperation>();
        foreach (TileArrayGetFlowSnapshot flow in audit.TileArrayGetFlows)
            operations.Add(Operation("TileArrayGet", flow.Location, flow.Strategy, flow.Consumer));
        foreach (TileMemberInstructionSnapshot fieldInstruction in audit.TileFieldInstructions)
            operations.Add(Operation("TileFieldInstruction", fieldInstruction.Location, fieldInstruction.OpCode, fieldInstruction.Member));
        foreach (TileMemberInstructionSnapshot methodInstruction in audit.TileMethodInstructions)
            operations.Add(Operation("TileMethodInstruction", methodInstruction.Location, methodInstruction.OpCode, methodInstruction.Member));
        foreach (TileMemberInstructionSnapshot constructorInstruction in audit.TileConstructorInstructions)
            operations.Add(Operation("TileConstructorInstruction", constructorInstruction.Location, constructorInstruction.OpCode, constructorInstruction.Member));
        foreach (TileMemberInstructionSnapshot boxInstruction in audit.TileBoxInstructions)
            operations.Add(Operation("TileBoxInstruction", boxInstruction.Location, boxInstruction.OpCode, boxInstruction.Member));
        foreach (TileStoreSnapshot store in audit.TileStores)
            operations.Add(Operation("TileArraySet", store.Location, $"StoreKind:{store.Kind}", store.Producer));
        foreach (TileNullCheckSnapshot nullCheck in audit.TileNullChecks)
            operations.Add(Operation("TileNullCheck", nullCheck.Location, $"NullCheckKind:{nullCheck.Kind}", nullCheck.BranchOffset));
        foreach (TileLocalAliasFlowSnapshot aliasFlow in audit.TileLocalAliasFlows)
            operations.Add(Operation("TileLocalAlias", aliasFlow.Location, "ByReferenceLocal", $"local {aliasFlow.Local}: {string.Join(", ", aliasFlow.UseKinds)}"));
        foreach (TileStackFlowSnapshot stackFlow in audit.TileStackFlows)
            operations.Add(Operation("TileStackFlow", stackFlow.Location, "ControlFlowDataflow", string.Join(", ", stackFlow.Outcomes), stackFlow.StateLimitReached));
        foreach (TileSignatureContractSnapshot contract in audit.TileSignatureContracts)
            operations.Add(Operation("TileSignature", contract.Method, DescribeContract(contract), "Method signature must receive a verified value/by-reference migration."));
        foreach (TileSignatureCallSnapshot call in audit.TileSignatureCalls)
            operations.Add(Operation("TileSignatureCall", call.Location, "CallContract", $"{call.Target}; return={call.CarriesTileReturn}; tileParameters={call.TileParameterCount}"));
        foreach (string field in audit.TileReferenceFields)
            operations.Add(Operation("TileReferenceField", field, "FieldContract", "Field use must preserve value or reference semantics."));
        foreach ((string runtimeUse, int count) in audit.TileRuntimeTypeUses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            operations.Add(Operation("TileRuntimeType", runtimeUse, "RuntimeContract", $"{count} occurrence(s) require an explicit struct-compatible rewrite."));
        foreach (string unsupported in audit.UnsupportedTileArrayCalls)
            operations.Add(Operation("UnsupportedArrayCall", unsupported, "Unsupported", "No transform may proceed until this call shape is handled."));
        foreach (TileStoreSnapshot unclassified in audit.UnclassifiedTileStores)
            operations.Add(Operation("UnclassifiedStore", unclassified.Location, "Unsupported", unclassified.Producer));

        TileTransformationOperation[] orderedOperations = operations
            .OrderBy(operation => operation.Category, StringComparer.Ordinal)
            .ThenBy(operation => operation.Source, StringComparer.Ordinal)
            .ThenBy(operation => operation.Strategy, StringComparer.Ordinal)
            .Select((operation, index) => operation with { Id = $"TS-{index + 1:D6}" })
            .ToArray();
        var blockers = new List<string>();
        foreach (string violation in preflight.Violations)
            blockers.Add("Unsupported lowering shape: " + violation);
        if (orderedOperations.Length == 0)
            blockers.Add("The audit did not contain a complete operation ledger.");
        foreach (IGrouping<string, TileTransformationOperation> group in orderedOperations.Where(operation => !operation.IsLowered).GroupBy(operation => operation.Category, StringComparer.Ordinal))
            blockers.Add($"{group.Key} operations without verified lowering: {group.Count()}");

        return new TileTransformationPlan
        {
            SchemaVersion = 2,
            InputSha256 = audit.Sha256,
            CanTransform = blockers.Count == 0,
            Blockers = blockers,
            Preflight = preflight,
            Operations = orderedOperations,
            Rules = new[]
            {
                "Only an operation with an exact version-locked lowering may be emitted into a transformed executable.",
                "A Tile array read must become addressable access to the active compact map, never a detached value copy.",
                "A null check must become a materialization check with its original branch polarity preserved.",
                "Every Tile parameter, return, local, field, and runtime-type use requires an explicit value/by-reference contract.",
                "Any new, unrecognized, ambiguous, or unlowered operation blocks transformation."
            }
        };
    }

    private static TileTransformationOperation Operation(string category, string source, string strategy, string detail, bool stateLimitReached = false)
    {
        return new TileTransformationOperation
        {
            Category = category,
            Source = source,
            Strategy = strategy,
            Detail = detail,
            IsLowered = false,
            StateLimitReached = stateLimitReached
        };
    }

    private static string DescribeContract(TileSignatureContractSnapshot contract)
    {
        var parts = new List<string>();
        if (contract.ReturnsTile)
            parts.Add("return");
        if (contract.TileParameters.Count != 0)
            parts.Add($"parameters {string.Join(", ", contract.TileParameters.Select(parameter => parameter.IsByReference ? $"{parameter.Index}&" : parameter.Index.ToString()))}");
        if (contract.HasDirectTileFieldWrite)
            parts.Add("writes fields");
        if (contract.CallsTileMutator)
            parts.Add("calls mutator");
        if (contract.HasNullLiteral)
            parts.Add("has null semantics");
        return string.Join("; ", parts);
    }
}

public sealed class TileLoweringPreflight
{
    private static readonly HashSet<string> SupportedFieldOpcodes = new(StringComparer.Ordinal)
    {
        "Ldfld", "Stfld"
    };

    private static readonly HashSet<string> SupportedMethodOpcodes = new(StringComparer.Ordinal)
    {
        "Call", "Callvirt"
    };

    private static readonly HashSet<string> SupportedNullCheckKinds = new(StringComparer.Ordinal)
    {
        "DirectBranchTrue", "DirectBranchFalse", "DirectNullComparison", "LocalBranchTrue", "LocalBranchFalse", "LocalNullComparison"
    };

    private static readonly HashSet<string> SupportedStackOutcomes = new(StringComparer.Ordinal)
    {
        "FieldRead", "FieldWrite", "TileMethodRead", "TileMethodMutation", "TileParameterEscape", "ArgumentEscape", "FieldEscape", "IndirectEscape", "DiscardedTileValue"
    };

    public IReadOnlyList<string> Violations { get; init; } = Array.Empty<string>();

    public static TileLoweringPreflight Evaluate(TileStorageAuditSnapshot audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        var violations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (TileMemberInstructionSnapshot instruction in audit.TileFieldInstructions)
        {
            if (!SupportedFieldOpcodes.Contains(instruction.OpCode))
                violations.Add($"field instruction {instruction.OpCode} at {instruction.Location}");
        }
        foreach (string member in TileFieldLoweringCatalog.FindUnsupportedMembers(audit.TileFieldInstructions))
            violations.Add($"field without compact lowering {member}");
        foreach (TileMemberInstructionSnapshot instruction in audit.TileMethodInstructions)
        {
            if (!SupportedMethodOpcodes.Contains(instruction.OpCode))
                violations.Add($"method instruction {instruction.OpCode} at {instruction.Location}");
        }
        foreach (string member in TileMethodLoweringCatalog.FindUnsupportedMembers(audit.TileMethodInstructions))
            violations.Add($"method without compact lowering {member}");
        foreach (TileMemberInstructionSnapshot instruction in audit.TileConstructorInstructions)
        {
            if (!string.Equals(instruction.OpCode, "Newobj", StringComparison.Ordinal) ||
                (!instruction.Member.EndsWith("::.ctor()", StringComparison.Ordinal) &&
                 !instruction.Member.EndsWith("::.ctor(Terraria.Tile)", StringComparison.Ordinal)))
            {
                violations.Add($"constructor instruction {instruction.OpCode}:{instruction.Member} at {instruction.Location}");
            }
        }
        foreach (TileNullCheckSnapshot nullCheck in audit.TileNullChecks)
        {
            if (!SupportedNullCheckKinds.Contains(nullCheck.Kind))
                violations.Add($"null check {nullCheck.Kind} at {nullCheck.Location}");
        }
        foreach (TileStackFlowSnapshot stackFlow in audit.TileStackFlows)
        {
            if (stackFlow.StateLimitReached)
                violations.Add($"stack analysis limit reached at {stackFlow.Location}");
            foreach (string outcome in stackFlow.Outcomes)
            {
                if (!SupportedStackOutcomes.Contains(outcome))
                    violations.Add($"stack outcome {outcome} at {stackFlow.Location}");
            }
        }
        foreach (TileMemberInstructionSnapshot boxInstruction in audit.TileBoxInstructions)
            violations.Add($"tile boxing at {boxInstruction.Location}");

        return new TileLoweringPreflight { Violations = violations.ToArray() };
    }
}

public sealed class TileTransformationPlan
{
    public int SchemaVersion { get; init; }
    public string InputSha256 { get; init; } = string.Empty;
    public bool CanTransform { get; init; }
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
    public TileLoweringPreflight Preflight { get; init; } = new();
    public IReadOnlyList<TileTransformationOperation> Operations { get; init; } = Array.Empty<TileTransformationOperation>();
    public IReadOnlyList<string> Rules { get; init; } = Array.Empty<string>();
}

public sealed record TileTransformationOperation
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Strategy { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool IsLowered { get; init; }
    public bool StateLimitReached { get; init; }
}

public sealed class CopyOnlyPatchTransaction
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly string _expectedInputHash;

    public CopyOnlyPatchTransaction(string inputPath, string outputPath, string expectedInputHash)
    {
        _inputPath = Path.GetFullPath(inputPath ?? throw new ArgumentNullException(nameof(inputPath)));
        _outputPath = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
        _expectedInputHash = expectedInputHash ?? throw new ArgumentNullException(nameof(expectedInputHash));
    }

    public CopyOnlyPatchReceipt Commit(Action<string> transformStagingCopy)
    {
        ArgumentNullException.ThrowIfNull(transformStagingCopy);
        if (!File.Exists(_inputPath))
            throw new FileNotFoundException("The source executable was not found.", _inputPath);
        if (PathsEqual(_inputPath, _outputPath))
            throw new InvalidOperationException("The output executable must be a separate copy.");
        if (File.Exists(_outputPath))
            throw new IOException("The output executable already exists and will not be overwritten.");
        if (!string.Equals(ComputeSha256(_inputPath), _expectedInputHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The source executable hash changed before the copy transaction started.");

        string? outputDirectory = Path.GetDirectoryName(_outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
            throw new InvalidOperationException("The output executable must have a parent directory.");
        Directory.CreateDirectory(outputDirectory);
        string stagingPath = Path.Combine(outputDirectory, $".{Path.GetFileName(_outputPath)}.{Guid.NewGuid():N}.staging");
        try
        {
            File.Copy(_inputPath, stagingPath, overwrite: false);
            transformStagingCopy(stagingPath);
            string outputHash = ComputeSha256(stagingPath);
            File.Move(stagingPath, _outputPath);
            return new CopyOnlyPatchReceipt(_inputPath, _outputPath, _expectedInputHash, outputHash);
        }
        finally
        {
            if (File.Exists(stagingPath))
                File.Delete(stagingPath);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}

public sealed class CopyOnlyPatchReceipt
{
    internal CopyOnlyPatchReceipt(string inputPath, string outputPath, string inputHash, string outputHash)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
        InputHash = inputHash;
        OutputHash = outputHash;
    }

    public string InputPath { get; }
    public string OutputPath { get; }
    public string InputHash { get; }
    public string OutputHash { get; }

    public void RollbackOutput()
    {
        if (!File.Exists(OutputPath))
            return;
        string currentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(OutputPath)));
        if (!string.Equals(currentHash, OutputHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The output executable changed after the transaction and will not be deleted.");
        File.Delete(OutputPath);
    }
}
