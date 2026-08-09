using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

public sealed class RuntimeStageAndAbiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Alacrity.ClientBuilder.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RuntimeStageAndFacadeAreValidatedAsOneCoherentSet()
    {
        CreateRuntimeStage("2|2|2|1.4.5.6");

        var stage = RuntimeStage.Load(directory);
        var source = new SupportedTerrariaBuild("fixture", "1.4.5.6", "hash", PermanentPatchCatalog.Identity);

        Assert.NotEmpty(stage.Files);
        Assert.Equal("2|2|2|1.4.5.6", BridgeAbiCatalog.ValidateRuntimeFacade(directory, source));
    }

    [Fact]
    public void PermanentPatchCatalogHasStableUniqueOperationContracts()
    {
        PermanentPatchCatalog.ValidateCatalog();
        var definitions = PermanentPatchCatalog.GetDefinitions();

        Assert.Equal(
            new[]
            {
                "patch.runtime.startup-and-menu",
                "patch.runtime.input-and-keybinds",
                "patch.runtime.rendering-and-combat",
                "patch.runtime.visual-effects",
                "patch.runtime.chat-input-and-commands",
                "patch.runtime.chat-display-and-interaction"
            },
            definitions.Select(definition => definition.Id));
        Assert.Equal(
            new[]
            {
                "runtime.startup-and-menu",
                "runtime.input-and-keybinds",
                "runtime.rendering-and-combat",
                "runtime.visual-effects",
                "runtime.chat-input-and-commands",
                "runtime.chat-display-and-interaction"
            },
            definitions.SelectMany(definition => definition.Operations).Select(operation => operation.Id));
        Assert.All(definitions.SelectMany(definition => definition.Operations), operation =>
        {
            Assert.NotEmpty(operation.BridgeMethods);
            Assert.NotEmpty(operation.Targets);
            Assert.All(operation.Targets, target =>
            {
                Assert.False(string.IsNullOrWhiteSpace(target.MemberSignature));
                Assert.False(string.IsNullOrWhiteSpace(target.Anchor));
                Assert.False(string.IsNullOrWhiteSpace(target.Injection));
                Assert.False(string.IsNullOrWhiteSpace(target.Precondition));
                Assert.False(string.IsNullOrWhiteSpace(target.Postcondition));
            });
        });
    }

    [Fact]
    public void RuntimeStageRejectsAnUndeclaredOrChangedFile()
    {
        CreateRuntimeStage("2|2|2|1.4.5.6");
        File.AppendAllText(Path.Combine(directory, "Alacrity.Core.dll"), "changed");

        var exception = Assert.Throws<ClientBuildException>(() => RuntimeStage.Load(directory));

        Assert.Contains("does not verify", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeStageRejectsDifferentRootAndBinCopiesOfTheSameAssembly()
    {
        CreateRuntimeStage("2|2|2|1.4.5.6");
        File.WriteAllText(Path.Combine(directory, "bin", "Alacrity.Core.dll"), "different core");
        WriteStageManifest();

        var exception = Assert.Throws<ClientBuildException>(() => RuntimeStage.Load(directory));
        Assert.Contains("different root and bin copies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeFacadeRejectsAnIncompatibleHandshake()
    {
        CreateRuntimeStage("2|2|1|1.4.5.6");
        var source = new SupportedTerrariaBuild("fixture", "1.4.5.6", "hash", PermanentPatchCatalog.Identity);

        var exception = Assert.Throws<ClientBuildException>(() => BridgeAbiCatalog.ValidateRuntimeFacade(directory, source));

        Assert.Contains("Bridge ABI", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeFacadeRejectsAChangedPatchReferencedSignature()
    {
        CreateRuntimeStage("2|2|2|1.4.5.6");
        var facadePath = Path.Combine(directory, "bin", "Alacrity.PluginUiRuntime.dll");
        using (var facade = ModuleDefinition.ReadModule(facadePath, new ReaderParameters { ReadWrite = true }))
        {
            var type = CecilPatchPrimitives.RequireType(facade, "AlacrityTerraria.PluginUiRuntime");
            var invalidOverload = new MethodDefinition("HandleInput", MethodAttributes.Public | MethodAttributes.Static, facade.TypeSystem.Void);
            invalidOverload.Parameters.Add(new ParameterDefinition(facade.TypeSystem.String));
            invalidOverload.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(invalidOverload);
            facade.Write();
        }
        WriteStageManifest();

        var source = new SupportedTerrariaBuild("fixture", "1.4.5.6", "hash", PermanentPatchCatalog.Identity);
        var exception = Assert.Throws<ClientBuildException>(() => BridgeAbiCatalog.ValidateRuntimeFacade(directory, source));

        Assert.Contains("authoritative contract", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchedExecutableRequiresAnExactFacadeMember()
    {
        CreateRuntimeStage("2|2|2|1.4.5.6");
        var executablePath = Path.Combine(directory, "Alacrity.exe");
        using (var module = ModuleDefinition.CreateModule("Fixture", ModuleKind.Console))
        {
            var type = new TypeDefinition("Fixture", "Entry", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);
            var method = new MethodDefinition("Call", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            type.Methods.Add(method);
            var facadeAssembly = new AssemblyNameReference(BridgeAbiContractCatalog.FacadeAssemblyName, new Version(1, 0));
            module.AssemblyReferences.Add(facadeAssembly);
            var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", module, facadeAssembly);
            var bridgeCall = new MethodReference("BootstrapPluginRuntime", module.TypeSystem.Void, bridgeType)
            {
                HasThis = false
            };
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, bridgeCall));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            module.Write(executablePath);
        }

        using var patched = ModuleDefinition.ReadModule(executablePath);
        var methods = BridgeAbiCatalog.ValidatePatchedExecutable(patched, directory);

        Assert.Single(methods);
        Assert.Contains("BootstrapPluginRuntime", methods[0], StringComparison.Ordinal);
    }

    [Fact]
    public void PatchedExecutableRejectsAFacadeLookalikeFromTheWrongAssembly()
    {
        CreateRuntimeStage("2|2|2|1.4.5.6");
        var executablePath = Path.Combine(directory, "wrong-scope.exe");
        using (var module = ModuleDefinition.CreateModule("Fixture", ModuleKind.Console))
        {
            var type = new TypeDefinition("Fixture", "Entry", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);
            var method = new MethodDefinition("Call", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            type.Methods.Add(method);
            var wrongAssembly = new AssemblyNameReference("Wrong.PluginUiRuntime", new Version(1, 0));
            module.AssemblyReferences.Add(wrongAssembly);
            var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", module, wrongAssembly);
            var bridgeCall = new MethodReference("BootstrapPluginRuntime", module.TypeSystem.Void, bridgeType)
            {
                HasThis = false
            };
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, bridgeCall));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            module.Write(executablePath);
        }

        using var patched = ModuleDefinition.ReadModule(executablePath);
        var exception = Assert.Throws<ClientBuildException>(() => BridgeAbiCatalog.ValidatePatchedExecutable(patched, directory));
        Assert.Contains("Wrong.PluginUiRuntime", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return")]
    [InlineData("parameter")]
    [InlineData("generic")]
    [InlineData("instance")]
    [InlineData("missing")]
    public void PatchedExecutableRejectsEveryChangedAbiSignatureShape(string variation)
    {
        CreateRuntimeStage("2|2|2|1.4.5.6");
        string executablePath = Path.Combine(directory, variation + ".exe");
        using (var module = ModuleDefinition.CreateModule("Fixture", ModuleKind.Console))
        {
            var type = new TypeDefinition("Fixture", "Entry", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);
            var method = new MethodDefinition("Call", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            type.Methods.Add(method);
            var facadeAssembly = new AssemblyNameReference(BridgeAbiContractCatalog.FacadeAssemblyName, new Version(1, 0));
            module.AssemblyReferences.Add(facadeAssembly);
            var bridgeType = new TypeReference("AlacrityTerraria", "PluginUiRuntime", module, facadeAssembly);
            var bridgeCall = new MethodReference(
                variation == "missing" ? "NotAnAbiMethod" : "BootstrapPluginRuntime",
                variation == "return" ? module.TypeSystem.String : module.TypeSystem.Void,
                bridgeType)
            {
                HasThis = variation == "instance"
            };
            if (variation == "parameter")
            {
                bridgeCall.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            }
            if (variation == "generic")
            {
                bridgeCall.GenericParameters.Add(new GenericParameter("T", bridgeCall));
            }
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, bridgeCall));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            module.Write(executablePath);
        }

        using var patched = ModuleDefinition.ReadModule(executablePath);
        Assert.Throws<ClientBuildException>(() => BridgeAbiCatalog.ValidatePatchedExecutable(patched, directory));
    }

    [Fact]
    public void StaleOutputCleanupRemovesOnlyPriorPipelineOwnedFiles()
    {
        Directory.CreateDirectory(Path.Combine(directory, "plugins", "removed-plugin"));
        Directory.CreateDirectory(Path.Combine(directory, "bin"));
        var stalePlugin = Path.Combine(directory, "plugins", "removed-plugin", "Plugin.dll");
        var staleBridge = Path.Combine(directory, "bin", "Alacrity.OldBridge.dll");
        var unrelated = Path.Combine(directory, "user-extension.dll");
        File.WriteAllText(stalePlugin, "stale");
        File.WriteAllText(staleBridge, "stale");
        File.WriteAllText(unrelated, "user");
        var previous = new ClientBuildManifest
        {
            RuntimeFiles = new List<ClientBuildFile>
            {
                new ClientBuildFile { Path = "plugins/removed-plugin/Plugin.dll", Sha256 = "old" },
                new ClientBuildFile { Path = "bin/Alacrity.OldBridge.dll", Sha256 = "old" },
                new ClientBuildFile { Path = "user-extension.dll", Sha256 = "old" }
            }
        };
        File.WriteAllText(Path.Combine(directory, "alacrity-client-manifest.json"), System.Text.Json.JsonSerializer.Serialize(previous));

        ClientBuildPipeline.RemovePreviouslyOwnedFiles(directory, new ClientBuildManifest());

        Assert.False(File.Exists(stalePlugin));
        Assert.False(File.Exists(staleBridge));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void DeploymentReadsPriorOwnershipBeforeItPublishesTheNewManifest()
    {
        string output = Path.Combine(directory, "client");
        string temporary = Path.Combine(directory, "temporary");
        Directory.CreateDirectory(Path.Combine(output, "bin"));
        Directory.CreateDirectory(Path.Combine(output, "plugins", "removed-plugin"));
        Directory.CreateDirectory(Path.Combine(temporary, "bin"));
        File.WriteAllText(Path.Combine(output, "bin", "OldBridge.dll"), "old bridge");
        File.WriteAllText(Path.Combine(output, "plugins", "removed-plugin", "RemovedPlugin.dll"), "old plugin");
        File.WriteAllText(Path.Combine(output, "user-extension.dll"), "user file");
        File.WriteAllText(Path.Combine(temporary, "Alacrity.exe"), "new executable");
        File.WriteAllText(Path.Combine(temporary, "bin", "NewBridge.dll"), "new bridge");

        var previous = new ClientBuildManifest
        {
            RuntimeFiles = new List<ClientBuildFile>
            {
                new ClientBuildFile { Path = "bin/OldBridge.dll", Sha256 = "old" },
                new ClientBuildFile { Path = "plugins/removed-plugin/RemovedPlugin.dll", Sha256 = "old" }
            }
        };
        File.WriteAllText(Path.Combine(output, "alacrity-client-manifest.json"), System.Text.Json.JsonSerializer.Serialize(previous));
        var current = new ClientBuildManifest
        {
            RuntimeFiles = new List<ClientBuildFile>
            {
                new ClientBuildFile { Path = "bin/NewBridge.dll", Sha256 = "new" }
            }
        };

        ClientBuildPipeline.PublishDeployment(temporary, output, current);

        Assert.False(File.Exists(Path.Combine(output, "bin", "OldBridge.dll")));
        Assert.False(File.Exists(Path.Combine(output, "plugins", "removed-plugin", "RemovedPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(output, "user-extension.dll")));
        Assert.True(File.Exists(Path.Combine(output, "Alacrity.exe")));
        Assert.True(File.Exists(Path.Combine(output, "bin", "NewBridge.dll")));
        var published = System.Text.Json.JsonSerializer.Deserialize<ClientBuildManifest>(File.ReadAllText(Path.Combine(output, "alacrity-client-manifest.json")));
        Assert.NotNull(published);
        Assert.Single(published!.RuntimeFiles);
        Assert.Equal("bin/NewBridge.dll", published.RuntimeFiles[0].Path);
    }

    [Fact]
    public void FailedDeploymentKeepsThePreviousOwnershipManifest()
    {
        string output = Path.Combine(directory, "client");
        string temporary = Path.Combine(directory, "temporary");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(temporary);
        var previous = new ClientBuildManifest
        {
            RuntimeFiles = new List<ClientBuildFile>
            {
                new ClientBuildFile { Path = "bin/OldBridge.dll", Sha256 = "old" }
            }
        };
        string manifestPath = Path.Combine(output, "alacrity-client-manifest.json");
        string previousJson = System.Text.Json.JsonSerializer.Serialize(previous);
        File.WriteAllText(manifestPath, previousJson);

        var invalid = new ClientBuildManifest
        {
            RuntimeFiles = new List<ClientBuildFile>
            {
                new ClientBuildFile { Path = "../outside.dll", Sha256 = "bad" }
            }
        };

        Assert.Throws<ClientBuildException>(() => ClientBuildPipeline.PublishDeployment(temporary, output, invalid));
        Assert.Equal(previousJson, File.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("../../outside.dll")]
    [InlineData("C:\\outside.dll")]
    [InlineData("\\\\server\\share\\outside.dll")]
    public void ManifestPathsCannotEscapeTheirStageOrDeploymentRoot(string path)
    {
        Directory.CreateDirectory(Path.Combine(directory, "stage"));
        File.WriteAllText(Path.Combine(directory, "stage", "runtime-manifest.txt"), "Configuration=Release\n" + path + "|hash");

        Assert.Throws<ClientBuildException>(() => RuntimeStage.Load(Path.Combine(directory, "stage")));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void CreateRuntimeStage(string handshake)
    {
        Directory.CreateDirectory(Path.Combine(directory, "bin"));
        File.WriteAllText(Path.Combine(directory, "VERSION"), "2");
        File.WriteAllText(Path.Combine(directory, "Alacrity.PluginSdk.dll"), "sdk");
        File.WriteAllText(Path.Combine(directory, "Alacrity.Core.dll"), "core");
        CreateCoreBridge(Path.Combine(directory, "bin", "Alacrity.PluginUiCoreBridge.dll"), handshake);
        CreateFacade(Path.Combine(directory, "bin", "Alacrity.PluginUiRuntime.dll"));

        WriteStageManifest();
    }

    private void WriteStageManifest()
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("runtime-manifest.txt", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') + "|" + SupportedTerrariaBuildCatalog.ComputeSha256(path));
        File.WriteAllLines(Path.Combine(directory, "runtime-manifest.txt"), new[] { "Configuration=Release" }.Concat(files));
    }

    private static void CreateFacade(string path)
    {
        using var module = ModuleDefinition.CreateModule("Alacrity.PluginUiRuntime", ModuleKind.Dll);
        var type = new TypeDefinition("AlacrityTerraria", "PluginUiRuntime", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        foreach (var contract in BridgeAbiContractCatalog.GetContracts())
        {
            var method = new MethodDefinition(contract.Name, MethodAttributes.Public | MethodAttributes.Static, CreateTypeReference(module, contract.ReturnType));
            for (var parameterIndex = 0; parameterIndex < contract.ParameterTypes.Count; parameterIndex++)
            {
                method.Parameters.Add(new ParameterDefinition(CreateTypeReference(module, contract.ParameterTypes[parameterIndex])));
            }
            type.Methods.Add(method);
            AddDefaultReturn(method);
        }

        module.Write(path);
    }

    private static TypeReference CreateTypeReference(ModuleDefinition module, string fullName)
    {
        return fullName switch
        {
            "System.Void" => module.TypeSystem.Void,
            "System.Boolean" => module.TypeSystem.Boolean,
            "System.Byte" => module.TypeSystem.Byte,
            "System.Int32" => module.TypeSystem.Int32,
            "System.Single" => module.TypeSystem.Single,
            "System.String" => module.TypeSystem.String,
            "System.Object" => module.TypeSystem.Object,
            _ => new TypeReference(fullName.Substring(0, fullName.LastIndexOf('.')), fullName.Substring(fullName.LastIndexOf('.') + 1), module, module.TypeSystem.CoreLibrary)
        };
    }

    private static void AddDefaultReturn(MethodDefinition method)
    {
        if (method.ReturnType.FullName == "System.Void")
        {
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return;
        }

        if (method.ReturnType.FullName == "System.Single")
        {
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 0f));
        }
        else if (method.ReturnType.FullName == "System.Int32" || method.ReturnType.FullName == "System.Boolean" || method.ReturnType.FullName == "System.Byte")
        {
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        }
        else
        {
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        }

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }

    private static void CreateCoreBridge(string path, string handshake)
    {
        using var module = ModuleDefinition.CreateModule("Alacrity.PluginUiCoreBridge", ModuleKind.Dll);
        var type = new TypeDefinition("AlacrityTerraria", "PluginUiRuntime", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition("GetBridgeHandshake", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
        type.Methods.Add(method);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, handshake));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        module.Write(path);
    }
}
