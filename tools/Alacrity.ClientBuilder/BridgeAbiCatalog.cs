using Alacrity.PluginSdk;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BridgeAbiCatalog
{
    private const string BridgeTypeName = BridgeAbiContractCatalog.FacadeTypeName;

    internal static string ValidateRuntimeFacade(string runtimeDirectory, SupportedTerrariaBuild source)
    {
        var facadePath = Path.Combine(runtimeDirectory, "bin", "Alacrity.PluginUiRuntime.dll");
        var coreBridgePath = Path.Combine(runtimeDirectory, "bin", "Alacrity.PluginUiCoreBridge.dll");
        if (!File.Exists(facadePath))
        {
            throw new ClientBuildException("Runtime stage is missing bin\\Alacrity.PluginUiRuntime.dll.");
        }
        if (!File.Exists(coreBridgePath))
        {
            throw new ClientBuildException("Runtime stage is missing bin\\Alacrity.PluginUiCoreBridge.dll.");
        }

        using var facade = ModuleDefinition.ReadModule(facadePath);
        if (!string.Equals(facade.Assembly.Name.Name, BridgeAbiContractCatalog.FacadeAssemblyName, StringComparison.Ordinal))
        {
            throw new ClientBuildException("Runtime stage facade assembly identity was " + facade.Assembly.Name.Name + ", expected " + BridgeAbiContractCatalog.FacadeAssemblyName + ".");
        }
        var facadeType = CecilPatchPrimitives.RequireType(facade, BridgeTypeName);
        ValidateRequiredFacadeMethods(facadeType);
        using var coreBridge = ModuleDefinition.ReadModule(coreBridgePath);
        var coreBridgeType = CecilPatchPrimitives.RequireType(coreBridge, BridgeTypeName);
        var handshake = CecilPatchPrimitives.RequireMethod(coreBridgeType, "GetBridgeHandshake", "System.String");
        if (!handshake.IsStatic || !handshake.HasBody)
        {
            throw new ClientBuildException("The bridge handshake ABI must be a static method with a body.");
        }

        string? value = null;
        for (var index = 0; index < handshake.Body.Instructions.Count; index++)
        {
            var instruction = handshake.Body.Instructions[index];
            if (instruction.OpCode == OpCodes.Ldstr)
            {
                value = instruction.Operand as string;
                break;
            }
        }

        var diagnostic = "the method did not return a literal handshake.";
        if (value == null || !BridgeCompatibilityDescriptor.TryParse(value, out var actual, out diagnostic))
        {
            throw new ClientBuildException("Runtime bridge handshake is invalid: " + (diagnostic ?? "the method did not return a literal handshake.") + ".");
        }

        var expected = new BridgeCompatibilityDescriptor(
            AlacrityCompatibility.PluginSdk,
            AlacrityCompatibility.Host,
            AlacrityCompatibility.BridgeAbi,
            source.Version);
        if (!actual.TryValidateAgainst(expected, out diagnostic))
        {
            throw new ClientBuildException(diagnostic);
        }

        return value;
    }

    private static void ValidateRequiredFacadeMethods(TypeDefinition facadeType)
    {
        var requiredNames = new HashSet<string>(StringComparer.Ordinal);
        var definitions = PermanentPatchCatalog.GetDefinitions();
        for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
        {
            var operations = definitions[definitionIndex].Operations;
            for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                var bridgeMethods = operations[operationIndex].BridgeMethods;
                for (var methodIndex = 0; methodIndex < bridgeMethods.Count; methodIndex++)
                {
                    requiredNames.Add(bridgeMethods[methodIndex]);
                }
            }
        }

        foreach (var requiredName in requiredNames)
        {
            var contract = BridgeAbiContractCatalog.Require(requiredName);
            MethodDefinition? match = null;
            for (var methodIndex = 0; methodIndex < facadeType.Methods.Count; methodIndex++)
            {
                var candidate = facadeType.Methods[methodIndex];
                if (!candidate.IsPublic || !candidate.IsStatic || !string.Equals(candidate.Name, requiredName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (candidate.GenericParameters.Count != 0 || !MatchesContract(candidate, contract))
                {
                    throw new ClientBuildException("Staged ABI facade method does not match the authoritative contract " + Describe(contract) + ": " + candidate.FullName + ".");
                }

                if (match != null)
                {
                    throw new ClientBuildException("Staged ABI facade contains an ambiguous public static bridge method named " + requiredName + ".");
                }

                match = candidate;
            }

            if (match == null)
            {
                throw new ClientBuildException("Staged ABI facade is missing the required exact bridge contract " + Describe(contract) + ". Rebuild and stage all Alacrity assemblies together.");
            }
        }
    }

    internal static List<string> ValidatePatchedExecutable(ModuleDefinition module, string runtimeDirectory)
    {
        var facadePath = Path.Combine(runtimeDirectory, "bin", "Alacrity.PluginUiRuntime.dll");
        using var facade = ModuleDefinition.ReadModule(facadePath);
        var bridgeType = CecilPatchPrimitives.RequireType(facade, BridgeTypeName);
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in module.Types)
        {
            CollectBridgeCalls(type, bridgeType, methods);
        }

        if (methods.Count == 0)
        {
            throw new ClientBuildException("Patched executable contains no calls to the staged PluginUiRuntime ABI facade.");
        }

        return methods.OrderBy(value => value, StringComparer.Ordinal).ToList();
    }

    private static void CollectBridgeCalls(TypeDefinition type, TypeDefinition bridgeType, ISet<string> methods)
    {
        for (var methodIndex = 0; methodIndex < type.Methods.Count; methodIndex++)
        {
            var method = type.Methods[methodIndex];
            if (!method.HasBody)
            {
                continue;
            }

            for (var instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].Operand is not MethodReference reference ||
                    !string.Equals(reference.DeclaringType.FullName, BridgeTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!HasExactFacadeMethod(bridgeType, reference))
                {
                    throw new ClientBuildException("Patched method " + method.FullName + " references an ABI member absent from the staged facade: " + reference.FullName + ".");
                }
                var contract = BridgeAbiContractCatalog.Require(reference.Name);
                if (!MatchesContract(reference, contract))
                {
                    throw new ClientBuildException("Patched method " + method.FullName + " references an ABI signature that differs from the authoritative contract " + Describe(contract) + ".");
                }

                methods.Add(reference.FullName);
            }
        }

        for (var nestedIndex = 0; nestedIndex < type.NestedTypes.Count; nestedIndex++)
        {
            CollectBridgeCalls(type.NestedTypes[nestedIndex], bridgeType, methods);
        }
    }

    private static bool HasExactFacadeMethod(TypeDefinition bridgeType, MethodReference reference)
    {
        for (var index = 0; index < bridgeType.Methods.Count; index++)
        {
            var candidate = bridgeType.Methods[index];
            if (!candidate.IsStatic || !string.Equals(candidate.Name, reference.Name, StringComparison.Ordinal) ||
                candidate.GenericParameters.Count != reference.GenericParameters.Count ||
                !string.Equals(candidate.ReturnType.FullName, reference.ReturnType.FullName, StringComparison.Ordinal) ||
                candidate.Parameters.Count != reference.Parameters.Count)
            {
                continue;
            }

            var matches = true;
            for (var parameterIndex = 0; parameterIndex < candidate.Parameters.Count; parameterIndex++)
            {
                if (!string.Equals(candidate.Parameters[parameterIndex].ParameterType.FullName, reference.Parameters[parameterIndex].ParameterType.FullName, StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesContract(MethodReference method, BridgeAbiContract contract)
    {
        if (!string.Equals(method.ReturnType.FullName, contract.ReturnType, StringComparison.Ordinal) || method.Parameters.Count != contract.ParameterTypes.Count)
        {
            return false;
        }

        for (var index = 0; index < method.Parameters.Count; index++)
        {
            if (!string.Equals(method.Parameters[index].ParameterType.FullName, contract.ParameterTypes[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(BridgeAbiContract contract)
    {
        return BridgeAbiContractCatalog.FacadeTypeName + "::" + contract.Name + "(" + string.Join(", ", contract.ParameterTypes) + ") -> " + contract.ReturnType;
    }
}
