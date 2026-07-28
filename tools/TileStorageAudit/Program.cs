using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private const string SupportedTerrariaVersion = "1.4.5.6";
    private const string SupportedTerrariaHash = "A89A24C6531D88A972662821044ACF1B3B5817621DD6C81D4BD7523BC4BBDDA9";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && string.Equals(args[0], "--dump", StringComparison.Ordinal))
            {
                DumpMethod(args[1], args[2], args[3]);
                return 0;
            }

            bool requireTransformReadiness = args.Length > 0 && string.Equals(args[0], "--require-transform-ready", StringComparison.Ordinal);
            int argumentOffset = requireTransformReadiness ? 1 : 0;
            if (args.Length - argumentOffset is < 1 or > 2)
                throw new ArgumentException("Usage: TileStorageAudit <Terraria.exe> [report.json] | --require-transform-ready <Terraria.exe> [report.json] | --dump <Terraria.exe> <type> <method>");

            string executablePath = Path.GetFullPath(args[argumentOffset]);
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("Terraria.exe was not found.", executablePath);
            if (!string.Equals(Path.GetFileName(executablePath), "Terraria.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The input file must be named Terraria.exe.");

            string reportPath = args.Length - argumentOffset == 2
                ? Path.GetFullPath(args[argumentOffset + 1])
                : Path.Combine(Path.GetDirectoryName(executablePath)!, "tile-storage-audit.json");
            TileStorageAuditReport report = Audit(executablePath);
            ValidateExpectedSurface(report);
            if (requireTransformReadiness)
                ValidateTransformReadiness(report);

            string? directory = Path.GetDirectoryName(reportPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("The report path must include a directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));

            Console.WriteLine($"Verified Terraria {report.AssemblyVersion} ({report.Sha256}).");
            Console.WriteLine($"Wrote {reportPath}");
            Console.WriteLine($"Tile-array calls: Get={report.TileArrayCalls.Get}, Set={report.TileArrayCalls.Set}, Address={report.TileArrayCalls.Address}, allocations={report.TileArrayCalls.Constructor}.");
            Console.WriteLine($"Methods touching Main.tile: {report.MethodsTouchingMainTile}; tile constructors: {report.MethodsConstructingTile}.");
            Console.WriteLine($"Unsupported tile-array call shapes: {report.UnsupportedTileArrayCalls.Count}.");
            Console.WriteLine($"Unclassified Tile[,] store sources: {report.UnclassifiedTileStores.Count}.");
            Console.WriteLine($"Verified Tile null branches: {report.TileNullChecks.Count}.");
            Console.WriteLine($"Tile reference surface: fields={report.TileReferenceFields.Count}, signatures={report.TileReferenceSignatures.Count}, locals={report.TileTypedLocals}.");
            return report.UnsupportedTileArrayCalls.Count == 0 && report.UnclassifiedTileStores.Count == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Tile storage audit failed: {exception.Message}");
            return 1;
        }
    }

    private static void DumpMethod(string executableArgument, string typeName, string methodName)
    {
        string executablePath = Path.GetFullPath(executableArgument);
        ValidateTargetExecutable(executablePath);
        using ModuleDefinition module = ModuleDefinition.ReadModule(executablePath, new ReaderParameters
        {
            ReadSymbols = false,
            ReadingMode = ReadingMode.Deferred
        });

        TypeDefinition type = RequireType(module, typeName);
        MethodDefinition[] methods = type.Methods
            .Where(candidate => candidate.Name == methodName && candidate.HasBody)
            .ToArray();
        if (methods.Length == 0)
            throw new InvalidOperationException($"No method body named {typeName}.{methodName} was found.");

        foreach (MethodDefinition method in methods)
        {
            Console.WriteLine(method.FullName);
            foreach (Instruction instruction in method.Body.Instructions)
                Console.WriteLine($"  IL_{instruction.Offset:X4}: {DescribeInstruction(instruction)}");
        }
    }

    private static TileStorageAuditReport Audit(string executablePath)
    {
        string hash = ValidateTargetExecutable(executablePath);

        using ModuleDefinition module = ModuleDefinition.ReadModule(executablePath, new ReaderParameters
        {
            ReadSymbols = false,
            ReadingMode = ReadingMode.Deferred
        });
        if (!string.Equals(module.Assembly.Name.Version?.ToString(), SupportedTerrariaVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected Terraria {SupportedTerrariaVersion}, got {module.Assembly.Name.Version}.");

        TypeDefinition mainType = RequireType(module, "Terraria.Main");
        TypeDefinition tileType = RequireType(module, "Terraria.Tile");
        ValidateTileSemanticMethods(tileType);
        ValidateTileCriticalBoundaries(module);
        FieldDefinition mainTile = mainType.Fields.SingleOrDefault(field => field.Name == "tile")
            ?? throw new InvalidOperationException("Terraria.Main.tile was not found.");
        string tileArrayType = mainTile.FieldType.FullName;
        if (tileArrayType != "Terraria.Tile[0...,0...]")
            throw new InvalidOperationException($"Terraria.Main.tile has an unexpected type: {tileArrayType}.");

        var methods = new List<TileMethodAudit>();
        var tileArrayFields = new List<string>();
        var unsupportedCalls = new HashSet<string>(StringComparer.Ordinal);
        var tileStores = new List<TileStoreAudit>();
        var tileNullChecks = new List<TileNullCheckAudit>();
        var tileReferenceFields = new List<string>();
        var tileReferenceSignatures = new HashSet<string>(StringComparer.Ordinal);
        var tileFieldAccesses = new Dictionary<string, int>(StringComparer.Ordinal);
        var tileMethodCalls = new Dictionary<string, int>(StringComparer.Ordinal);
        var tileFieldInstructions = new List<TileMemberInstructionAudit>();
        var tileMethodInstructions = new List<TileMemberInstructionAudit>();
        var tileConstructorInstructions = new List<TileMemberInstructionAudit>();
        var tileBoxInstructions = new List<TileMemberInstructionAudit>();
        var tileArrayGetConsumers = new Dictionary<string, int>(StringComparer.Ordinal);
        var tileArrayGetConsumerSamples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tileArrayGetStrategies = new Dictionary<string, int>(StringComparer.Ordinal);
        var tileArrayGetStrategySamples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tileArrayGetFlows = new List<TileArrayGetFlowAudit>();
        var tileArrayGetResidualFlows = new List<TileArrayGetFlowAudit>();
        var tileStackFlows = new List<TileStackFlowAudit>();
        var tileRuntimeTypeUses = new Dictionary<string, int>(StringComparer.Ordinal);
        var tileSignatureContracts = new List<TileSignatureContractAudit>();
        var tileSignatureCalls = new List<TileSignatureCallAudit>();
        var tileLocalAliasFlows = new List<TileLocalAliasFlowAudit>();
        var tileLocalAliasUseKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var tileLocalAliasUseSamples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        HashSet<string> tileMutatingMethods = FindTileMutatingMethods(tileType);
        int tileTypedLocals = 0;
        foreach (TypeDefinition type in Flatten(module.Types))
        {
            foreach (FieldDefinition field in type.Fields)
            {
                if (field.FieldType.FullName == tileArrayType)
                    tileArrayFields.Add($"{type.FullName}::{field.Name}");
                if (IsTileType(field.FieldType, tileType.FullName))
                    tileReferenceFields.Add($"{type.FullName}::{field.Name}");
            }

            foreach (MethodDefinition method in type.Methods)
            {
                if (IsTileType(method.ReturnType, tileType.FullName))
                    tileReferenceSignatures.Add($"return {method.FullName}");
                foreach (ParameterDefinition parameter in method.Parameters)
                {
                    if (IsTileType(parameter.ParameterType, tileType.FullName))
                        tileReferenceSignatures.Add($"parameter {method.FullName}");
                }
                if (IsTileType(method.ReturnType, tileType.FullName) || method.Parameters.Any(parameter => IsTileType(parameter.ParameterType, tileType.FullName)))
                    tileSignatureContracts.Add(AuditTileSignatureContract(method, tileType.FullName, tileMutatingMethods));
                if (method.HasBody)
                {
                    tileTypedLocals += method.Body.Variables.Count(variable => IsTileType(variable.VariableType, tileType.FullName));
                    foreach (Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is FieldReference field && field.DeclaringType.FullName == tileType.FullName)
                        {
                            Increment(tileFieldAccesses, field.Name);
                            tileFieldInstructions.Add(CreateTileMemberInstructionAudit(method, instruction, field.FullName));
                        }
                        if (instruction.Operand is MethodReference called && called.DeclaringType.FullName == tileType.FullName)
                        {
                            Increment(tileMethodCalls, $"{called.Name}/{called.Parameters.Count}");
                            (called.Name == ".ctor" ? tileConstructorInstructions : tileMethodInstructions)
                                .Add(CreateTileMemberInstructionAudit(method, instruction, called.FullName));
                        }
                        if (instruction.OpCode == OpCodes.Box && instruction.Operand is TypeReference boxed && IsTileType(boxed, tileType.FullName))
                            tileBoxInstructions.Add(CreateTileMemberInstructionAudit(method, instruction, boxed.FullName));
                        if (instruction.Operand is MethodReference signatureCall &&
                            (IsTileType(signatureCall.ReturnType, tileType.FullName) || signatureCall.Parameters.Any(parameter => IsTileType(parameter.ParameterType, tileType.FullName))))
                        {
                            tileSignatureCalls.Add(new TileSignatureCallAudit
                            {
                                Location = $"{method.DeclaringType.FullName}::{method.Name}@IL_{instruction.Offset:X4}",
                                Target = signatureCall.FullName,
                                CarriesTileReturn = IsTileType(signatureCall.ReturnType, tileType.FullName),
                                TileParameterCount = signatureCall.Parameters.Count(parameter => IsTileType(parameter.ParameterType, tileType.FullName))
                            });
                        }
                        if (instruction.Operand is MethodReference arrayMethod && arrayMethod.DeclaringType.FullName == tileArrayType && arrayMethod.Name == "Get")
                        {
                            string consumer = DescribeTileArrayGetConsumer(NextNonNop(instruction));
                            Increment(tileArrayGetConsumers, consumer);
                            AddSample(tileArrayGetConsumerSamples, consumer, $"{method.DeclaringType.FullName}::{method.Name}@IL_{instruction.Offset:X4}");
                            string strategy = ClassifyTileArrayGetStrategy(method, instruction, tileType.FullName);
                            Increment(tileArrayGetStrategies, strategy);
                            AddSample(tileArrayGetStrategySamples, strategy, $"{method.DeclaringType.FullName}::{method.Name}@IL_{instruction.Offset:X4}");
                            var flow = new TileArrayGetFlowAudit
                            {
                                Location = $"{method.DeclaringType.FullName}::{method.Name}@IL_{instruction.Offset:X4}",
                                Consumer = consumer,
                                Strategy = strategy
                            };
                            tileArrayGetFlows.Add(flow);
                            if (!strategy.StartsWith("Addressable", StringComparison.Ordinal) && strategy != "DirectNullCheck" && strategy != "DiscardedTileValue")
                            {
                                tileArrayGetResidualFlows.Add(flow);
                            }
                            if (strategy == "StackFlowRequiresDataflow")
                                tileStackFlows.Add(TileStackFlowAnalyzer.Analyze(method, instruction, tileType.FullName, tileMutatingMethods));
                            if (strategy == "LocalAliasFlow" && TryGetStoredLocal(method, NextNonNop(instruction), out VariableDefinition? local) && local is not null)
                            {
                                TileLocalAliasFlowAudit localFlow = AuditTileLocalAliasFlow(method, instruction, local, tileType.FullName, tileMutatingMethods);
                                tileLocalAliasFlows.Add(localFlow);
                                foreach (string use in localFlow.UseKinds)
                                {
                                    Increment(tileLocalAliasUseKinds, use);
                                    AddSample(tileLocalAliasUseSamples, use, localFlow.Location);
                                }
                            }
                        }
                        string? runtimeTypeUse = ClassifyTileRuntimeTypeUse(method, instruction, tileType.FullName);
                        if (runtimeTypeUse is not null)
                            Increment(tileRuntimeTypeUses, runtimeTypeUse);
                    }
                }
                TileMethodAudit? audit = AuditMethod(method, mainTile, tileArrayType, tileType.FullName, unsupportedCalls, tileStores, tileNullChecks);
                if (audit is not null)
                    methods.Add(audit);
            }
        }

        methods.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Method, right.Method));
        tileArrayFields.Sort(StringComparer.Ordinal);
        tileReferenceFields.Sort(StringComparer.Ordinal);
        tileStores.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileNullChecks.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileLocalAliasFlows.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileArrayGetFlows.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileArrayGetResidualFlows.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileStackFlows.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileSignatureContracts.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Method, right.Method));
        tileSignatureCalls.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileFieldInstructions.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileMethodInstructions.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileConstructorInstructions.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        tileBoxInstructions.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Location, right.Location));
        TileStoreAudit[] unclassifiedStores = tileStores.Where(store => store.Kind == TileStoreKind.Unclassified).ToArray();
        return new TileStorageAuditReport
        {
            AssemblyVersion = module.Assembly.Name.Version?.ToString() ?? string.Empty,
            Sha256 = hash,
            MainTileFieldType = tileArrayType,
            MethodsTouchingMainTile = methods.Count(method => method.MainTileFieldReferences > 0),
            MethodsConstructingTile = methods.Count(method => method.TileConstructors > 0),
            TileArrayCalls = new TileArrayCallCounts
            {
                Get = methods.Sum(method => method.TileArrayGetCalls),
                Set = methods.Sum(method => method.TileArraySetCalls),
                Address = methods.Sum(method => method.TileArrayAddressCalls),
                Constructor = methods.Sum(method => method.TileArrayConstructorCalls)
            },
            TileArrayFields = tileArrayFields,
            TileReferenceFields = tileReferenceFields,
            TileReferenceSignatures = tileReferenceSignatures.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            TileTypedLocals = tileTypedLocals,
            TileFieldAccesses = tileFieldAccesses.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            TileMethodCalls = tileMethodCalls.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            TileFieldInstructions = tileFieldInstructions,
            TileMethodInstructions = tileMethodInstructions,
            TileConstructorInstructions = tileConstructorInstructions,
            TileBoxInstructions = tileBoxInstructions,
            TileArrayGetConsumers = tileArrayGetConsumers.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            TileArrayGetConsumerSamples = tileArrayGetConsumerSamples.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal),
            TileArrayGetStrategies = tileArrayGetStrategies.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            TileArrayGetStrategySamples = tileArrayGetStrategySamples.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal),
            TileArrayGetFlows = tileArrayGetFlows,
            TileArrayGetResidualFlows = tileArrayGetResidualFlows,
            TileStackFlows = tileStackFlows,
            TileRuntimeTypeUses = tileRuntimeTypeUses.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            TileSignatureContracts = tileSignatureContracts,
            TileSignatureCalls = tileSignatureCalls,
            TileLocalAliasFlows = tileLocalAliasFlows,
            TileLocalAliasUseKinds = tileLocalAliasUseKinds.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            TileLocalAliasUseSamples = tileLocalAliasUseSamples.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal),
            UnsupportedTileArrayCalls = unsupportedCalls.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            TileStores = tileStores,
            UnclassifiedTileStores = unclassifiedStores,
            TileNullChecks = tileNullChecks,
            Methods = methods
        };
    }

    private static string ValidateTargetExecutable(string executablePath)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Terraria.exe was not found.", executablePath);
        if (!string.Equals(Path.GetFileName(executablePath), "Terraria.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The input file must be named Terraria.exe.");

        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executablePath)));
        if (!string.Equals(hash, SupportedTerrariaHash, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported Terraria.exe hash. Expected {SupportedTerrariaHash}, got {hash}.");
        return hash;
    }

    private static void ValidateExpectedSurface(TileStorageAuditReport report)
    {
        if (report.MethodsTouchingMainTile != 1221 || report.MethodsConstructingTile != 135 ||
            report.TileArrayCalls.Get != 13460 || report.TileArrayCalls.Set != 379 ||
            report.TileArrayCalls.Address != 0 || report.TileArrayCalls.Constructor != 1)
        {
            throw new InvalidOperationException("Tile array surface does not match the verified Terraria 1.4.5.6 migration baseline.");
        }

        string[] expectedArrayFields =
        {
            "Terraria.GameContent.Drawing.WallDrawing::_tileArray",
            "Terraria.Main::tile"
        };
        if (!report.TileArrayFields.SequenceEqual(expectedArrayFields, StringComparer.Ordinal))
            throw new InvalidOperationException("Tile array aliases do not match the verified migration baseline.");

        if (report.TileReferenceFields.Count != 4 || report.TileReferenceSignatures.Count != 166 || report.TileTypedLocals != 1088)
            throw new InvalidOperationException("Tile reference surface does not match the verified migration baseline.");

        if (report.UnclassifiedTileStores.Count != 0 || report.TileNullChecks.Count != 944 || report.UnsupportedTileArrayCalls.Count != 0)
            throw new InvalidOperationException("Tile storage audit contains unresolved semantic patterns.");
    }

    private static void ValidateTransformReadiness(TileStorageAuditReport report)
    {
        int localAliases = GetCount(report.TileArrayGetStrategies, "LocalAliasFlow");
        int stackFlows = GetCount(report.TileArrayGetStrategies, "StackFlowRequiresDataflow");
        int calleeReviewFlows = GetCount(report.TileArrayGetStrategies, "TileParameterCallRequiresCalleeReview");
        if (localAliases != 0 || stackFlows != 0 || calleeReviewFlows != 0)
        {
            throw new InvalidOperationException(
                $"Tile migration is not transform-ready: {localAliases} local alias flows, {stackFlows} stack flows, and {calleeReviewFlows} tile-parameter calls still require method-level dataflow rules.");
        }

        string[] unsupportedRuntimeUses = report.TileRuntimeTypeUses.Keys
            .Where(kind => !string.Equals(kind, "SystemObjectCall:.ctor/0", StringComparison.Ordinal) &&
                           !string.Equals(kind, "SystemObjectCall:MemberwiseClone/0", StringComparison.Ordinal))
            .ToArray();
        if (unsupportedRuntimeUses.Length != 0)
            throw new InvalidOperationException("Tile migration has unsupported runtime type operations: " + string.Join(", ", unsupportedRuntimeUses) + ".");
    }

    private static int GetCount(IReadOnlyDictionary<string, int> values, string key)
    {
        return values.TryGetValue(key, out int count) ? count : 0;
    }

    private static HashSet<string> FindTileMutatingMethods(TypeDefinition tileType)
    {
        var mutating = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodDefinition method in tileType.Methods.Where(method => method.HasBody))
        {
            if (method.Body.Instructions.Any(instruction => instruction.OpCode.Code == Code.Stfld &&
                                                        instruction.Operand is FieldReference field &&
                                                        field.DeclaringType.FullName == tileType.FullName))
            {
                mutating.Add(GetMethodKey(method));
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (MethodDefinition method in tileType.Methods.Where(method => method.HasBody))
            {
                if (mutating.Contains(GetMethodKey(method)))
                    continue;
                if (method.Body.Instructions
                    .Select(instruction => instruction.Operand as MethodReference)
                    .Any(called => called is not null && called.DeclaringType.FullName == tileType.FullName && mutating.Contains(GetMethodKey(called))))
                {
                    changed = mutating.Add(GetMethodKey(method)) || changed;
                }
            }
        }
        while (changed);

        return mutating;
    }

    private static TileSignatureContractAudit AuditTileSignatureContract(MethodDefinition method, string tileType, ISet<string> tileMutatingMethods)
    {
        bool hasDirectTileFieldWrite = method.HasBody && method.Body.Instructions.Any(instruction =>
            instruction.OpCode.Code == Code.Stfld && instruction.Operand is FieldReference field && field.DeclaringType.FullName == tileType);
        bool callsTileMutator = method.HasBody && method.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called && called.DeclaringType.FullName == tileType && tileMutatingMethods.Contains(GetMethodKey(called)));
        return new TileSignatureContractAudit
        {
            Method = method.FullName,
            ReturnsTile = IsTileType(method.ReturnType, tileType),
            TileParameters = method.Parameters
                .Where(parameter => IsTileType(parameter.ParameterType, tileType))
                .Select(parameter => new TileParameterContractAudit
                {
                    Index = parameter.Index,
                    IsByReference = parameter.ParameterType is ByReferenceType,
                    IsOut = parameter.IsOut,
                    IsIn = parameter.IsIn
                })
                .ToArray(),
            HasDirectTileFieldWrite = hasDirectTileFieldWrite,
            CallsTileMutator = callsTileMutator,
            HasNullLiteral = method.HasBody && method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldnull)
        };
    }

    private static TileMemberInstructionAudit CreateTileMemberInstructionAudit(MethodDefinition method, Instruction instruction, string member)
    {
        return new TileMemberInstructionAudit
        {
            Location = $"{method.DeclaringType.FullName}::{method.Name}@IL_{instruction.Offset:X4}",
            OpCode = instruction.OpCode.Code.ToString(),
            Member = member
        };
    }

    private static TileLocalAliasFlowAudit AuditTileLocalAliasFlow(
        MethodDefinition method,
        Instruction getCall,
        VariableDefinition local,
        string tileType,
        ISet<string> tileMutatingMethods)
    {
        var useKinds = new HashSet<string>(StringComparer.Ordinal);
        var uses = new List<TileLocalAliasUseAudit>();
        for (Instruction? cursor = NextNonNop(NextNonNop(getCall)!); cursor is not null; cursor = NextNonNop(cursor))
        {
            if (IsStoreToLocal(method, cursor, local))
                break;
            if (!IsLoadFromLocal(method, cursor, local))
                continue;

            string kind = ClassifyTileLocalUse(method, cursor, tileType, tileMutatingMethods);
            TileStackFlowAudit? stackFlow = kind == "UnknownUse"
                ? TileStackFlowAnalyzer.AnalyzeFromLoad(method, cursor, tileType, tileMutatingMethods)
                : null;
            useKinds.Add(kind);
            uses.Add(new TileLocalAliasUseAudit
            {
                Location = $"IL_{cursor.Offset:X4}",
                Kind = kind,
                StackOutcomes = stackFlow?.Outcomes ?? Array.Empty<string>()
            });
        }

        if (useKinds.Count == 0)
        {
            useKinds.Add("NoObservedUse");
            uses.Add(new TileLocalAliasUseAudit { Location = "<none>", Kind = "NoObservedUse" });
        }
        return new TileLocalAliasFlowAudit
        {
            Location = $"{method.DeclaringType.FullName}::{method.Name}@IL_{getCall.Offset:X4}",
            Local = local.Index,
            UseKinds = useKinds.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Uses = uses
        };
    }

    private static string ClassifyTileLocalUse(MethodDefinition method, Instruction load, string tileType, ISet<string> tileMutatingMethods)
    {
        Instruction? next = NextNonNop(load);
        if (next?.Operand is FieldReference field && field.DeclaringType.FullName == tileType && next.OpCode.Code == Code.Ldfld)
            return "FieldRead";
        if (next?.Operand is MethodReference tileMethod && tileMethod.DeclaringType.FullName == tileType)
            return tileMutatingMethods.Contains(GetMethodKey(tileMethod)) ? "TileMethodMutation" : "TileMethodRead";
        if (TryGetBranch(next, out _) || TryGetExplicitNullComparison(next, out _) || TryGetNullComparisonValue(next))
            return "NullCheck";
        if (next?.OpCode.Code == Code.Ret)
            return "ReturnEscape";
        if (TryGetStoredLocal(method, next, out VariableDefinition? destination) && destination is not null && destination.VariableType.FullName == tileType)
            return "LocalAliasCopy";
        if (next?.Operand is FieldReference destinationField && destinationField.FieldType.FullName == tileType && next.OpCode.Code == Code.Stfld)
            return "FieldEscape";
        if (TryFindDeferredTileFieldWrite(next, tileType))
            return "FieldWrite";
        if (TryFindDeferredTileMethod(next, tileType, out MethodReference? deferredTileMethod) && deferredTileMethod is not null)
            return tileMutatingMethods.Contains(GetMethodKey(deferredTileMethod)) ? "TileMethodMutation" : "TileMethodRead";
        if (TryFindDeferredTileParameterCall(next, tileType, out _))
            return "TileParameterEscape";
        return "UnknownUse";
    }

    private static bool TryFindDeferredTileMethod(Instruction? first, string tileType, out MethodReference? method)
    {
        method = null;
        if (first is null)
            return false;

        for (Instruction? cursor = first; cursor is not null && cursor.Offset - first.Offset <= 48; cursor = NextNonNop(cursor))
        {
            if (cursor.Operand is MethodReference called && called.DeclaringType.FullName == tileType)
            {
                method = called;
                return true;
            }
            if (!IsSimpleStackPush(cursor))
                return false;
        }

        return false;
    }

    private static bool TryFindDeferredTileParameterCall(Instruction? first, string tileType, out MethodReference? method)
    {
        method = null;
        if (first is null)
            return false;

        for (Instruction? cursor = first; cursor is not null && cursor.Offset - first.Offset <= 48; cursor = NextNonNop(cursor))
        {
            if (cursor.Operand is MethodReference called && called.Parameters.Any(parameter => IsTileType(parameter.ParameterType, tileType)))
            {
                method = called;
                return true;
            }
            if (!IsSimpleStackPush(cursor))
                return false;
        }

        return false;
    }

    private static bool TryFindDeferredTileFieldWrite(Instruction? first, string tileType)
    {
        if (first is null)
            return false;

        for (Instruction? cursor = first; cursor is not null && cursor.Offset - first.Offset <= 48; cursor = NextNonNop(cursor))
        {
            if (cursor.Operand is FieldReference field && field.DeclaringType.FullName == tileType && cursor.OpCode.Code == Code.Stfld)
                return true;
            if (!IsSimpleStackPush(cursor))
                return false;
        }

        return false;
    }

    private static string GetMethodKey(MethodReference method)
    {
        return $"{method.Name}/{method.Parameters.Count}";
    }

    // Locks the compact-data helpers to the exact Tile methods verified in this executable.
    private static void ValidateTileSemanticMethods(TypeDefinition tileType)
    {
        RequireStoredFields(tileType, "ClearEverything", new[]
        {
            "type", "wall", "liquid", "sTileHeader", "bTileHeader", "bTileHeader2", "bTileHeader3", "frameX", "frameY"
        });
        RequireCalledMethods(tileType, "ClearTile", new[] { "ClearSlope/0", "active/1", "inActive/1" });
        RequireCalledMethods(tileType, "ClearSlope", new[] { "slope/1", "halfBrick/1" });
        RequireCalledMethods(tileType, "ClearTileAndPaint", new[] { "ClearTile/0", "ClearBlockPaintAndCoating/0" });
        RequireStoredFields(tileType, "CopyFrom", new[]
        {
            "type", "wall", "liquid", "sTileHeader", "bTileHeader", "bTileHeader2", "bTileHeader3", "frameX", "frameY"
        });
        RequireCalledMethods(tileType, "Clear", new[]
        {
            "active/1", "wallFrameX/1", "wallFrameY/1", "ClearBlockPaintAndCoating/0", "ClearWallPaintAndCoating/0",
            "liquidType/1", "checkingLiquid/1", "slope/1", "halfBrick/1", "wire/1", "wire2/1", "wire3/1", "wire4/1", "actuator/1", "inActive/1"
        });
        RequireCalledMethods(tileType, "CopyPaintAndCoating", new[] { "color/0", "color/1", "invisibleBlock/0", "invisibleBlock/1", "fullbrightBlock/0", "fullbrightBlock/1" });
        RequireCalledMethods(tileType, "ClearBlockPaintAndCoating", new[] { "color/1", "fullbrightBlock/1", "invisibleBlock/1" });
        RequireCalledMethods(tileType, "ClearWallPaintAndCoating", new[] { "wallColor/1", "fullbrightWall/1", "invisibleWall/1" });
    }

    private static void ValidateTileCriticalBoundaries(ModuleDefinition module)
    {
        RequireMethod(module, "Terraria.Main", ".cctor", 8701);
        RequireMethod(module, "Terraria.IO.WorldFile", "SaveWorldTiles", 1067, "System.IO.BinaryWriter");
        RequireMethod(module, "Terraria.IO.WorldFile", "LoadWorldTiles", 1012, "System.IO.BinaryReader", "System.Boolean[]");
        RequireMethod(module, "Terraria.IO.WorldFile", "LoadWorld_Version1_Old_BeforeRelease88", 3652, "System.IO.BinaryReader");
        RequireMethod(module, "Terraria.NetMessage", "CompressTileBlock_Inner", 2190, "System.IO.BinaryWriter", "System.Int32", "System.Int32", "System.Int32", "System.Int32");
        RequireMethod(module, "Terraria.NetMessage", "DecompressTileBlock_Inner", 1119, "System.IO.BinaryReader", "System.Int32", "System.Int32", "System.Int32", "System.Int32");
        RequireMethod(module, "Terraria.NetMessage", "SendTileSquare", 23, "System.Int32", "System.Int32", "System.Int32", "System.Int32", "System.Int32", "Terraria.ID.TileChangeType");
    }

    private static void RequireMethod(ModuleDefinition module, string typeName, string methodName, int expectedCodeSize, params string[] parameterTypes)
    {
        TypeDefinition type = RequireType(module, typeName);
        MethodDefinition method = type.Methods.SingleOrDefault(candidate =>
            candidate.Name == methodName &&
            candidate.HasBody &&
            candidate.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameterTypes, StringComparer.Ordinal))
            ?? throw new InvalidOperationException($"{typeName}.{methodName} does not have the expected parameter signature.");
        if (method.Body.CodeSize != expectedCodeSize)
            throw new InvalidOperationException($"{method.FullName} does not have the verified 1.4.5.6 IL shape.");
    }

    private static void RequireStoredFields(TypeDefinition tileType, string methodName, IReadOnlyList<string> expectedFields)
    {
        MethodDefinition method = RequireTileMethod(tileType, methodName);
        string[] actualFields = method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code == Code.Stfld)
            .Select(instruction => ((FieldReference)instruction.Operand).Name)
            .ToArray();
        if (!actualFields.SequenceEqual(expectedFields, StringComparer.Ordinal))
            throw new InvalidOperationException($"Terraria.Tile.{methodName} no longer has the verified field behavior.");
    }

    private static void RequireCalledMethods(TypeDefinition tileType, string methodName, IReadOnlyList<string> expectedCalls)
    {
        MethodDefinition method = RequireTileMethod(tileType, methodName);
        string[] actualCalls = method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt)
            .Select(instruction => (MethodReference)instruction.Operand)
            .Where(called => called.DeclaringType.FullName == tileType.FullName)
            .Select(called => $"{called.Name}/{called.Parameters.Count}")
            .ToArray();
        if (!actualCalls.SequenceEqual(expectedCalls, StringComparer.Ordinal))
            throw new InvalidOperationException($"Terraria.Tile.{methodName} no longer has the verified method behavior.");
    }

    private static MethodDefinition RequireTileMethod(TypeDefinition tileType, string methodName)
    {
        return tileType.Methods.SingleOrDefault(method => method.Name == methodName && method.HasBody)
            ?? throw new InvalidOperationException($"Terraria.Tile.{methodName} was not found.");
    }

    private static TileMethodAudit? AuditMethod(
        MethodDefinition method,
        FieldDefinition mainTile,
        string tileArrayType,
        string tileType,
        ISet<string> unsupportedCalls,
        ICollection<TileStoreAudit> tileStores,
        ICollection<TileNullCheckAudit> tileNullChecks)
    {
        int mainTileReferences = 0;
        int getCalls = 0;
        int setCalls = 0;
        int addressCalls = 0;
        int constructorCalls = 0;
        int tileConstructors = 0;
        int tileArrayLocals = method.HasBody ? method.Body.Variables.Count(variable => variable.VariableType.FullName == tileArrayType) : 0;
        int nullBranches = 0;

        if (method.HasBody)
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is FieldReference field && IsMainTile(field, mainTile))
                    mainTileReferences++;

                if (instruction.Operand is MethodReference called)
                {
                    if (called.DeclaringType.FullName == tileArrayType)
                    {
                        switch (called.Name)
                        {
                            case "Get": getCalls++; break;
                            case "Set":
                                setCalls++;
                                tileStores.Add(ClassifyTileStore(method, instruction, tileArrayType, tileType));
                                break;
                            case "Address": addressCalls++; break;
                            case ".ctor": constructorCalls++; break;
                            default: unsupportedCalls.Add(called.FullName); break;
                        }
                    }

                    if (called.DeclaringType.FullName == tileType && called.Name == ".ctor")
                        tileConstructors++;
                }

                if (TryClassifyTileNullCheck(method, instruction, tileArrayType, out TileNullCheckAudit? nullCheck) && nullCheck is not null)
                {
                    nullBranches++;
                    tileNullChecks.Add(nullCheck);
                }
            }
        }

        if (mainTileReferences == 0 && getCalls == 0 && setCalls == 0 && addressCalls == 0 && constructorCalls == 0 && tileConstructors == 0 && tileArrayLocals == 0)
            return null;

        return new TileMethodAudit
        {
            Method = $"{method.DeclaringType.FullName}::{method.Name}",
            MainTileFieldReferences = mainTileReferences,
            TileArrayGetCalls = getCalls,
            TileArraySetCalls = setCalls,
            TileArrayAddressCalls = addressCalls,
            TileArrayConstructorCalls = constructorCalls,
            TileConstructors = tileConstructors,
            TileArrayLocals = tileArrayLocals,
            CandidateNullBranches = nullBranches
        };
    }

    private static bool TryClassifyTileNullCheck(MethodDefinition method, Instruction instruction, string tileArrayType, out TileNullCheckAudit? nullCheck)
    {
        nullCheck = null;
        if (instruction.Operand is not MethodReference called || called.DeclaringType.FullName != tileArrayType || called.Name != "Get")
            return false;

        Instruction? next = NextNonNop(instruction);
        if (TryGetBranch(next, out TileNullCheckKind directKind))
        {
            nullCheck = CreateNullCheck(method, instruction, directKind, next!);
            return true;
        }

        if (TryGetExplicitNullComparison(next, out Instruction? directBranch))
        {
            nullCheck = CreateNullCheck(method, instruction, TileNullCheckKind.DirectNullComparison, directBranch!);
            return true;
        }

        if (!TryGetStoredLocal(method, next, out VariableDefinition? local) || local is null)
            return false;

        for (Instruction? cursor = NextNonNop(next!); cursor is not null && cursor.Offset - instruction.Offset <= 80; cursor = NextNonNop(cursor))
        {
            if (IsStoreToLocal(method, cursor, local))
                break;
            if (!IsLoadFromLocal(method, cursor, local))
                continue;

            Instruction? localUse = NextNonNop(cursor);
            if (TryGetBranch(localUse, out TileNullCheckKind localKind))
            {
                nullCheck = CreateNullCheck(method, instruction, localKind == TileNullCheckKind.DirectBranchTrue ? TileNullCheckKind.LocalBranchTrue : TileNullCheckKind.LocalBranchFalse, localUse!);
                return true;
            }
            if (TryGetExplicitNullComparison(localUse, out Instruction? localBranch))
            {
                nullCheck = CreateNullCheck(method, instruction, TileNullCheckKind.LocalNullComparison, localBranch!);
                return true;
            }
            break;
        }

        return false;
    }

    private static TileNullCheckAudit CreateNullCheck(MethodDefinition method, Instruction getCall, TileNullCheckKind kind, Instruction branch)
    {
        return new TileNullCheckAudit
        {
            Location = $"{method.DeclaringType.FullName}::{method.Name}@IL_{getCall.Offset:X4}",
            Kind = kind,
            BranchOffset = $"IL_{branch.Offset:X4}"
        };
    }

    private static bool TryGetBranch(Instruction? instruction, out TileNullCheckKind kind)
    {
        kind = TileNullCheckKind.DirectBranchFalse;
        if (instruction is null)
            return false;
        if (instruction.OpCode == OpCodes.Brtrue || instruction.OpCode == OpCodes.Brtrue_S)
        {
            kind = TileNullCheckKind.DirectBranchTrue;
            return true;
        }
        if (instruction.OpCode == OpCodes.Brfalse || instruction.OpCode == OpCodes.Brfalse_S)
        {
            kind = TileNullCheckKind.DirectBranchFalse;
            return true;
        }
        return false;
    }

    private static bool TryGetExplicitNullComparison(Instruction? instruction, out Instruction? branch)
    {
        branch = null;
        if (instruction?.OpCode != OpCodes.Ldnull)
            return false;
        Instruction? comparison = NextNonNop(instruction);
        if (comparison?.OpCode != OpCodes.Ceq)
            return false;
        Instruction? candidate = NextNonNop(comparison);
        if (candidate?.OpCode != OpCodes.Brtrue && candidate?.OpCode != OpCodes.Brtrue_S && candidate?.OpCode != OpCodes.Brfalse && candidate?.OpCode != OpCodes.Brfalse_S)
            return false;
        branch = candidate;
        return true;
    }

    private static bool TryGetNullComparisonValue(Instruction? instruction)
    {
        if (instruction?.OpCode != OpCodes.Ldnull)
            return false;
        Instruction? comparison = NextNonNop(instruction);
        return comparison?.OpCode == OpCodes.Ceq || comparison?.OpCode == OpCodes.Cgt_Un;
    }

    private static TileStoreAudit ClassifyTileStore(MethodDefinition method, Instruction setCall, string tileArrayType, string tileType)
    {
        Instruction? producer = PreviousNonNop(setCall);
        TileStoreKind kind = TileStoreKind.Unclassified;
        string producerDescription = producer is null ? "<missing>" : DescribeInstruction(producer);

        if (producer is not null)
        {
            if (producer.OpCode == OpCodes.Ldnull)
            {
                kind = TileStoreKind.Null;
            }
            else if (producer.Operand is MethodReference called && called.DeclaringType.FullName == tileArrayType && called.Name == "Get")
            {
                kind = TileStoreKind.DirectTileRead;
            }
            else if (producer.Operand is MethodReference constructor && constructor.DeclaringType.FullName == tileType && constructor.Name == ".ctor")
            {
                kind = constructor.Parameters.Count == 0 ? TileStoreKind.NewDefaultTile : TileStoreKind.NewCopiedTile;
            }
            else if (TryClassifyDuplicatedTileConstructor(method, producer, tileType, out TileStoreKind duplicatedConstructorKind))
            {
                kind = duplicatedConstructorKind;
            }
            else if (TryGetLoadedLocal(method, producer, out VariableDefinition? local) && local is not null && local.VariableType.FullName == tileType)
            {
                kind = TileStoreKind.TileLocal;
            }
            else if (TryGetLoadedParameter(method, producer, out ParameterDefinition? parameter) && parameter is not null && parameter.ParameterType.FullName == tileType)
            {
                kind = TileStoreKind.TileParameter;
            }
            else if (producer.Operand is FieldReference field && field.FieldType.FullName == tileType)
            {
                kind = TileStoreKind.TileField;
            }
        }

        return new TileStoreAudit
        {
            Location = $"{method.DeclaringType.FullName}::{method.Name}@IL_{setCall.Offset:X4}",
            Kind = kind,
            Producer = producerDescription
        };
    }

    private static Instruction? PreviousNonNop(Instruction instruction)
    {
        for (Instruction? cursor = instruction.Previous; cursor is not null; cursor = cursor.Previous)
        {
            if (cursor.OpCode != OpCodes.Nop)
                return cursor;
        }

        return null;
    }

    private static Instruction? NextNonNop(Instruction instruction)
    {
        for (Instruction? cursor = instruction.Next; cursor is not null; cursor = cursor.Next)
        {
            if (cursor.OpCode != OpCodes.Nop)
                return cursor;
        }

        return null;
    }

    private static bool TryClassifyDuplicatedTileConstructor(MethodDefinition method, Instruction producer, string tileType, out TileStoreKind kind)
    {
        kind = TileStoreKind.Unclassified;
        if (!IsStoreLocal(method, producer))
            return false;

        Instruction? duplicate = PreviousNonNop(producer);
        Instruction? constructor = duplicate is not null ? PreviousNonNop(duplicate) : null;
        if (duplicate?.OpCode != OpCodes.Dup || constructor?.Operand is not MethodReference called || called.DeclaringType.FullName != tileType || called.Name != ".ctor")
            return false;

        kind = called.Parameters.Count == 0 ? TileStoreKind.DuplicatedNewDefaultTile : TileStoreKind.DuplicatedNewCopiedTile;
        return true;
    }

    private static bool IsStoreLocal(MethodDefinition method, Instruction instruction)
    {
        return instruction.OpCode.Code switch
        {
            Code.Stloc_0 => method.Body.Variables.Count > 0,
            Code.Stloc_1 => method.Body.Variables.Count > 1,
            Code.Stloc_2 => method.Body.Variables.Count > 2,
            Code.Stloc_3 => method.Body.Variables.Count > 3,
            Code.Stloc or Code.Stloc_S => instruction.Operand is VariableDefinition,
            _ => false
        };
    }

    private static bool TryGetStoredLocal(MethodDefinition method, Instruction? instruction, out VariableDefinition? local)
    {
        local = null;
        if (instruction is null || !IsStoreLocal(method, instruction))
            return false;

        local = instruction.OpCode.Code switch
        {
            Code.Stloc_0 => method.Body.Variables[0],
            Code.Stloc_1 => method.Body.Variables[1],
            Code.Stloc_2 => method.Body.Variables[2],
            Code.Stloc_3 => method.Body.Variables[3],
            Code.Stloc or Code.Stloc_S when instruction.Operand is VariableDefinition variable => variable,
            _ => null
        };
        return local is not null;
    }

    private static bool IsStoreToLocal(MethodDefinition method, Instruction instruction, VariableDefinition local)
    {
        return TryGetStoredLocal(method, instruction, out VariableDefinition? stored) && stored == local;
    }

    private static bool IsLoadFromLocal(MethodDefinition method, Instruction instruction, VariableDefinition local)
    {
        return TryGetLoadedLocal(method, instruction, out VariableDefinition? loaded) && loaded == local;
    }

    private static bool TryGetLoadedLocal(MethodDefinition method, Instruction instruction, out VariableDefinition? local)
    {
        local = instruction.OpCode.Code switch
        {
            Code.Ldloc_0 when method.Body.Variables.Count > 0 => method.Body.Variables[0],
            Code.Ldloc_1 when method.Body.Variables.Count > 1 => method.Body.Variables[1],
            Code.Ldloc_2 when method.Body.Variables.Count > 2 => method.Body.Variables[2],
            Code.Ldloc_3 when method.Body.Variables.Count > 3 => method.Body.Variables[3],
            Code.Ldloc or Code.Ldloc_S when instruction.Operand is VariableDefinition variable => variable,
            _ => null
        };
        return local is not null;
    }

    private static bool TryGetLoadedParameter(MethodDefinition method, Instruction instruction, out ParameterDefinition? parameter)
    {
        int index = instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => 0,
            Code.Ldarg_1 => 1,
            Code.Ldarg_2 => 2,
            Code.Ldarg_3 => 3,
            Code.Ldarg or Code.Ldarg_S when instruction.Operand is ParameterDefinition value => value.Index,
            _ => -1
        };
        if (!method.IsStatic)
            index--;
        parameter = index >= 0 && index < method.Parameters.Count ? method.Parameters[index] : null;
        return parameter is not null;
    }

    private static string DescribeInstruction(Instruction instruction)
    {
        return instruction.Operand is null ? instruction.OpCode.Code.ToString() : $"{instruction.OpCode.Code}: {instruction.Operand}";
    }

    private static bool IsMainTile(FieldReference field, FieldDefinition mainTile)
    {
        return field.Name == mainTile.Name && field.DeclaringType.FullName == mainTile.DeclaringType.FullName && field.FieldType.FullName == mainTile.FieldType.FullName;
    }

    private static bool IsTileType(TypeReference type, string tileType)
    {
        return type.FullName == tileType || type is ByReferenceType byReference && byReference.ElementType.FullName == tileType;
    }

    private static void Increment(IDictionary<string, int> values, string key)
    {
        values.TryGetValue(key, out int count);
        values[key] = count + 1;
    }

    private static void AddSample(IDictionary<string, List<string>> samples, string key, string location)
    {
        if (!samples.TryGetValue(key, out List<string>? values))
        {
            values = new List<string>();
            samples.Add(key, values);
        }

        if (values.Count < 8)
            values.Add(location);
    }

    // A value-backed tile can keep direct field/method access addressable, but an
    // array-loaded reference that escapes into a local needs alias-preserving IL.
    private static string ClassifyTileArrayGetStrategy(MethodDefinition method, Instruction getCall, string tileType)
    {
        Instruction? next = NextNonNop(getCall);
        if (next?.Operand is FieldReference field && field.DeclaringType.FullName == tileType)
            return "AddressableFieldAccess";
        if (next?.Operand is MethodReference called && called.DeclaringType.FullName == tileType)
            return "AddressableTileMethod";
        if (TryGetBranch(next, out _) || TryGetExplicitNullComparison(next, out _) || TryGetNullComparisonValue(next))
            return "DirectNullCheck";
        if (next?.OpCode.Code == Code.Pop)
            return "DiscardedTileValue";
        if (TryGetStoredLocal(method, next, out VariableDefinition? local) && local is not null && local.VariableType.FullName == tileType)
            return "LocalAliasFlow";
        if (TryClassifyDeferredTileReceiver(next, tileType, out string? deferredStrategy))
            return deferredStrategy!;
        return "StackFlowRequiresDataflow";
    }

    private static bool TryClassifyDeferredTileReceiver(Instruction? first, string tileType, out string? strategy)
    {
        strategy = null;
        if (first is null)
            return false;

        for (Instruction? cursor = first; cursor is not null && cursor.Offset - first.Offset <= 48; cursor = NextNonNop(cursor))
        {
            if (cursor.Operand is MethodReference method && method.DeclaringType.FullName == tileType)
            {
                strategy = "AddressableTileMethodWithArguments";
                return true;
            }

            if (cursor.Operand is MethodReference methodWithTileParameter &&
                methodWithTileParameter.Parameters.Any(parameter => IsTileType(parameter.ParameterType, tileType)))
            {
                strategy = "TileParameterCallRequiresCalleeReview";
                return true;
            }

            if (cursor.Operand is FieldReference field && field.DeclaringType.FullName == tileType && cursor.OpCode.Code == Code.Stfld)
            {
                strategy = "AddressableFieldWrite";
                return true;
            }

            if (!IsSimpleStackPush(cursor))
                return false;
        }

        return false;
    }

    private static bool IsSimpleStackPush(Instruction instruction)
    {
        return instruction.OpCode.Code switch
        {
            Code.Ldc_I4_M1 or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5 or
            Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8 or
            Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg or Code.Ldarg_S or
            Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc or Code.Ldloc_S or
            Code.Ldnull or Code.Ldstr or Code.Ldsfld or Code.Ldftn => true,
            _ => false
        };
    }

    private static string? ClassifyTileRuntimeTypeUse(MethodDefinition containingMethod, Instruction instruction, string tileType)
    {
        if (instruction.Operand is TypeReference type && type.FullName == tileType)
        {
            return instruction.OpCode.Code switch
            {
                Code.Box => "Box",
                Code.Castclass => "Castclass",
                Code.Isinst => "Isinst",
                Code.Ldtoken => "Ldtoken",
                Code.Newarr => "Newarr",
                Code.Unbox => "Unbox",
                Code.Unbox_Any => "UnboxAny",
                _ => $"TypeOperand:{instruction.OpCode.Code}"
            };
        }

        if (containingMethod.DeclaringType.FullName == tileType &&
            instruction.Operand is MethodReference called && called.DeclaringType.FullName == "System.Object" &&
            instruction.OpCode.Code is Code.Call or Code.Callvirt)
            return $"SystemObjectCall:{called.Name}/{called.Parameters.Count}";

        return null;
    }

    private static string DescribeTileArrayGetConsumer(Instruction? instruction)
    {
        if (instruction is null)
            return "EndOfMethod";
        if (instruction.Operand is MethodReference method)
            return $"{instruction.OpCode.Code}:{method.DeclaringType.FullName}::{method.Name}/{method.Parameters.Count}";
        if (instruction.Operand is FieldReference field)
            return $"{instruction.OpCode.Code}:{field.DeclaringType.FullName}::{field.Name}";
        if (instruction.Operand is VariableDefinition variable)
            return $"{instruction.OpCode.Code}:local:{variable.VariableType.FullName}";
        return instruction.OpCode.Code.ToString();
    }

    private static TypeDefinition RequireType(ModuleDefinition module, string fullName)
    {
        return Flatten(module.Types).SingleOrDefault(type => type.FullName == fullName)
            ?? throw new InvalidOperationException($"Required Terraria type was not found: {fullName}.");
    }

    private static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in Flatten(type.NestedTypes))
                yield return nested;
        }
    }
}

