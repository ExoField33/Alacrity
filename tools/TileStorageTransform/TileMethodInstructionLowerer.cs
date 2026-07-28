using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Makes verified Tile instance methods explicit receiver methods. This is the
/// required calling convention when Tile becomes a value handle: callers retain
/// their receiver on the evaluation stack, while the rewritten method receives
/// it as parameter zero.
/// </summary>
public static class TileMethodInstructionLowerer
{
    public static int Rewrite(TypeDefinition tileType, IEnumerable<MethodDefinition> callSites)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(callSites);

        var convertedMethods = new HashSet<MethodDefinition>();
        foreach (MethodDefinition method in tileType.Methods)
        {
            if (method.IsConstructor || !TileMethodLoweringCatalog.TryGet(method.FullName, out TileMethodLoweringDescriptor? descriptor) || descriptor == null)
                continue;
            if (descriptor.Kind != TileMethodLoweringKind.InstanceMethodBecomesHandleMethod)
                continue;
            if (method.IsStatic)
                continue;

            method.Parameters.Insert(0, new ParameterDefinition("tile", ParameterAttributes.None, tileType));
            method.Attributes |= MethodAttributes.Static;
            convertedMethods.Add(method);
        }

        int rewrittenCalls = 0;
        foreach (MethodDefinition method in tileType.Methods.Concat(callSites).Where(method => method.HasBody).Distinct())
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) || instruction.Operand is not MethodReference called || called.DeclaringType.FullName != tileType.FullName)
                    continue;
                if (!TileMethodLoweringCatalog.TryGet(called.FullName, out TileMethodLoweringDescriptor? descriptor) || descriptor == null)
                    throw new InvalidOperationException($"No compact method lowering exists for {called.FullName}.");
                if (descriptor.Kind != TileMethodLoweringKind.InstanceMethodBecomesHandleMethod)
                    continue;
                MethodDefinition target = called.Resolve() ?? throw new InvalidOperationException($"Could not resolve Tile method {called.FullName}.");
                if (!convertedMethods.Contains(target))
                    throw new InvalidOperationException($"The Tile method {called.FullName} was referenced as an instance method but was not converted.");
                if (!target.IsStatic || target.Parameters.Count == 0 || target.Parameters[0].ParameterType.FullName != tileType.FullName)
                    throw new InvalidOperationException($"The rewritten Tile method {target.FullName} does not have an explicit receiver.");
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = target;
                rewrittenCalls++;
            }
        }

        return rewrittenCalls;
    }
}
