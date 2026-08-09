using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

public sealed class CecilPatchPrimitiveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alacrity.ClientBuilder.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExactTypeMethodAndUniqueAnchorAreResolvedAndPersisted()
    {
        var path = CreateFixture();
        using (var module = ModuleDefinition.ReadModule(path))
        {
            var type = CecilPatchPrimitives.RequireType(module, "Fixture.Container/Nested");
            var method = CecilPatchPrimitives.RequireMethod(type, "Run", "System.Void", "System.Int32");
            var anchor = CecilPatchPrimitives.RequireUniqueInstruction(method, instruction => instruction.OpCode == OpCodes.Ret, "return");
            var processor = method.Body.GetILProcessor();
            CecilPatchPrimitives.InsertBefore(processor, anchor, processor.Create(OpCodes.Nop));
            module.Write(path + ".patched.dll");
        }

        using var reopened = ModuleDefinition.ReadModule(path + ".patched.dll");
        var reopenedMethod = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(reopened, "Fixture.Container/Nested"), "Run", "System.Void", "System.Int32");
        Assert.Equal(OpCodes.Nop, reopenedMethod.Body.Instructions[^2].OpCode);
        Assert.Equal(OpCodes.Ret, reopenedMethod.Body.Instructions[^1].OpCode);
    }

    [Fact]
    public void MissingAndAmbiguousAnchorsFailClosed()
    {
        var path = CreateFixture();
        using var module = ModuleDefinition.ReadModule(path);
        var method = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(module, "Fixture.Container/Nested"), "Run", "System.Void", "System.Int32");

        Assert.Throws<ClientBuildException>(() => CecilPatchPrimitives.RequireUniqueInstruction(method, instruction => instruction.OpCode == OpCodes.Throw, "throw"));
        Assert.Throws<ClientBuildException>(() => CecilPatchPrimitives.RequireUniqueInstruction(method, instruction => instruction.OpCode == OpCodes.Nop, "nop"));
    }

    [Fact]
    public void BranchAndExceptionHandlerBoundariesRemainValidWhenInsertingBeforeReturn()
    {
        var path = CreateExceptionFixture();
        using (var module = ModuleDefinition.ReadModule(path))
        {
            var method = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(module, "Fixture.ExceptionFixture"), "Run", "System.Void");
            var returnInstruction = CecilPatchPrimitives.RequireUniqueInstruction(method, instruction => instruction.OpCode == OpCodes.Ret, "return");
            CecilPatchPrimitives.InsertBefore(method.Body.GetILProcessor(), returnInstruction, Instruction.Create(OpCodes.Nop));
            module.Write(path + ".patched.dll");
        }

        using var reopened = ModuleDefinition.ReadModule(path + ".patched.dll");
        var reopenedMethod = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(reopened, "Fixture.ExceptionFixture"), "Run", "System.Void");
        Assert.Single(reopenedMethod.Body.ExceptionHandlers);
        Assert.NotNull(reopenedMethod.Body.ExceptionHandlers[0].TryStart);
        Assert.NotNull(reopenedMethod.Body.ExceptionHandlers[0].HandlerStart);
    }

    [Fact]
    public void PatchDefinitionsRejectDuplicateOrOutOfOrderDependencies()
    {
        var operation = new ClientPatchOperation("fixture.operation", "Fixture", "fixture", "Ping");
        var first = new ClientPatchDefinition("first", (_, _) => { }, _ => false, new[] { operation });
        var duplicate = new ClientPatchDefinition("first", (_, _) => { }, _ => false, new[] { new ClientPatchOperation("fixture.operation-two", "Fixture", "fixture", "Ping") });
        Assert.Throws<ClientBuildException>(() => PermanentPatchCatalog.ValidateDefinitions(new[] { first, duplicate }));

        var dependent = new ClientPatchDefinition("dependent", (_, _) => { }, _ => false, new[] { new ClientPatchOperation("fixture.dependent", "Fixture", "fixture", "Ping") }, "provider");
        var provider = new ClientPatchDefinition("provider", (_, _) => { }, _ => false, new[] { new ClientPatchOperation("fixture.provider", "Fixture", "fixture", "Ping") });
        Assert.Throws<ClientBuildException>(() => PermanentPatchCatalog.ValidateDefinitions(new[] { dependent, provider }));
    }

    [Fact]
    public void IndividualPatchRepeatIsRejectedWithoutDuplicatingBridgeCalls()
    {
        var path = CreateFixture();
        using var module = ModuleDefinition.ReadModule(path);
        var operation = new ClientPatchOperation("fixture.ping", "Fixture.Container/Nested", "Insert Ping before return", "Ping");
        var definition = new ClientPatchDefinition(
            "fixture.patch",
            (target, _) =>
            {
                var method = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(target, "Fixture.Container/Nested"), "Run", "System.Void", "System.Int32");
                var anchor = CecilPatchPrimitives.RequireUniqueInstruction(method, instruction => instruction.OpCode == OpCodes.Ret, "return");
                var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", target, target.TypeSystem.CoreLibrary);
                var ping = new MethodReference("Ping", target.TypeSystem.Void, bridgeType) { HasThis = false };
                CecilPatchPrimitives.InsertBefore(method.Body.GetILProcessor(), anchor, Instruction.Create(OpCodes.Call, ping));
            },
            target => CountPingCalls(target) > 0,
            new[] { operation });

        var results = PermanentPatchCatalog.ApplyDefinitions(module, path, new[] { definition });
        Assert.Single(results);
        Assert.Equal(1, CountPingCalls(module));
        Assert.Throws<ClientBuildException>(() => PermanentPatchCatalog.ApplyDefinitions(module, path, new[] { definition }));
        Assert.Equal(1, CountPingCalls(module));
    }

    [Fact]
    public void PatchPostconditionRejectsBridgeCallsInsertedIntoAnUnrelatedTarget()
    {
        var path = CreateFixture();
        using var module = ModuleDefinition.ReadModule(path);
        var target = new ClientPatchTarget("fixture.run", "Fixture.Container/Nested", "Run(System.Int32)", "return", "insert Ping", "Ping");
        var operation = new ClientPatchOperation("fixture.targeted", "Fixture.Container/Nested", "targeted fixture", new[] { target }, "Ping");
        var definition = new ClientPatchDefinition(
            "fixture.targeted.patch",
            (assembly, _) =>
            {
                var unrelated = new TypeDefinition("Fixture", "Unrelated", TypeAttributes.Public | TypeAttributes.Class, assembly.TypeSystem.Object);
                assembly.Types.Add(unrelated);
                var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, assembly.TypeSystem.Void);
                unrelated.Methods.Add(method);
                var facade = new AssemblyNameReference(BridgeAbiContractCatalog.FacadeAssemblyName, new Version(1, 0));
                assembly.AssemblyReferences.Add(facade);
                var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", assembly, facade);
                var ping = new MethodReference("Ping", assembly.TypeSystem.Void, bridgeType) { HasThis = false };
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, ping));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            },
            assembly => CountPingCalls(assembly) > 0,
            new[] { operation });

        var exception = Assert.Throws<ClientBuildException>(() => PermanentPatchCatalog.ApplyDefinitions(module, path, new[] { definition }));
        Assert.Contains("fixture.run", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchPostconditionRejectsBridgeCallsInTheWrongOverload()
    {
        var path = CreateFixture();
        using var module = ModuleDefinition.ReadModule(path);
        var nested = CecilPatchPrimitives.RequireType(module, "Fixture.Container/Nested");
        var wrongOverload = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        wrongOverload.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        nested.Methods.Add(wrongOverload);

        var target = new ClientPatchTarget("fixture.run", "Fixture.Container/Nested", "Run(System.Int32)", "return", "insert Ping", "Ping");
        var operation = new ClientPatchOperation("fixture.targeted", "Fixture.Container/Nested", "targeted fixture", new[] { target }, "Ping");
        var definition = new ClientPatchDefinition(
            "fixture.targeted.patch",
            (assembly, _) =>
            {
                var facade = new AssemblyNameReference(BridgeAbiContractCatalog.FacadeAssemblyName, new Version(1, 0));
                assembly.AssemblyReferences.Add(facade);
                var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", assembly, facade);
                var ping = new MethodReference("Ping", assembly.TypeSystem.Void, bridgeType) { HasThis = false };
                wrongOverload.Body.GetILProcessor().InsertBefore(wrongOverload.Body.Instructions[0], Instruction.Create(OpCodes.Call, ping));
            },
            assembly => CountPingCalls(assembly) > 0,
            new[] { operation });

        var exception = Assert.Throws<ClientBuildException>(() => PermanentPatchCatalog.ApplyDefinitions(module, path, new[] { definition }));
        Assert.Contains("fixture.run", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchPostconditionRejectsDuplicateBridgeCallsInTheCorrectTarget()
    {
        var path = CreateFixture();
        using var module = ModuleDefinition.ReadModule(path);
        var target = new ClientPatchTarget("fixture.run", "Fixture.Container/Nested", "Run(System.Int32)", "return", "insert Ping", "Ping");
        var operation = new ClientPatchOperation("fixture.targeted", "Fixture.Container/Nested", "targeted fixture", new[] { target }, "Ping");
        var definition = new ClientPatchDefinition(
            "fixture.duplicate-call.patch",
            (assembly, _) =>
            {
                MethodDefinition method = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(assembly, "Fixture.Container/Nested"), "Run", "System.Void", "System.Int32");
                Instruction ret = CecilPatchPrimitives.RequireUniqueInstruction(method, instruction => instruction.OpCode == OpCodes.Ret, "return");
                MethodReference ping = CreatePing(assembly);
                method.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call, ping));
                method.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call, ping));
            },
            assembly => CountPingCalls(assembly) > 0,
            new[] { operation });

        ClientBuildException exception = Assert.Throws<ClientBuildException>(() => PermanentPatchCatalog.ApplyDefinitions(module, path, new[] { definition }));
        Assert.Contains("expected 1 call(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllReturnPatchPostconditionRequiresABridgeCallBeforeEveryReturn()
    {
        var path = CreateFixture();
        using var module = ModuleDefinition.ReadModule(path);
        MethodDefinition method = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(module, "Fixture.Container/Nested"), "Run", "System.Void", "System.Int32");
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var target = new ClientPatchTarget("fixture.run", "Fixture.Container/Nested", "Run(System.Int32)", "every return", "insert before return", "Ping");
        var operation = new ClientPatchOperation("fixture.targeted", "Fixture.Container/Nested", "targeted fixture", new[] { target }, "Ping");
        var definition = new ClientPatchDefinition(
            "fixture.all-returns.patch",
            (assembly, _) =>
            {
                MethodDefinition targetMethod = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(assembly, "Fixture.Container/Nested"), "Run", "System.Void", "System.Int32");
                Instruction firstReturn = targetMethod.Body.Instructions.First(instruction => instruction.OpCode == OpCodes.Ret);
                targetMethod.Body.GetILProcessor().InsertBefore(firstReturn, Instruction.Create(OpCodes.Call, CreatePing(assembly)));
            },
            assembly => CountPingCalls(assembly) > 0,
            new[] { operation });

        ClientBuildException exception = Assert.Throws<ClientBuildException>(() => PermanentPatchCatalog.ApplyDefinitions(module, path, new[] { definition }));
        Assert.Contains("expected 2 call(s)", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string CreateFixture()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.dll");
        using var module = ModuleDefinition.CreateModule("Fixture", ModuleKind.Dll);
        var container = new TypeDefinition("Fixture", "Container", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var nested = new TypeDefinition(string.Empty, "Nested", TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object);
        container.NestedTypes.Add(nested);
        module.Types.Add(container);
        var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        nested.Methods.Add(method);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        module.Write(path);
        return path;
    }

    private static int CountPingCalls(ModuleDefinition module)
    {
        var count = 0;
        foreach (var type in module.Types)
        {
            CountPingCalls(type, ref count);
        }

        return count;
    }

    private static MethodReference CreatePing(ModuleDefinition assembly)
    {
        var facade = new AssemblyNameReference(BridgeAbiContractCatalog.FacadeAssemblyName, new Version(1, 0));
        assembly.AssemblyReferences.Add(facade);
        var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", assembly, facade);
        return new MethodReference("Ping", assembly.TypeSystem.Void, bridgeType) { HasThis = false };
    }

    private static void CountPingCalls(TypeDefinition type, ref int count)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MethodReference reference && reference.DeclaringType.FullName == "AlacrityTerraria.PluginUiRuntime" && reference.Name == "Ping")
                {
                    count++;
                }
            }
        }

        foreach (var nestedType in type.NestedTypes)
        {
            CountPingCalls(nestedType, ref count);
        }
    }

    private string CreateExceptionFixture()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "exception-fixture.dll");
        using var module = ModuleDefinition.CreateModule("Fixture", ModuleKind.Dll);
        var type = new TypeDefinition("Fixture", "ExceptionFixture", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        type.Methods.Add(method);
        var tryStart = Instruction.Create(OpCodes.Nop);
        var leaveTry = Instruction.Create(OpCodes.Leave_S, Instruction.Create(OpCodes.Ret));
        var handlerStart = Instruction.Create(OpCodes.Pop);
        var leaveHandler = Instruction.Create(OpCodes.Leave_S, leaveTry.Operand as Instruction ?? throw new InvalidOperationException());
        var returnInstruction = leaveTry.Operand as Instruction ?? throw new InvalidOperationException();
        method.Body.Instructions.Add(tryStart);
        method.Body.Instructions.Add(leaveTry);
        method.Body.Instructions.Add(handlerStart);
        method.Body.Instructions.Add(leaveHandler);
        method.Body.Instructions.Add(returnInstruction);
        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            CatchType = module.ImportReference(typeof(Exception)),
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = returnInstruction
        });
        module.Write(path);
        return path;
    }
}
