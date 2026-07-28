using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyEveryAuditedBoundaryProducesAnOperation();
            VerifyPreflightRejectsUnknownLoweringShape();
            VerifyFieldLoweringCatalogRejectsUnknownTileState();
            VerifyMethodLoweringCatalogRejectsUnknownTileBehavior();
            VerifyEmptyAuditFailsClosed();
            VerifyCopyOnlyTransactionPreservesInputAndRollsBackOutput();
            VerifyCopyOnlyTransactionCleansUpAfterFailure();
            VerifyFixtureFieldLoweringUsesCompactValueStorage();
            VerifyReusableFieldInstructionLowering();
            VerifyReusableTileMethodInstructionLowering();
            Console.WriteLine("Tile storage transformation planner tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Tile storage transformation planner test failed: " + exception.Message);
            return 1;
        }
    }

    private static void VerifyEveryAuditedBoundaryProducesAnOperation()
    {
        var audit = new TileStorageAuditSnapshot
        {
            Sha256 = "test",
            TileArrayGetFlows = new[]
            {
                new TileArrayGetFlowSnapshot { Location = "T::A@IL_0001", Strategy = "AddressableFieldRead", Consumer = "Ldfld" }
            },
            TileFieldInstructions = new[]
            {
                new TileMemberInstructionSnapshot { Location = "T::A@IL_0001", OpCode = "Ldfld", Member = "T.Tile::type" }
            },
            TileMethodInstructions = new[]
            {
                new TileMemberInstructionSnapshot { Location = "T::A@IL_0001", OpCode = "Callvirt", Member = "T.Tile::active" }
            },
            TileConstructorInstructions = new[]
            {
                new TileMemberInstructionSnapshot { Location = "T::A@IL_0001", OpCode = "Newobj", Member = "System.Void Terraria.Tile::.ctor()" }
            },
            TileStores = new[]
            {
                new TileStoreSnapshot { Location = "T::A@IL_0002", Kind = "NewDefaultTile", Producer = "Newobj" }
            },
            TileNullChecks = new[]
            {
                new TileNullCheckSnapshot { Location = "T::A@IL_0003", Kind = "DirectBranchFalse", BranchOffset = "IL_0004" }
            },
            TileLocalAliasFlows = new[]
            {
                new TileLocalAliasFlowSnapshot { Location = "T::A@IL_0005", Local = 0, UseKinds = new[] { "FieldRead" } }
            },
            TileStackFlows = new[]
            {
                new TileStackFlowSnapshot { Location = "T::A@IL_0006", Outcomes = new[] { "FieldWrite" } }
            },
            TileSignatureContracts = new[]
            {
                new TileSignatureContractSnapshot
                {
                    Method = "T::A(Terraria.Tile)",
                    TileParameters = new[] { new TileParameterContractSnapshot { Index = 0 } }
                }
            },
            TileSignatureCalls = new[]
            {
                new TileSignatureCallSnapshot { Location = "T::B@IL_0007", Target = "T::A(Terraria.Tile)", TileParameterCount = 1 }
            },
            TileReferenceFields = new[] { "T::_tile" },
            TileRuntimeTypeUses = new Dictionary<string, int> { ["MemberwiseClone"] = 1 }
        };

        TileTransformationPlan plan = TileTransformationPlanner.CreatePlan(audit);
        Require(!plan.CanTransform, "An unlowered operation plan must fail closed.");
        Require(plan.Preflight.Violations.Count == 0, "Known fixture instruction shapes must pass lowering preflight.");
        Require(plan.Operations.Count == 12, "Every audited boundary must produce exactly one planner operation.");
        Require(plan.Operations.Select(operation => operation.Id).Distinct(StringComparer.Ordinal).Count() == plan.Operations.Count, "Planner operation IDs must be stable and unique.");
        Require(plan.Blockers.Count == 12, "Every unlowered operation category must be represented by a blocker.");
    }

    private static void VerifyPreflightRejectsUnknownLoweringShape()
    {
        TileLoweringPreflight preflight = TileLoweringPreflight.Evaluate(new TileStorageAuditSnapshot
        {
            TileFieldInstructions = new[]
            {
                new TileMemberInstructionSnapshot { Location = "T::A@IL_0001", OpCode = "Ldflda", Member = "T.Tile::type" }
            },
            TileStackFlows = new[]
            {
                new TileStackFlowSnapshot { Location = "T::A@IL_0002", Outcomes = new[] { "UnsupportedInstruction:Box" } }
            }
        });

        Require(preflight.Violations.Count == 2, "Unknown field and stack shapes must block lowering explicitly.");
    }

    private static void VerifyFieldLoweringCatalogRejectsUnknownTileState()
    {
        Require(TileFieldLoweringCatalog.TryGet("System.UInt16 Terraria.Tile::type", out TileFieldLoweringDescriptor? type) && type!.GetterName == "GetTypeValue", "The type field must have an exact compact getter lowering.");
        Require(TileFieldLoweringCatalog.TryGet("System.Int16 Terraria.Tile::frameY", out TileFieldLoweringDescriptor? frameY) && frameY!.SetterName == "SetFrameY", "The frameY field must have an exact compact setter lowering.");
        Require(!TileFieldLoweringCatalog.TryGet("System.Byte Terraria.Tile::unknown", out _), "Unknown Tile fields may not receive a fallback lowering.");

        TileLoweringPreflight preflight = TileLoweringPreflight.Evaluate(new TileStorageAuditSnapshot
        {
            TileFieldInstructions = new[]
            {
                new TileMemberInstructionSnapshot { Location = "T::A@IL_0001", OpCode = "Ldfld", Member = "System.Byte Terraria.Tile::unknown" }
            }
        });
        Require(preflight.Violations.Count == 1 && preflight.Violations[0].Contains("without compact lowering", StringComparison.Ordinal), "A field outside the version-locked catalog must block transformation.");
    }

    private static void VerifyMethodLoweringCatalogRejectsUnknownTileBehavior()
    {
        Require(TileMethodLoweringCatalog.TryGet("System.Boolean Terraria.Tile::active()", out TileMethodLoweringDescriptor? active) && active!.Kind == TileMethodLoweringKind.InstanceMethodBecomesHandleMethod, "Tile instance methods must be routed through the compact handle lowering.");
        Require(TileMethodLoweringCatalog.TryGet("System.Void Terraria.Tile::SmoothSlope(System.Int32,System.Int32,System.Boolean,System.Boolean)", out TileMethodLoweringDescriptor? smoothSlope) && smoothSlope!.Kind == TileMethodLoweringKind.StaticMethodRemainsStatic, "Tile static methods must retain their original static call shape.");
        Require(!TileMethodLoweringCatalog.TryGet("System.Void Terraria.Tile::Unknown()", out _), "Unknown Tile methods may not receive a fallback lowering.");

        TileLoweringPreflight preflight = TileLoweringPreflight.Evaluate(new TileStorageAuditSnapshot
        {
            TileMethodInstructions = new[]
            {
                new TileMemberInstructionSnapshot { Location = "T::A@IL_0001", OpCode = "Callvirt", Member = "System.Void Terraria.Tile::Unknown()" }
            }
        });
        Require(preflight.Violations.Count == 1 && preflight.Violations[0].Contains("without compact lowering", StringComparison.Ordinal), "A method outside the version-locked catalog must block transformation.");
    }

    private static void VerifyEmptyAuditFailsClosed()
    {
        TileTransformationPlan plan = TileTransformationPlanner.CreatePlan(new TileStorageAuditSnapshot { Sha256 = "test" });
        Require(!plan.CanTransform, "An empty audit may not produce a transform-ready plan.");
        Require(plan.Blockers.Count == 1 && plan.Blockers[0].Contains("complete operation ledger", StringComparison.Ordinal), "The empty-audit failure should explain the missing ledger.");
    }

    private static void VerifyCopyOnlyTransactionPreservesInputAndRollsBackOutput()
    {
        string root = CreateTestDirectory();
        string input = Path.Combine(root, "Terraria.exe");
        string output = Path.Combine(root, "Alacrity.TileStorageTest.exe");
        try
        {
            File.WriteAllBytes(input, new byte[] { 1, 2, 3, 4 });
            string inputHash = Hash(input);
            var transaction = new CopyOnlyPatchTransaction(input, output, inputHash);
            CopyOnlyPatchReceipt receipt = transaction.Commit(AppendMarkerByte);

            Require(File.Exists(output), "A successful copy-only transaction must create its output.");
            Require(Hash(input) == inputHash, "The source executable must remain byte-identical.");
            Require(Hash(output) == receipt.OutputHash, "The receipt must bind rollback to the produced output bytes.");
            receipt.RollbackOutput();
            Require(!File.Exists(output), "Rollback must remove only the output copy.");
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static void VerifyCopyOnlyTransactionCleansUpAfterFailure()
    {
        string root = CreateTestDirectory();
        string input = Path.Combine(root, "Terraria.exe");
        string output = Path.Combine(root, "Alacrity.TileStorageTest.exe");
        try
        {
            File.WriteAllBytes(input, new byte[] { 8, 9 });
            var transaction = new CopyOnlyPatchTransaction(input, output, Hash(input));
            bool threw = false;
            try
            {
                transaction.Commit(_ => throw new InvalidOperationException("synthetic transform failure"));
            }
            catch (InvalidOperationException exception) when (exception.Message == "synthetic transform failure")
            {
                threw = true;
            }

            Require(threw, "The transaction must report a transform failure.");
            Require(!File.Exists(output), "A failed transform may not leave an output executable.");
            Require(Directory.GetFiles(root, "*.staging").Length == 0, "A failed transform may not leave a staging executable.");
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static void VerifyFixtureFieldLoweringUsesCompactValueStorage()
    {
        string root = CreateTestDirectory();
        string source = Path.Combine(AppContext.BaseDirectory, "TileStorageTransform.Fixture.dll");
        string output = Path.Combine(root, "TileStorageTransform.Fixture.Lowered.dll");
        try
        {
            Require(File.Exists(source), "The compiled tile-lowering fixture must be copied beside the tests.");
            FixtureTileLoweringResult result = new FixtureTileFieldLowerer().Rewrite(source, output);
            Require(result.RewrittenMethods == 10, "The fixture lowerer must rewrite every approved fixture storage method.");

            var context = new AssemblyLoadContext("TileStorageFixtureLowered", isCollectible: true);
            using FileStream stream = File.OpenRead(output);
            Assembly assembly = context.LoadFromStream(stream);
            Type main = assembly.GetType("TileStorageTransformFixture.Main", throwOnError: true)!;
            Invoke(main, "Initialize", 4, 3);
            Invoke(main, "WriteType", 2, 1, (ushort)87);
            Invoke(main, "WriteFrameX", 2, 1, (short)-36);
            Require((ushort)Invoke(main, "ReadType", 2, 1)! == 87, "The compact fixture storage must preserve unsigned tile field writes.");
            Require((short)Invoke(main, "ReadFrameX", 2, 1)! == -36, "The compact fixture storage must preserve signed frame field writes.");
            Require(!(bool)Invoke(main, "IsMissing", 2, 1)!, "Written cells must remain materialized.");
            Invoke(main, "Clear", 2, 1);
            Require((bool)Invoke(main, "IsMissing", 2, 1)!, "Cleared cells must restore null-slot semantics.");
            Invoke(main, "EnsureAndWriteType", 2, 1, (ushort)42);
            Require((ushort)Invoke(main, "ReadType", 2, 1)! == 42 && !(bool)Invoke(main, "IsMissing", 2, 1)!, "Lazy materialization must preserve subsequent writes.");
            Invoke(main, "CopyCell", 2, 1, 3, 2);
            Require((ushort)Invoke(main, "ReadType", 3, 2)! == 42, "Copy construction must preserve compact tile state.");

            FieldInfo tile = main.GetField("tile", BindingFlags.Public | BindingFlags.Static)!;
            Require(tile.FieldType.IsArray && tile.FieldType.GetArrayRank() == 1, "The lowered fixture must replace Tile[,] with a flat compact array.");
            Require(tile.FieldType.GetElementType()!.Name == "CompactTileData", "The lowered fixture must store a value record instead of Tile objects.");
            context.Unload();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static void VerifyReusableFieldInstructionLowering()
    {
        string root = CreateTestDirectory();
        string source = Path.Combine(AppContext.BaseDirectory, "TileStorageTransform.Fixture.dll");
        string output = Path.Combine(root, "TileStorageTransform.Fixture.FieldCalls.dll");
        try
        {
            using (AssemblyDefinition sourceAssembly = AssemblyDefinition.ReadAssembly(source))
            {
                TypeDefinition tile = sourceAssembly.MainModule.GetType("TileStorageTransformFixture.Tile")!;
                TypeDefinition sourceMain = sourceAssembly.MainModule.GetType("TileStorageTransformFixture.Main")!;
                TypeDefinition sourceRuntime = sourceAssembly.MainModule.GetType("TileStorageTransformFixture.TileRuntime")!;
                MethodDefinition[] fieldFixtureMethods = sourceMain.Methods
                    .Where(method => method.Name == "ReadType" || method.Name == "WriteType" || method.Name == "ReadFrameX" || method.Name == "WriteFrameX")
                    .ToArray();
                int rewritten = TileFieldInstructionLowerer.Rewrite(tile, sourceRuntime, fieldFixtureMethods);
                Require(rewritten == 4, "The reusable field lowerer must rewrite each approved fixture Tile field instruction.");
                sourceAssembly.Write(output);
            }

            var context = new AssemblyLoadContext("TileStorageFixtureFieldCalls", isCollectible: true);
            using FileStream stream = File.OpenRead(output);
            Assembly loweredAssembly = context.LoadFromStream(stream);
            Type loweredMain = loweredAssembly.GetType("TileStorageTransformFixture.Main", throwOnError: true)!;
            Type loweredRuntime = loweredAssembly.GetType("TileStorageTransformFixture.TileRuntime", throwOnError: true)!;
            Invoke(loweredMain, "Initialize", 3, 2);
            Invoke(loweredMain, "WriteType", 1, 1, (ushort)52);
            Invoke(loweredMain, "WriteFrameX", 1, 1, (short)-72);
            Require((ushort)Invoke(loweredMain, "ReadType", 1, 1)! == 52 && (short)Invoke(loweredMain, "ReadFrameX", 1, 1)! == -72, "Rewritten Tile field operations must retain their original behavior.");
            Require((int)loweredRuntime.GetField("FieldCallCount", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)! == 4, "Rewritten field operations must execute the compact runtime calls.");
            context.Unload();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static void VerifyReusableTileMethodInstructionLowering()
    {
        string root = CreateTestDirectory();
        string source = Path.Combine(AppContext.BaseDirectory, "TileStorageTransform.Fixture.dll");
        string output = Path.Combine(root, "TileStorageTransform.Fixture.MethodCalls.dll");
        try
        {
            using (AssemblyDefinition sourceAssembly = AssemblyDefinition.ReadAssembly(source))
            {
                TypeDefinition tile = sourceAssembly.MainModule.GetType("TileStorageTransformFixture.Tile")!;
                TypeDefinition sourceMain = sourceAssembly.MainModule.GetType("TileStorageTransformFixture.Main")!;
                TypeDefinition sourceRuntime = sourceAssembly.MainModule.GetType("TileStorageTransformFixture.TileRuntime")!;
                MethodDefinition active = tile.Methods.Single(method => method.Name == "active");
                TileFieldInstructionLowerer.Rewrite(tile, sourceRuntime, new[] { active });
                int rewritten = TileMethodInstructionLowerer.Rewrite(tile, sourceMain.Methods);
                Require(rewritten == 1 && active.IsStatic && active.Parameters.Count == 1, "The reusable method lowerer must make Tile instance calls explicit-receiver calls.");
                sourceAssembly.Write(output);
            }

            var context = new AssemblyLoadContext("TileStorageFixtureMethodCalls", isCollectible: true);
            using FileStream stream = File.OpenRead(output);
            Assembly loweredAssembly = context.LoadFromStream(stream);
            Type loweredMain = loweredAssembly.GetType("TileStorageTransformFixture.Main", throwOnError: true)!;
            Invoke(loweredMain, "Initialize", 2, 2);
            Invoke(loweredMain, "WriteType", 0, 0, (ushort)1);
            Require((bool)Invoke(loweredMain, "ReadActive", 0, 0)!, "A rewritten Tile instance call must preserve receiver behavior.");
            context.Unload();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static object? Invoke(Type type, string name, params object[] arguments)
    {
        MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"The lowered fixture method {name} was not found.");
        try
        {
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw new InvalidOperationException(
                $"The lowered fixture method {name} failed with {exception.InnerException.GetType().FullName}: {exception.InnerException.Message}{Environment.NewLine}{exception.InnerException.StackTrace}",
                exception.InnerException);
        }
    }

    private static string CreateTestDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "AlacrityTileStorageTransformTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTestDirectory(string root)
    {
        foreach (string file in Directory.GetFiles(root))
            File.Delete(file);
        Directory.Delete(root);
    }

    private static string Hash(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static void AppendMarkerByte(string path)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
        stream.WriteByte(5);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
