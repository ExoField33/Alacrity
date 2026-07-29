using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyEveryAuditedBoundaryProducesAnOperation();
            VerifyMigrationManifestGroupsExactRewriteDomains();
            VerifyMigrationManifestRejectsAmbiguousLocations();
            VerifyValueSignatureLowererRequiresCompleteMigratedCallers();
            VerifyReferenceFieldLowererRequiresCompleteConsumers();
            VerifyByReferenceSignatureLowererRequiresCompleteMigratedCallers();
            VerifyOutSignatureLowererRequiresCompleteMigratedCallers();
            VerifyTileConstructorLowererRejectsUnknownConstructors();
            VerifyTileNullLowererRewritesOnlyExactProducers();
            VerifyTileLocalAliasLowererRequiresExactTileLocal();
            VerifyPreflightRejectsUnknownLoweringShape();
            VerifyFieldLoweringCatalogRejectsUnknownTileState();
            VerifyMethodLoweringCatalogRejectsUnknownTileBehavior();
            VerifyEmptyAuditFailsClosed();
            VerifyCopyOnlyTransactionPreservesInputAndRollsBackOutput();
            VerifyCopyOnlyTransactionCleansUpAfterFailure();
            VerifyCecilStagingTransactionWritesOnlyVerifiedCopy();
            VerifySourceFixtureHasExpectedRectangularArrayPatterns();
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

    private static void VerifyMigrationManifestGroupsExactRewriteDomains()
    {
        var audit = new TileStorageAuditSnapshot
        {
            Sha256 = "test",
            TileArrayGetFlows = new[] { new TileArrayGetFlowSnapshot { Location = "T::Read@IL_0001", Strategy = "AddressableFieldRead", Consumer = "Ldfld" } },
            TileFieldInstructions = new[] { new TileMemberInstructionSnapshot { Location = "T::Read@IL_0002", OpCode = "Ldfld", Member = "Terraria.Tile::type" } },
            TileSignatureContracts = new[] { new TileSignatureContractSnapshot { Method = "T::Use(Terraria.Tile)", TileParameters = new[] { new TileParameterContractSnapshot { Index = 0 } } } },
            TileSignatureCalls = new[] { new TileSignatureCallSnapshot { Location = "T::Read@IL_0003", Target = "T::Use(Terraria.Tile)", TileParameterCount = 1 } },
            TileReferenceFields = new[] { "T::_tile" },
            TileRuntimeTypeUses = new Dictionary<string, int> { ["SystemObjectCall:MemberwiseClone/0"] = 1 }
        };

        TileMigrationManifest manifest = TileMigrationManifestBuilder.Create(audit);
        Require(!manifest.CanTransform, "The migration manifest is planning-only until a complete verified lowerer exists.");
        Require(manifest.MethodDomains.Count == 1 && manifest.MethodDomains[0].Method == "T::Read", "Operations from one IL method must share one rewrite domain.");
        Require(manifest.MethodDomains[0].Operations.Count == 3, "The method domain must retain every audited operation.");
        Require(manifest.MethodDomains[0].CalledTileSignatureTargets.SequenceEqual(new[] { "T::Use(Terraria.Tile)" }), "The method domain must record exact Tile-signature dependencies.");
        Require(manifest.SignatureDomains.Single().CallSites.SequenceEqual(new[] { "T::Read@IL_0003" }), "The signature domain must retain every exact caller location.");
        Require(manifest.TileReferenceFields.SequenceEqual(new[] { "T::_tile" }) && manifest.RuntimeDomains.Single().Count == 1, "Non-method Tile domains must remain explicit in the manifest.");
    }

    private static void VerifyMigrationManifestRejectsAmbiguousLocations()
    {
        bool rejected = false;
        try
        {
            _ = TileMigrationManifestBuilder.Create(new TileStorageAuditSnapshot
            {
                Sha256 = "test",
                TileArrayGetFlows = new[] { new TileArrayGetFlowSnapshot { Location = "T::Read", Strategy = "AddressableFieldRead", Consumer = "Ldfld" } }
            });
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("exact IL location", StringComparison.Ordinal))
        {
            rejected = true;
        }

        Require(rejected, "A migration manifest must reject a source operation without an exact IL location.");
    }

    private static void VerifyValueSignatureLowererRequiresCompleteMigratedCallers()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("TileSignatureFixture", ModuleKind.Dll);
        TypeDefinition tile = AddType(module, "Tile");
        TypeDefinition handle = AddType(module, "TileHandle");
        TypeDefinition host = AddType(module, "Host");
        var handleField = new FieldDefinition("Current", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, handle);
        host.Fields.Add(handleField);

        MethodDefinition get = AddStaticMethod(host, "Get", tile);
        get.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldsfld, handleField));
        get.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        MethodDefinition use = AddStaticMethod(host, "Use", module.TypeSystem.Void, tile);
        use.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        MethodDefinition caller = AddStaticMethod(host, "Caller", module.TypeSystem.Void);
        ILProcessor callerIl = caller.Body.GetILProcessor();
        callerIl.Append(Instruction.Create(OpCodes.Call, get));
        callerIl.Append(Instruction.Create(OpCodes.Pop));
        callerIl.Append(Instruction.Create(OpCodes.Ldsfld, handleField));
        callerIl.Append(Instruction.Create(OpCodes.Call, use));
        callerIl.Append(Instruction.Create(OpCodes.Ret));

        TileValueSignatureRewrite[] rewrites =
        {
            new(get, RewriteReturn: true, Array.Empty<int>()),
            new(use, RewriteReturn: false, new[] { 0 })
        };
        bool missingCallerRejected = false;
        try
        {
            _ = TileValueSignatureLowerer.RewriteAfterBodyLowering(tile, handle, rewrites, Array.Empty<MethodDefinition>());
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("unmigrated incoming call", StringComparison.Ordinal))
        {
            missingCallerRejected = true;
        }
        Require(missingCallerRejected, "The value-signature lowerer must reject every incoming call that was not explicitly migrated.");

        int rewritten = TileValueSignatureLowerer.RewriteAfterBodyLowering(tile, handle, rewrites, new[] { caller });
        Require(rewritten == 2 && get.ReturnType.FullName == handle.FullName && use.Parameters[0].ParameterType.FullName == handle.FullName, "The value-signature lowerer must apply only exact approved Tile value slots.");
        Require(caller.Body.Instructions.Where(instruction => instruction.Operand is MethodReference).All(instruction => instruction.Operand is MethodDefinition), "Migrated callers must use resolved rewritten method definitions.");

        bool rejected = false;
        try
        {
            _ = TileValueSignatureLowerer.RewriteAfterBodyLowering(tile, handle, new[] { new TileValueSignatureRewrite(get, true, Array.Empty<int>()) }, Array.Empty<MethodDefinition>());
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("expected Tile return", StringComparison.Ordinal))
        {
            rejected = true;
        }
        Require(rejected, "The value-signature lowerer must reject a second or otherwise stale signature migration.");
    }

    private static void VerifyReferenceFieldLowererRequiresCompleteConsumers()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("TileFieldFixture", ModuleKind.Dll);
        TypeDefinition tile = AddType(module, "Tile");
        TypeDefinition handle = AddType(module, "TileHandle");
        TypeDefinition first = AddType(module, "First");
        TypeDefinition second = AddType(module, "Second");
        FieldDefinition firstField = new("Current", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, tile);
        FieldDefinition secondField = new("Current", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, tile);
        first.Fields.Add(firstField);
        second.Fields.Add(secondField);
        MethodDefinition firstConsumer = AddStaticMethod(first, "Read", module.TypeSystem.Void);
        firstConsumer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
        firstConsumer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Stsfld, firstField));
        firstConsumer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        MethodDefinition secondConsumer = AddStaticMethod(second, "Read", module.TypeSystem.Void);
        secondConsumer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
        secondConsumer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Stsfld, secondField));
        secondConsumer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));

        bool missingConsumerRejected = false;
        try
        {
            _ = TileReferenceFieldLowerer.RewriteAfterConsumerLowering(tile, handle, new[] { firstField }, Array.Empty<MethodDefinition>());
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("unmigrated consumer", StringComparison.Ordinal))
        {
            missingConsumerRejected = true;
        }
        Require(missingConsumerRejected, "The Tile reference-field lowerer must reject an unlowered field consumer.");

        int rewritten = TileReferenceFieldLowerer.RewriteAfterConsumerLowering(tile, handle, new[] { firstField }, new[] { firstConsumer });
        Require(rewritten == 1 && firstField.FieldType.FullName == handle.FullName, "The approved Tile reference field must move to the compact handle type.");
        Require(secondField.FieldType.FullName == tile.FullName, "Reference fields owned by other domains must remain unchanged.");
        Require(firstConsumer.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Stsfld).Operand is FieldDefinition, "Migrated consumers must use the resolved field definition.");
    }

    private static void VerifyByReferenceSignatureLowererRequiresCompleteMigratedCallers()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("TileByReferenceFixture", ModuleKind.Dll);
        TypeDefinition tile = AddType(module, "Tile");
        TypeDefinition handle = AddType(module, "TileHandle");
        TypeDefinition host = AddType(module, "Host");
        MethodDefinition mutate = AddStaticMethod(host, "Mutate", module.TypeSystem.Void, new ByReferenceType(tile));
        mutate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        MethodDefinition caller = AddStaticMethod(host, "Caller", module.TypeSystem.Void);
        caller.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
        caller.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Call, mutate));
        caller.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));

        bool missingCallerRejected = false;
        try
        {
            _ = TileByReferenceSignatureLowerer.RewriteAfterBodyLowering(tile, handle, new[] { new TileByReferenceSignatureRewrite(mutate, 0) }, Array.Empty<MethodDefinition>());
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("unmigrated incoming call", StringComparison.Ordinal))
        {
            missingCallerRejected = true;
        }
        Require(missingCallerRejected, "The Tile by-reference lowerer must reject an unlowered caller.");

        int rewritten = TileByReferenceSignatureLowerer.RewriteAfterBodyLowering(tile, handle, new[] { new TileByReferenceSignatureRewrite(mutate, 0) }, new[] { caller });
        Require(rewritten == 1 && mutate.Parameters[0].ParameterType.FullName == handle.FullName, "The approved mutation-only ref Tile parameter must move to a compact handle value.");
        Require(caller.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call).Operand is MethodDefinition, "Migrated callers must use the resolved by-reference target definition.");
    }

    private static void VerifyOutSignatureLowererRequiresCompleteMigratedCallers()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("TileOutFixture", ModuleKind.Dll);
        TypeDefinition tile = AddType(module, "Tile");
        TypeDefinition handle = AddType(module, "TileHandle");
        TypeDefinition host = AddType(module, "Host");
        MethodDefinition fill = AddStaticMethod(host, "Fill", module.TypeSystem.Void, new ByReferenceType(tile));
        fill.Parameters[0].IsOut = true;
        fill.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        MethodDefinition caller = AddStaticMethod(host, "Caller", module.TypeSystem.Void);
        caller.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
        caller.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Call, fill));
        caller.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));

        bool missingCallerRejected = false;
        try
        {
            _ = TileOutSignatureLowerer.RewriteAfterBodyLowering(tile, handle, new[] { new TileOutSignatureRewrite(fill, 0) }, Array.Empty<MethodDefinition>());
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("unmigrated incoming call", StringComparison.Ordinal))
        {
            missingCallerRejected = true;
        }
        Require(missingCallerRejected, "The Tile out-signature lowerer must reject an unlowered caller.");

        int rewritten = TileOutSignatureLowerer.RewriteAfterBodyLowering(tile, handle, new[] { new TileOutSignatureRewrite(fill, 0) }, new[] { caller });
        Require(rewritten == 1 && fill.Parameters[0].ParameterType is ByReferenceType parameter && parameter.ElementType.FullName == handle.FullName, "The approved out Tile parameter must move to an out compact handle.");
        Require(caller.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call).Operand is MethodDefinition, "Migrated out callers must use the resolved target definition.");
    }

    private static void VerifyTileConstructorLowererRejectsUnknownConstructors()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("TileConstructionFixture", ModuleKind.Dll);
        TypeDefinition tile = AddType(module, "Tile");
        MethodDefinition defaultConstructor = new(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        defaultConstructor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        tile.Methods.Add(defaultConstructor);
        MethodDefinition copyConstructor = new(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        copyConstructor.Parameters.Add(new ParameterDefinition("source", Mono.Cecil.ParameterAttributes.None, tile));
        copyConstructor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        tile.Methods.Add(copyConstructor);
        TypeDefinition handle = AddType(module, "TileHandle");
        TypeDefinition runtime = AddType(module, "Runtime");
        AddStaticMethod(runtime, "Create", handle).Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
        runtime.Methods[^1].Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        AddStaticMethod(runtime, "CreateCopy", handle, tile).Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
        runtime.Methods[^1].Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        TypeDefinition host = AddType(module, "Host");
        MethodDefinition allocate = AddStaticMethod(host, "Allocate", tile);
        allocate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Newobj, defaultConstructor));
        allocate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));

        int rewritten = TileHandleConstructionLowerer.Rewrite(tile, runtime, new[] { allocate });
        Require(rewritten == 1 && allocate.Body.Instructions[0].OpCode == OpCodes.Call, "The exact default Tile constructor must become a compact factory call.");

        MethodDefinition unsupportedConstructor = new(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        unsupportedConstructor.Parameters.Add(new ParameterDefinition("value", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
        unsupportedConstructor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        tile.Methods.Add(unsupportedConstructor);
        MethodDefinition unsupportedAllocate = AddStaticMethod(host, "Unsupported", tile);
        unsupportedAllocate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldc_I4_0));
        unsupportedAllocate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Newobj, unsupportedConstructor));
        unsupportedAllocate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        bool rejected = false;
        try
        {
            _ = TileHandleConstructionLowerer.Rewrite(tile, runtime, new[] { unsupportedAllocate });
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("Unsupported Tile constructor", StringComparison.Ordinal))
        {
            rejected = true;
        }
        Require(rejected, "An unknown Tile constructor must block compact lowering.");
    }

    private static void VerifyTileNullLowererRewritesOnlyExactProducers()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("TileNullFixture", ModuleKind.Dll);
        TypeDefinition handle = AddType(module, "TileHandle");
        TypeDefinition runtime = AddType(module, "Runtime");
        MethodDefinition isNull = AddStaticMethod(runtime, "IsNull", module.TypeSystem.Boolean, handle);
        isNull.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldc_I4_0));
        isNull.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        TypeDefinition host = AddType(module, "Host");
        FieldDefinition current = new("Current", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, handle);
        host.Fields.Add(current);
        MethodDefinition branch = AddStaticMethod(host, "Branch", module.TypeSystem.Void);
        ILProcessor branchIl = branch.Body.GetILProcessor();
        Instruction producer = Instruction.Create(OpCodes.Ldsfld, current);
        Instruction done = Instruction.Create(OpCodes.Ret);
        branchIl.Append(producer);
        branchIl.Append(Instruction.Create(OpCodes.Brfalse_S, done));
        branchIl.Append(done);

        int rewritten = TileHandleNullLowerer.Rewrite(runtime, new[] { new TileNullCheckRewrite(branch, producer) });
        Require(rewritten == 1 && branch.Body.Instructions[1].OpCode == OpCodes.Call && branch.Body.Instructions[2].OpCode == OpCodes.Brfalse_S, "A direct Tile null branch must receive exactly one IsNull call.");

        MethodDefinition comparison = AddStaticMethod(host, "Comparison", module.TypeSystem.Boolean);
        ILProcessor comparisonIl = comparison.Body.GetILProcessor();
        Instruction comparisonProducer = Instruction.Create(OpCodes.Ldsfld, current);
        comparisonIl.Append(comparisonProducer);
        comparisonIl.Append(Instruction.Create(OpCodes.Ldnull));
        comparisonIl.Append(Instruction.Create(OpCodes.Ceq));
        comparisonIl.Append(Instruction.Create(OpCodes.Ret));
        rewritten = TileHandleNullLowerer.Rewrite(runtime, new[] { new TileNullCheckRewrite(comparison, comparisonProducer) });
        Require(rewritten == 1 && comparison.Body.Instructions[1].OpCode == OpCodes.Call && comparison.Body.Instructions[2].OpCode == OpCodes.Nop, "A Tile == null comparison must become IsNull without retaining a null comparison.");
    }

    private static void VerifyTileLocalAliasLowererRequiresExactTileLocal()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("TileLocalFixture", ModuleKind.Dll);
        TypeDefinition tile = AddType(module, "Tile");
        TypeDefinition handle = AddType(module, "TileHandle");
        TypeDefinition host = AddType(module, "Host");
        MethodDefinition method = AddStaticMethod(host, "Use", module.TypeSystem.Void);
        method.Body.Variables.Add(new VariableDefinition(tile));
        method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
        method.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));

        int rewritten = TileHandleLocalLowerer.RewriteAfterBodyLowering(tile, handle, new[] { new TileLocalAliasRewrite(method, 0) });
        Require(rewritten == 1 && method.Body.Variables[0].VariableType.FullName == handle.FullName, "An explicit Tile local alias must become a compact handle local.");

        bool rejected = false;
        try
        {
            _ = TileHandleLocalLowerer.RewriteAfterBodyLowering(tile, handle, new[] { new TileLocalAliasRewrite(method, 1) });
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("exact Tile local", StringComparison.Ordinal))
        {
            rejected = true;
        }
        Require(rejected, "A non-Tile local must never be rewritten as a compact handle.");
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

    private static void VerifyCecilStagingTransactionWritesOnlyVerifiedCopy()
    {
        string root = CreateTestDirectory();
        string source = Path.Combine(AppContext.BaseDirectory, "TileStorageTransform.Fixture.dll");
        string input = Path.Combine(root, "Terraria.exe");
        string output = Path.Combine(root, "Alacrity.TileStorageTest.exe");
        try
        {
            File.Copy(source, input);
            string inputHash = Hash(input);
            var transaction = new CecilStagingPatchTransaction(input, output, inputHash);
            var plan = new TileTransformationPlan { CanTransform = true, InputSha256 = inputHash };
            CopyOnlyPatchReceipt receipt = transaction.Commit(plan, static (assembly, _) => assembly.Name.Name = "TileStorageTransform.StagingTest");

            Require(Hash(input) == inputHash, "The Cecil staging transaction must preserve its input assembly.");
            using (AssemblyDefinition transformed = AssemblyDefinition.ReadAssembly(output))
                Require(transformed.Name.Name == "TileStorageTransform.StagingTest", "The Cecil staging transaction must publish the rewritten staging assembly.");
            receipt.RollbackOutput();
            Require(!File.Exists(output), "The Cecil staging receipt must roll back only its output.");

            bool blocked = false;
            try
            {
                transaction = new CecilStagingPatchTransaction(input, output, inputHash);
                transaction.Commit(new TileTransformationPlan { InputSha256 = inputHash }, static (_, _) => throw new InvalidOperationException("must not run"));
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("not complete", StringComparison.Ordinal))
            {
                blocked = true;
            }

            Require(blocked && !File.Exists(output), "An incomplete plan must not create or rewrite an output assembly.");
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
            Require(result.RewrittenMethods == 37, "The fixture lowerer must rewrite every approved fixture storage and Tile-reference method.");
            string artifactDirectory = GetArtifactDirectory();
            Directory.CreateDirectory(artifactDirectory);
            string artifactPath = Path.Combine(artifactDirectory, "TileStorageTransform.Fixture.Lowered.dll");
            File.Copy(output, artifactPath, overwrite: true);
            FixtureMetadataDiagnostics.Write(artifactPath);
            VerifyLoweredFixtureStructure(artifactPath);

            var context = new AssemblyLoadContext("TileStorageFixtureLowered", isCollectible: true);
            using FileStream stream = File.OpenRead(output);
            Assembly assembly = context.LoadFromStream(stream);
            Type main = assembly.GetType("TileStorageTransformFixture.Main", throwOnError: true)!;
            FieldInfo tile = main.GetField("tile", BindingFlags.Public | BindingFlags.Static)!;
            Require(tile.FieldType.IsArray && tile.FieldType.GetArrayRank() == 1, "The lowered fixture must replace Tile[,] with a flat compact array.");
            Require(tile.FieldType.GetElementType()!.Name == "CompactTileData", "The lowered fixture must store a value record instead of Tile objects.");
            Invoke(main, "Initialize", 4, 3);
            Require((int)Invoke(main, "GetWidth")! == 4 && (int)Invoke(main, "GetHeight")! == 3, "Lowered GetLength calls must preserve both rectangular dimensions.");
            Require(!(bool)Invoke(main, "IsMissing", 0, 0)!, "The lowered fixture must preserve initialization materialization semantics.");
            Require((ushort)Invoke(main, "ReadTypeThroughLocal", 0, 0)! == 0, "A lowered Tile local must read the materialized compact cell.");
            Invoke(main, "WriteTypeViaParameter", 0, 0, (ushort)19);
            Require((ushort)Invoke(main, "ReadType", 0, 0)! == 19, "A lowered Tile parameter must preserve map-cell identity.");
            Invoke(main, "WriteTypeThroughAddress", 0, 0, (ushort)21);
            Require((ushort)Invoke(main, "ReadTypeThroughAddress", 0, 0)! == 21, "A lowered Tile[,] Address path must preserve live mutable cell access.");
            Invoke(main, "CopyTypeViaAddresses", 0, 0, 3, 0);
            Require((ushort)Invoke(main, "ReadType", 3, 0)! == 21, "Two lowered Tile[,] Address paths must preserve source and destination identity.");
            Require((ushort)Invoke(main, "ReadTypeThroughFraming", 0, 0)! == 21, "Framing.GetTileSafely must return a live compact Tile handle.");
            Require((ushort)Invoke(main, "ReadTypeThroughFloorTile", 0, 0)! == 21, "Tile-returning helper methods must return live compact Tile handles.");
            Require((bool)Invoke(main, "TryGetSittingBlock", 0, 0, null!)!, "out Tile helper methods must preserve materialized-cell success semantics.");
            Invoke(main, "ConvertTile", 0, 0, (ushort)22);
            Require((ushort)Invoke(main, "ReadType", 0, 0)! == 22, "ref Tile helper methods must mutate the compact cell identity.");
            Invoke(main, "StoreReferenceFields", 0, 0);
            Require((ushort)Invoke(main, "ReadReferenceFields")! == 88, "Every Tile reference field must retain the same compact Tile identity.");
            Require((ushort)Invoke(main, "ReadTypeViaByReference", 0, 0)! == 22, "A lowered Tile by-reference reader must preserve the referenced map cell.");
            Invoke(main, "CopyTypeViaByReference", 0, 0, 3, 0);
            Require((ushort)Invoke(main, "ReadType", 3, 0)! == 22, "A lowered Tile by-reference mutator must preserve the destination map-cell identity.");
            Invoke(main, "WriteTypeThroughReturnedCell", 1, 0, (ushort)20);
            Require((ushort)Invoke(main, "ReadType", 1, 0)! == 20, "A lowered Tile return must preserve map-cell identity.");
            Require(!(bool)Invoke(main, "ReturnedCellIsMissing", 1, 0)!, "A lowered Tile return must preserve non-null materialized cells.");
            Invoke(main, "WriteType", 2, 1, (ushort)87);
            Invoke(main, "WriteFrameX", 2, 1, (short)-36);
            Require((ushort)Invoke(main, "ReadType", 2, 1)! == 87, "The compact fixture storage must preserve unsigned tile field writes.");
            Require((short)Invoke(main, "ReadFrameX", 2, 1)! == -36, "The compact fixture storage must preserve signed frame field writes.");
            Require(!(bool)Invoke(main, "IsMissing", 2, 1)!, "Written cells must remain materialized.");
            Invoke(main, "Clear", 2, 1);
            Require((bool)Invoke(main, "IsMissing", 2, 1)!, "Cleared cells must restore null-slot semantics.");
            Require((bool)Invoke(main, "ReturnedCellIsMissing", 2, 1)!, "A lowered Tile return must preserve null cleared cells.");
            Invoke(main, "EnsureAndWriteType", 2, 1, (ushort)42);
            Require((ushort)Invoke(main, "ReadType", 2, 1)! == 42 && !(bool)Invoke(main, "IsMissing", 2, 1)!, "Lazy materialization must preserve subsequent writes.");
            Invoke(main, "WriteType", 0, 2, (ushort)31);
            Invoke(main, "StoreCell", 0, 2);
            Invoke(main, "Clear", 0, 2);
            Require((bool)Invoke(main, "IsMissing", 0, 2)! && !(bool)Invoke(main, "IsStoredMissing")!, "A compact Tile field must retain the displaced Tile identity after a cell is cleared.");
            Require((ushort)Invoke(main, "ReadStoredType")! == 31, "A compact Tile field must retain the displaced Tile data after a cell is cleared.");
            Invoke(main, "WriteStoredType", (ushort)32);
            Invoke(main, "EnsureAndWriteType", 0, 2, (ushort)33);
            Require((ushort)Invoke(main, "ReadStoredType")! == 32 && (ushort)Invoke(main, "ReadType", 0, 2)! == 33, "A re-materialized cell must not overwrite the displaced Tile field reference.");
            Invoke(main, "ClearStored");
            Require((bool)Invoke(main, "IsStoredMissing")!, "A lowered Tile reference field must preserve null assignment.");
            Invoke(main, "CopyCell", 2, 1, 3, 2);
            Require((ushort)Invoke(main, "ReadType", 3, 2)! == 42, "Copy construction must preserve compact tile state.");
            Invoke(main, "Initialize", 2, 5);
            Require((int)Invoke(main, "GetWidth")! == 2 && (int)Invoke(main, "GetHeight")! == 5, "Reinitialization must replace the active map dimensions.");
            Require(!(bool)Invoke(main, "IsMissing", 1, 4)!, "A replacement map must materialize the same default Tile cells as the source fixture.");

            context.Unload();
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static void VerifySourceFixtureHasExpectedRectangularArrayPatterns()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "TileStorageTransform.Fixture.dll");
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(source);
        TypeDefinition main = assembly.MainModule.GetType("TileStorageTransformFixture.Main")
            ?? throw new InvalidOperationException("The source fixture Main type was not found.");
        FieldDefinition tile = main.Fields.Single(field => field.Name == "tile");
        Require(tile.FieldType is ArrayType { Rank: 2 } array && array.ElementType.FullName == "TileStorageTransformFixture.Tile", "The source fixture must begin with Tile[,] storage.");

        RequireRectangularArrayCall(main, "Initialize", ".ctor");
        RequireRectangularArrayCall(main, "GetCell", "Get");
        RequireRectangularArrayCall(main, "Clear", "Set");
        RequireRectangularArrayCall(main, "GetCellAddress", "Address");
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
                sourceAssembly.Name.Name = "TileStorageTransform.Fixture.FieldCalls";
                sourceAssembly.MainModule.Name = "TileStorageTransform.Fixture.FieldCalls.dll";
                sourceAssembly.Write(output);
            }
            string artifactPath = Path.Combine(GetArtifactDirectory(), "TileStorageTransform.Fixture.FieldCalls.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            File.Copy(output, artifactPath, overwrite: true);
            Assembly loweredAssembly = Assembly.LoadFrom(artifactPath);
            Type loweredMain = loweredAssembly.GetType("TileStorageTransformFixture.Main", throwOnError: true)!;
            Type loweredRuntime = loweredAssembly.GetType("TileStorageTransformFixture.TileRuntime", throwOnError: true)!;
            Invoke(loweredMain, "Initialize", 3, 2);
            Invoke(loweredMain, "WriteType", 1, 1, (ushort)52);
            Invoke(loweredMain, "WriteFrameX", 1, 1, (short)-72);
            Require((ushort)Invoke(loweredMain, "ReadType", 1, 1)! == 52 && (short)Invoke(loweredMain, "ReadFrameX", 1, 1)! == -72, "Rewritten Tile field operations must retain their original behavior.");
            Require((int)loweredRuntime.GetField("FieldCallCount", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)! == 4, "Rewritten field operations must execute the compact runtime calls.");
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
                sourceAssembly.Name.Name = "TileStorageTransform.Fixture.MethodCalls";
                sourceAssembly.MainModule.Name = "TileStorageTransform.Fixture.MethodCalls.dll";
                sourceAssembly.Write(output);
            }

            string artifactPath = Path.Combine(GetArtifactDirectory(), "TileStorageTransform.Fixture.MethodCalls.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            File.Copy(output, artifactPath, overwrite: true);
            Assembly loweredAssembly = Assembly.LoadFrom(artifactPath);
            Type loweredMain = loweredAssembly.GetType("TileStorageTransformFixture.Main", throwOnError: true)!;
            Invoke(loweredMain, "Initialize", 2, 2);
            Invoke(loweredMain, "WriteType", 0, 0, (ushort)1);
            Require((bool)Invoke(loweredMain, "ReadActive", 0, 0)!, "A rewritten Tile instance call must preserve receiver behavior.");
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

    private static TypeDefinition AddType(ModuleDefinition module, string name)
    {
        var type = new TypeDefinition(string.Empty, name, Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        return type;
    }

    private static MethodDefinition AddStaticMethod(TypeDefinition owner, string name, TypeReference returnType, params TypeReference[] parameters)
    {
        var method = new MethodDefinition(name, Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, returnType);
        for (int index = 0; index < parameters.Length; index++)
            method.Parameters.Add(new ParameterDefinition($"arg{index}", Mono.Cecil.ParameterAttributes.None, parameters[index]));
        method.Body = new Mono.Cecil.Cil.MethodBody(method);
        owner.Methods.Add(method);
        return method;
    }

    private static void VerifyLoweredFixtureStructure(string assemblyPath)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { InMemory = true });
        TypeDefinition main = assembly.MainModule.GetType("TileStorageTransformFixture.Main")
            ?? throw new InvalidOperationException("The lowered fixture Main type was not found during structural validation.");
        FieldDefinition tile = main.Fields.Single(field => field.Name == "tile");
        Require(tile.FieldType is ArrayType { Rank: 1 } array && array.ElementType.FullName == "TileStorageTransformFixture.CompactTileData", "Structural validation must retain only the flat CompactTileData storage field.");

        foreach (MethodDefinition method in main.Methods)
        {
            Require(!ContainsFixtureTile(method.ReturnType), $"Structural validation found a Tile return in {method.FullName}.");
            Require(!method.Parameters.Any(parameter => ContainsFixtureTile(parameter.ParameterType)), $"Structural validation found a Tile parameter in {method.FullName}.");
            if (!method.HasBody)
                continue;

            Require(!method.Body.Variables.Any(variable => ContainsFixtureTile(variable.VariableType)), $"Structural validation found a Tile local in {method.FullName}.");
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MethodReference arrayMethod && IsRectangularTileArrayMember(arrayMethod))
                    throw new InvalidOperationException($"Structural validation found an unlowered Tile[,] {arrayMethod.Name} call in {method.FullName}.");
                if (instruction.Operand is FieldReference field && field.DeclaringType.FullName == "TileStorageTransformFixture.Tile")
                    throw new InvalidOperationException($"Structural validation found a Tile field reference in {method.FullName}.");
                if (instruction.Operand is MethodReference called && called.DeclaringType.FullName == "TileStorageTransformFixture.Tile")
                    throw new InvalidOperationException($"Structural validation found a Tile method reference in {method.FullName}.");
                if (instruction.Operand is TypeReference type && ContainsFixtureTile(type))
                    throw new InvalidOperationException($"Structural validation found a Tile type reference in {method.FullName}.");
            }
        }
    }

    private static void RequireRectangularArrayCall(TypeDefinition type, string methodName, string arrayMemberName)
    {
        MethodDefinition method = type.Methods.Single(candidate => candidate.Name == methodName && candidate.HasBody);
        Require(method.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called &&
            string.Equals(called.Name, arrayMemberName, StringComparison.Ordinal) &&
            IsRectangularTileArrayMember(called)),
            $"The source fixture {method.FullName} must contain Tile[,]::{arrayMemberName}.");
    }

    private static bool IsRectangularTileArrayMember(MethodReference reference)
    {
        return reference.DeclaringType is ArrayType { Rank: 2 } array &&
               array.ElementType.FullName == "TileStorageTransformFixture.Tile";
    }

    private static bool ContainsFixtureTile(TypeReference type)
    {
        if (type.FullName == "TileStorageTransformFixture.Tile")
            return true;
        if (type is GenericInstanceType generic && generic.GenericArguments.Any(ContainsFixtureTile))
            return true;
        return type is TypeSpecification specification && ContainsFixtureTile(specification.ElementType);
    }

    private static string GetArtifactDirectory()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools", "TileStorageTransform")))
            {
                string artifacts = Path.Combine(directory.FullName, "artifacts");
                Directory.CreateDirectory(artifacts);
                return artifacts;
            }
        }

        throw new InvalidOperationException("Could not locate the Alacrity repository root for fixture artifacts.");
    }

    private static void DeleteTestDirectory(string root)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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
