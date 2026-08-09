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

    [Fact]
    public void DroppedItemCullingUsesTheDrawItemIndexRatherThanAnEarlierIntegerLocal()
    {
        string path = CreateDroppedItemFixture();
        using (var module = ModuleDefinition.ReadModule(path))
        {
            TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
            PermanentPatchPlan.PatchDroppedItemDraw(main, CreateBridgeMethod(module, "ShouldDrawWorldItem", module.TypeSystem.Boolean, module.TypeSystem.Int32));
            module.Write(path + ".patched.dll");
        }

        using ModuleDefinition reopened = ModuleDefinition.ReadModule(path + ".patched.dll");
        MethodDefinition drawItems = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(reopened, "Terraria.Main"), "DrawItems", "System.Void");
        Instruction gate = Assert.Single(drawItems.Body.Instructions, instruction =>
            instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference called && called.Name == "ShouldDrawWorldItem");
        Assert.Equal(OpCodes.Ldloc_1, gate.Previous.OpCode);
        Assert.Equal(OpCodes.Brtrue, gate.Next.OpCode);
        Assert.True(gate.Next.Operand is Instruction resume && resume.OpCode == OpCodes.Ldsfld);
        Assert.True(gate.Next.Next.Operand is Instruction increment && increment.OpCode == OpCodes.Ldloc_1);
    }

    [Fact]
    public void ParticleCullingPreservesOneParticleFetchAndNativeLoopProgression()
    {
        string path = CreateParticleFixture();
        using (var module = ModuleDefinition.ReadModule(path))
        {
            TypeDefinition renderer = CecilPatchPrimitives.RequireType(module, "Terraria.Graphics.Renderers.ParticleRenderer");
            MethodDefinition draw = CecilPatchPrimitives.RequireMethod(renderer, "Draw", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch");
            int originalFetches = CountCalls(draw, "get_Item");
            PermanentPatchPlan.PatchWorldParticleDraw(
                module,
                renderer,
                CreateLocalParticleGate(module, renderer, CecilPatchPrimitives.RequireType(module, "Terraria.Graphics.Renderers.IParticle")));
            Assert.Equal(originalFetches, CountCalls(draw, "get_Item"));
            module.Write(path + ".patched.dll");
        }

        using ModuleDefinition reopened = ModuleDefinition.ReadModule(path + ".patched.dll");
        MethodDefinition patched = CecilPatchPrimitives.RequireMethod(CecilPatchPrimitives.RequireType(reopened, "Terraria.Graphics.Renderers.ParticleRenderer"), "Draw", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch");
        Instruction gate = Assert.Single(patched.Body.Instructions, instruction =>
            instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference called && called.Name == "ShouldDrawWorldParticle");
        Assert.Equal(1, CountCalls(patched, "Draw"));
        Assert.Equal(2, CountCalls(patched, "get_Item"));
        Assert.Equal(OpCodes.Brtrue, gate.Next.OpCode);
        Assert.True(gate.Next.Operand is Instruction resume && resume.OpCode.Code.ToString().StartsWith("Ldloc", StringComparison.Ordinal));
        Assert.True(gate.Next.Next.Operand is Instruction loopContinue && loopContinue.OpCode.Code.ToString().StartsWith("Ldloc", StringComparison.Ordinal));

        var assembly = System.Reflection.Assembly.Load(File.ReadAllBytes(path + ".patched.dll"));
        Type rendererType = assembly.GetType("Terraria.Graphics.Renderers.ParticleRenderer")!;
        Type particleType = assembly.GetType("Fixture.TestParticle")!;
        Type particleInterface = assembly.GetType("Terraria.Graphics.Renderers.IParticle")!;
        Type spriteBatchType = assembly.GetType("Microsoft.Xna.Framework.Graphics.SpriteBatch")!;
        Type bridgeType = assembly.GetType("AlacrityTerraria.PluginUiRuntime")!;
        var rendererInstance = Activator.CreateInstance(rendererType)!;
        var particleInstance = Activator.CreateInstance(particleType)!;
        Type listType = assembly.GetType("System.Collections.Generic.List`1")!.MakeGenericType(particleInterface);
        var particleList = Activator.CreateInstance(listType)!;
        particleList.GetType().GetMethod("Add")!.Invoke(particleList, new[] { particleInstance });
        rendererType.GetField("particles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(rendererInstance, particleList);
        object spriteBatch = Activator.CreateInstance(spriteBatchType)!;

        bridgeType.GetField("Allow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.SetValue(null, true);
        rendererType.GetMethod("Draw")!.Invoke(rendererInstance, new[] { spriteBatch });
        Assert.Equal(1, (int)particleType.GetField("DrawCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.GetValue(null)!);

        particleType.GetField("DrawCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.SetValue(null, 0);
        bridgeType.GetField("Allow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.SetValue(null, false);
        rendererType.GetMethod("Draw")!.Invoke(rendererInstance, new[] { spriteBatch });
        Assert.Equal(0, (int)particleType.GetField("DrawCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.GetValue(null)!);
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

    private string CreateDroppedItemFixture()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "dropped-item-fixture.dll");
        using var module = ModuleDefinition.CreateModule("Fixture", ModuleKind.Dll);
        var main = new TypeDefinition("Terraria", "Main", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(main);
        var items = new FieldDefinition("item", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Object);
        main.Fields.Add(items);
        var drawItem = new MethodDefinition("DrawItem", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        drawItem.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        drawItem.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        drawItem.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        main.Methods.Add(drawItem);
        var drawItems = new MethodDefinition("DrawItems", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        main.Methods.Add(drawItems);
        VariableDefinition unrelated = new VariableDefinition(module.TypeSystem.Int32);
        VariableDefinition itemIndex = new VariableDefinition(module.TypeSystem.Int32);
        drawItems.Body.Variables.Add(unrelated);
        drawItems.Body.Variables.Add(itemIndex);
        ILProcessor il = drawItems.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Stloc_0));
        Instruction itemLoad = Instruction.Create(OpCodes.Ldsfld, items);
        il.Append(itemLoad);
        il.Append(Instruction.Create(OpCodes.Pop));
        il.Append(Instruction.Create(OpCodes.Ldnull));
        il.Append(Instruction.Create(OpCodes.Ldloc_1));
        il.Append(Instruction.Create(OpCodes.Call, drawItem));
        Instruction increment = Instruction.Create(OpCodes.Ldloc_1);
        il.Append(increment);
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Add));
        il.Append(Instruction.Create(OpCodes.Stloc_1));
        il.Append(Instruction.Create(OpCodes.Ret));
        module.Write(path);
        return path;
    }

    private string CreateParticleFixture()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "particle-fixture.dll");
        using var module = ModuleDefinition.CreateModule("ParticleFixture" + Guid.NewGuid().ToString("N"), ModuleKind.Dll);
        var spriteBatch = new TypeDefinition("Microsoft.Xna.Framework.Graphics", "SpriteBatch", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(spriteBatch);
        AddDefaultConstructor(module, spriteBatch);
        // This fixture models the renderer's call shape with a concrete base type so the written
        // module can execute under the test runtime without depending on a generated interface map.
        var particle = new TypeDefinition("Terraria.Graphics.Renderers", "IParticle", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(particle);
        AddDefaultConstructor(module, particle);
        var removed = new MethodDefinition("get_ShouldBeRemovedFromRenderer", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig, module.TypeSystem.Boolean);
        removed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        removed.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var drawParticle = new MethodDefinition("Draw", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig, module.TypeSystem.Void);
        drawParticle.Parameters.Add(new ParameterDefinition(spriteBatch));
        drawParticle.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        particle.Methods.Add(removed);
        particle.Methods.Add(drawParticle);
        var renderer = new TypeDefinition("Terraria.Graphics.Renderers", "ParticleRenderer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(renderer);
        AddDefaultConstructor(module, renderer);
        var listDefinition = new TypeDefinition("System.Collections.Generic", "List`1", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var listItemType = new GenericParameter("T", listDefinition);
        listDefinition.GenericParameters.Add(listItemType);
        module.Types.Add(listDefinition);
        AddDefaultConstructor(module, listDefinition);
        var storedItem = new FieldDefinition("stored", FieldAttributes.Private, listItemType);
        listDefinition.Fields.Add(storedItem);
        var add = new MethodDefinition("Add", MethodAttributes.Public, module.TypeSystem.Void);
        add.Parameters.Add(new ParameterDefinition(listItemType));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, storedItem));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        listDefinition.Methods.Add(add);
        var getItemDefinition = new MethodDefinition("get_Item", MethodAttributes.Public, listItemType);
        getItemDefinition.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        getItemDefinition.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getItemDefinition.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, storedItem));
        getItemDefinition.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        listDefinition.Methods.Add(getItemDefinition);
        var listType = new GenericInstanceType(listDefinition);
        listType.GenericArguments.Add(particle);
        var particles = new FieldDefinition("particles", FieldAttributes.Private, listType);
        renderer.Fields.Add(particles);
        var testParticle = new TypeDefinition("Fixture", "TestParticle", TypeAttributes.Public | TypeAttributes.Class, particle);
        module.Types.Add(testParticle);
        AddDefaultConstructor(module, testParticle, particle.Methods.Single(method => method.IsConstructor));
        var drawCount = new FieldDefinition("DrawCount", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
        testParticle.Fields.Add(drawCount);
        var testRemoved = new MethodDefinition("get_ShouldBeRemovedFromRenderer", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.Final, module.TypeSystem.Boolean);
        testRemoved.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        testRemoved.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        testRemoved.Overrides.Add(removed);
        testParticle.Methods.Add(testRemoved);
        var testDraw = new MethodDefinition("Draw", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.Final, module.TypeSystem.Void);
        testDraw.Parameters.Add(new ParameterDefinition(spriteBatch));
        testDraw.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, drawCount));
        testDraw.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        testDraw.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
        testDraw.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, drawCount));
        testDraw.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        testDraw.Overrides.Add(drawParticle);
        testParticle.Methods.Add(testDraw);
        var listGetItem = new MethodReference("get_Item", listItemType, listType) { HasThis = true };
        listGetItem.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        var draw = new MethodDefinition("Draw", MethodAttributes.Public, module.TypeSystem.Void);
        draw.Parameters.Add(new ParameterDefinition(spriteBatch));
        renderer.Methods.Add(draw);
        VariableDefinition index = new VariableDefinition(module.TypeSystem.Int32);
        draw.Body.Variables.Add(index);
        ILProcessor il = draw.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldfld, particles));
        il.Append(Instruction.Create(OpCodes.Ldloc_0));
        il.Append(Instruction.Create(OpCodes.Callvirt, listGetItem));
        il.Append(Instruction.Create(OpCodes.Callvirt, removed));
        Instruction loopContinue = Instruction.Create(OpCodes.Ldloc_0);
        il.Append(Instruction.Create(OpCodes.Brtrue, loopContinue));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldfld, particles));
        il.Append(Instruction.Create(OpCodes.Ldloc_0));
        il.Append(Instruction.Create(OpCodes.Callvirt, listGetItem));
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Callvirt, drawParticle));
        il.Append(loopContinue);
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Add));
        il.Append(Instruction.Create(OpCodes.Stloc_0));
        il.Append(Instruction.Create(OpCodes.Ret));
        module.Write(path);
        return path;
    }

    private static MethodReference CreateBridgeMethod(ModuleDefinition module, string name, TypeReference returnType, params TypeReference[] parameters)
    {
        var facade = new AssemblyNameReference(BridgeAbiContractCatalog.FacadeAssemblyName, new Version(1, 0));
        module.AssemblyReferences.Add(facade);
        var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", module, facade);
        var method = new MethodReference(name, returnType, bridgeType) { HasThis = false };
        for (int index = 0; index < parameters.Length; index++)
        {
            method.Parameters.Add(new ParameterDefinition(parameters[index]));
        }

        return method;
    }

    private static MethodReference CreateLocalParticleGate(ModuleDefinition module, TypeReference renderer, TypeReference particle)
    {
        var bridge = new TypeDefinition("AlacrityTerraria", "PluginUiRuntime", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(bridge);
        var allow = new FieldDefinition("Allow", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Boolean);
        bridge.Fields.Add(allow);
        var gate = new MethodDefinition("ShouldDrawWorldParticle", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
        gate.Parameters.Add(new ParameterDefinition(renderer));
        gate.Parameters.Add(new ParameterDefinition(particle));
        gate.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, allow));
        gate.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        bridge.Methods.Add(gate);
        return gate;
    }

    private static void AddDefaultConstructor(ModuleDefinition module, TypeDefinition type, MethodReference? baseConstructor = null)
    {
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, baseConstructor ?? module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(constructor);
    }

    private static int CountCalls(MethodDefinition method, string name)
    {
        return method.Body.Instructions.Count(instruction => instruction.Operand is MethodReference called && called.Name == name);
    }
}