internal sealed class TileStorageAuditReport
{
    public string AssemblyVersion { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string MainTileFieldType { get; init; } = string.Empty;
    public int MethodsTouchingMainTile { get; init; }
    public int MethodsConstructingTile { get; init; }
    public TileArrayCallCounts TileArrayCalls { get; init; } = new();
    public IReadOnlyList<string> TileArrayFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TileReferenceFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TileReferenceSignatures { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TileSignatureContractAudit> TileSignatureContracts { get; init; } = Array.Empty<TileSignatureContractAudit>();
    public int TileTypedLocals { get; init; }
    public IReadOnlyDictionary<string, int> TileFieldAccesses { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> TileMethodCalls { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<TileMemberInstructionAudit> TileFieldInstructions { get; init; } = Array.Empty<TileMemberInstructionAudit>();
    public IReadOnlyList<TileMemberInstructionAudit> TileMethodInstructions { get; init; } = Array.Empty<TileMemberInstructionAudit>();
    public IReadOnlyList<TileMemberInstructionAudit> TileConstructorInstructions { get; init; } = Array.Empty<TileMemberInstructionAudit>();
    public IReadOnlyList<TileMemberInstructionAudit> TileBoxInstructions { get; init; } = Array.Empty<TileMemberInstructionAudit>();
    public IReadOnlyDictionary<string, int> TileArrayGetConsumers { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TileArrayGetConsumerSamples { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, int> TileArrayGetStrategies { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TileArrayGetStrategySamples { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyList<TileArrayGetFlowAudit> TileArrayGetFlows { get; init; } = Array.Empty<TileArrayGetFlowAudit>();
    public IReadOnlyList<TileArrayGetFlowAudit> TileArrayGetResidualFlows { get; init; } = Array.Empty<TileArrayGetFlowAudit>();
    public IReadOnlyList<TileStackFlowAudit> TileStackFlows { get; init; } = Array.Empty<TileStackFlowAudit>();
    public IReadOnlyDictionary<string, int> TileRuntimeTypeUses { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<TileSignatureCallAudit> TileSignatureCalls { get; init; } = Array.Empty<TileSignatureCallAudit>();
    public IReadOnlyList<TileLocalAliasFlowAudit> TileLocalAliasFlows { get; init; } = Array.Empty<TileLocalAliasFlowAudit>();
    public IReadOnlyDictionary<string, int> TileLocalAliasUseKinds { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TileLocalAliasUseSamples { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyList<string> UnsupportedTileArrayCalls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TileStoreAudit> TileStores { get; init; } = Array.Empty<TileStoreAudit>();
    public IReadOnlyList<TileStoreAudit> UnclassifiedTileStores { get; init; } = Array.Empty<TileStoreAudit>();
    public IReadOnlyList<TileNullCheckAudit> TileNullChecks { get; init; } = Array.Empty<TileNullCheckAudit>();
    public IReadOnlyList<TileMethodAudit> Methods { get; init; } = Array.Empty<TileMethodAudit>();
}

internal sealed class TileArrayCallCounts
{
    public int Get { get; init; }
    public int Set { get; init; }
    public int Address { get; init; }
    public int Constructor { get; init; }
}

internal sealed class TileMethodAudit
{
    public string Method { get; init; } = string.Empty;
    public int MainTileFieldReferences { get; init; }
    public int TileArrayGetCalls { get; init; }
    public int TileArraySetCalls { get; init; }
    public int TileArrayAddressCalls { get; init; }
    public int TileArrayConstructorCalls { get; init; }
    public int TileConstructors { get; init; }
    public int TileArrayLocals { get; init; }
    public int CandidateNullBranches { get; init; }
}

internal sealed class TileArrayGetFlowAudit
{
    public string Location { get; init; } = string.Empty;
    public string Consumer { get; init; } = string.Empty;
    public string Strategy { get; init; } = string.Empty;
}

internal sealed class TileLocalAliasFlowAudit
{
    public string Location { get; init; } = string.Empty;
    public int Local { get; init; }
    public IReadOnlyList<string> UseKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TileLocalAliasUseAudit> Uses { get; init; } = Array.Empty<TileLocalAliasUseAudit>();
}

internal sealed class TileLocalAliasUseAudit
{
    public string Location { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public IReadOnlyList<string> StackOutcomes { get; init; } = Array.Empty<string>();
}

internal sealed class TileSignatureContractAudit
{
    public string Method { get; init; } = string.Empty;
    public bool ReturnsTile { get; init; }
    public IReadOnlyList<TileParameterContractAudit> TileParameters { get; init; } = Array.Empty<TileParameterContractAudit>();
    public bool HasDirectTileFieldWrite { get; init; }
    public bool CallsTileMutator { get; init; }
    public bool HasNullLiteral { get; init; }
}

internal sealed class TileSignatureCallAudit
{
    public string Location { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool CarriesTileReturn { get; init; }
    public int TileParameterCount { get; init; }
}

internal sealed class TileMemberInstructionAudit
{
    public string Location { get; init; } = string.Empty;
    public string OpCode { get; init; } = string.Empty;
    public string Member { get; init; } = string.Empty;
}

internal sealed class TileParameterContractAudit
{
    public int Index { get; init; }
    public bool IsByReference { get; init; }
    public bool IsOut { get; init; }
    public bool IsIn { get; init; }
}

internal enum TileStoreKind
{
    NewDefaultTile,
    NewCopiedTile,
    DuplicatedNewDefaultTile,
    DuplicatedNewCopiedTile,
    Null,
    DirectTileRead,
    TileLocal,
    TileParameter,
    TileField,
    Unclassified
}

internal sealed class TileStoreAudit
{
    public string Location { get; init; } = string.Empty;
    public TileStoreKind Kind { get; init; }
    public string Producer { get; init; } = string.Empty;
}

internal enum TileNullCheckKind
{
    DirectBranchTrue,
    DirectBranchFalse,
    DirectNullComparison,
    LocalBranchTrue,
    LocalBranchFalse,
    LocalNullComparison
}

internal sealed class TileNullCheckAudit
{
    public string Location { get; init; } = string.Empty;
    public TileNullCheckKind Kind { get; init; }
    public string BranchOffset { get; init; } = string.Empty;
}
