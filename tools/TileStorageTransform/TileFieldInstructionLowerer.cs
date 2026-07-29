using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Replaces direct Terraria.Tile field instructions with static compact-runtime
/// calls. The stack contracts are identical: getter(Tile) -> value and
/// setter(Tile, value) -> void.
/// </summary>
public static class TileFieldInstructionLowerer
{
    public static int Rewrite(TypeDefinition tileType, TypeDefinition runtimeType, IEnumerable<MethodDefinition> methods)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(runtimeType);
        ArgumentNullException.ThrowIfNull(methods);

        var runtimeMethods = runtimeType.Methods
            .Where(method => method.IsStatic)
            .ToDictionary(method => method.Name, StringComparer.Ordinal);
        int rewritten = 0;
        foreach (MethodDefinition method in methods.Where(method => method.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not FieldReference field || field.DeclaringType.FullName != tileType.FullName)
                    continue;
                if (instruction.OpCode != OpCodes.Ldfld && instruction.OpCode != OpCodes.Stfld)
                    throw new InvalidOperationException($"Unsupported Tile field opcode {instruction.OpCode} at {method.FullName}.");
                if (!TileFieldLoweringCatalog.TryGet(field.FullName, out TileFieldLoweringDescriptor? descriptor) || descriptor == null)
                    throw new InvalidOperationException($"No compact field lowering exists for {field.FullName}.");

                string runtimeMethodName = instruction.OpCode == OpCodes.Ldfld ? descriptor.GetterName : descriptor.SetterName;
                if (!runtimeMethods.TryGetValue(runtimeMethodName, out MethodDefinition? runtimeMethod))
                    throw new InvalidOperationException($"The compact runtime is missing {runtimeMethodName} for {field.FullName}.");
                ValidateRuntimeSignature(tileType, field, runtimeMethod, instruction.OpCode == OpCodes.Stfld);
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = runtimeMethod;
                rewritten++;
            }
        }

        NormalizeInModuleMethodReferences(tileType.Module);
        return rewritten;
    }

    private static void NormalizeInModuleMethodReferences(ModuleDefinition module)
    {
        foreach (TypeDefinition type in Flatten(module.Types))
        {
            foreach (MethodDefinition method in type.Methods.Where(candidate => candidate.HasBody))
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference reference || reference is MethodDefinition)
                        continue;

                    MethodDefinition? definition;
                    try { definition = reference.Resolve(); }
                    catch (AssemblyResolutionException) { continue; }
                    if (definition?.Module == module)
                        instruction.Operand = definition;
                }
            }
        }
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

    private static void ValidateRuntimeSignature(TypeDefinition tileType, FieldReference field, MethodDefinition method, bool isSetter)
    {
        int expectedParameters = isSetter ? 2 : 1;
        if (!method.IsStatic || method.Parameters.Count != expectedParameters || method.Parameters[0].ParameterType.FullName != tileType.FullName)
            throw new InvalidOperationException($"The compact runtime method {method.FullName} has an incompatible Tile receiver signature.");
        if (isSetter)
        {
            if (method.ReturnType.MetadataType != MetadataType.Void || method.Parameters[1].ParameterType.FullName != field.FieldType.FullName)
                throw new InvalidOperationException($"The compact runtime setter {method.FullName} does not match {field.FullName}.");
        }
        else if (method.ReturnType.FullName != field.FieldType.FullName)
        {
            throw new InvalidOperationException($"The compact runtime getter {method.FullName} does not match {field.FullName}.");
        }
    }
}
