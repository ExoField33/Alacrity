using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Replaces verified Tile constructors with compact-handle factory calls. The
/// factory must preserve the original stack shape: constructor arguments are
/// consumed and one handle is pushed. Unknown constructors are rejected.
/// </summary>
public static class TileHandleConstructionLowerer
{
    public static int Rewrite(TypeDefinition tileType, TypeDefinition runtimeType, IEnumerable<MethodDefinition> methods)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(runtimeType);
        ArgumentNullException.ThrowIfNull(methods);

        MethodDefinition create = RequireFactory(runtimeType, "Create", tileType, 0);
        MethodDefinition createCopy = RequireFactory(runtimeType, "CreateCopy", tileType, 1);
        int rewritten = 0;
        foreach (MethodDefinition method in methods.Where(method => method.HasBody).Distinct())
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Newobj || instruction.Operand is not MethodReference constructor || constructor.DeclaringType.FullName != tileType.FullName)
                    continue;
                if (!string.Equals(constructor.Name, ".ctor", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Tile allocation at {method.FullName}@IL_{instruction.Offset:X4} does not target a constructor.");

                MethodDefinition target = constructor.Parameters.Count switch
                {
                    0 => create,
                    1 when constructor.Parameters[0].ParameterType.FullName == tileType.FullName => createCopy,
                    _ => throw new InvalidOperationException($"Unsupported Tile constructor {constructor.FullName} at {method.FullName}@IL_{instruction.Offset:X4}.")
                };
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = target;
                rewritten++;
            }
        }

        return rewritten;
    }

    private static MethodDefinition RequireFactory(TypeDefinition runtimeType, string name, TypeDefinition tileType, int parameterCount)
    {
        MethodDefinition method = runtimeType.Methods.SingleOrDefault(candidate =>
            candidate.IsStatic && candidate.Name == name && candidate.Parameters.Count == parameterCount &&
            candidate.ReturnType.FullName != tileType.FullName)
            ?? throw new InvalidOperationException($"The compact runtime does not expose a compatible {name} Tile factory.");
        if (parameterCount == 1 && method.Parameters[0].ParameterType.FullName != tileType.FullName)
            throw new InvalidOperationException($"The compact runtime copy factory {method.FullName} does not accept the expected Tile source.");
        return method;
    }
}

/// <summary>
/// Rewrites direct null branches and equality checks whose Tile producer was
/// already lowered to a compact handle. The caller supplies exact producer
/// instructions from the version-locked manifest; broad pattern matching is
/// deliberately avoided.
/// </summary>
public static class TileHandleNullLowerer
{
    public static int Rewrite(TypeDefinition runtimeType, IEnumerable<TileNullCheckRewrite> rewrites)
    {
        ArgumentNullException.ThrowIfNull(runtimeType);
        ArgumentNullException.ThrowIfNull(rewrites);
        MethodDefinition isNull = runtimeType.Methods.SingleOrDefault(method =>
            method.IsStatic && method.Name == "IsNull" && method.Parameters.Count == 1 && method.ReturnType.MetadataType == MetadataType.Boolean)
            ?? throw new InvalidOperationException("The compact runtime does not expose a compatible IsNull helper.");

        TileNullCheckRewrite[] requested = rewrites.ToArray();
        if (requested.Length == 0 || requested.Select(rewrite => (rewrite.Method, rewrite.Producer)).Distinct().Count() != requested.Length)
            throw new InvalidOperationException("Tile null lowering requires a non-empty, unique set of exact producers.");

        int rewritten = 0;
        foreach (TileNullCheckRewrite rewrite in requested)
        {
            if (!rewrite.Method.HasBody || !rewrite.Method.Body.Instructions.Contains(rewrite.Producer))
                throw new InvalidOperationException("Tile null lowering producer does not belong to a method body.");
            Instruction? next = NextNonNop(rewrite.Producer);
            if (next is null)
                throw new InvalidOperationException($"Tile null lowering producer at {rewrite.Method.FullName}@IL_{rewrite.Producer.Offset:X4} has no consumer.");

            ILProcessor il = rewrite.Method.Body.GetILProcessor();
            if (IsDirectNullBranch(next.OpCode))
            {
                il.InsertBefore(next, il.Create(OpCodes.Call, isNull));
                rewritten++;
                continue;
            }

            if (next.OpCode == OpCodes.Ldnull && NextNonNop(next) is Instruction comparison && comparison.OpCode == OpCodes.Ceq)
            {
                il.Replace(next, il.Create(OpCodes.Call, isNull));
                il.Replace(comparison, il.Create(OpCodes.Nop));
                rewritten++;
                continue;
            }

            throw new InvalidOperationException($"Unsupported Tile null consumer {next.OpCode} at {rewrite.Method.FullName}@IL_{next.Offset:X4}.");
        }

        return rewritten;
    }

    private static Instruction? NextNonNop(Instruction instruction)
    {
        for (Instruction? current = instruction.Next; current is not null; current = current.Next)
        {
            if (current.OpCode != OpCodes.Nop)
                return current;
        }
        return null;
    }

    private static bool IsDirectNullBranch(OpCode opcode)
    {
        return opcode == OpCodes.Brfalse || opcode == OpCodes.Brfalse_S || opcode == OpCodes.Brtrue || opcode == OpCodes.Brtrue_S;
    }
}

public sealed record TileNullCheckRewrite(MethodDefinition Method, Instruction Producer);

/// <summary>
/// Converts explicitly audited Tile locals to compact handles after every load
/// and store in the containing body has already been made handle-compatible.
/// The lowerer does not infer dataflow: each local index is an exact manifest
/// entry, so an unknown alias cannot silently become a detached value.
/// </summary>
public static class TileHandleLocalLowerer
{
    public static int RewriteAfterBodyLowering(
        TypeDefinition tileType,
        TypeReference handleType,
        IEnumerable<TileLocalAliasRewrite> rewrites)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(handleType);
        ArgumentNullException.ThrowIfNull(rewrites);
        if (handleType.Module != tileType.Module || ContainsType(handleType, tileType.FullName))
            throw new InvalidOperationException("The compact Tile handle must be a distinct type in the Tile module.");

        TileLocalAliasRewrite[] requested = rewrites.ToArray();
        if (requested.Length == 0 || requested.Select(rewrite => (rewrite.Method, rewrite.LocalIndex)).Distinct().Count() != requested.Length)
            throw new InvalidOperationException("Tile local-alias lowering requires a non-empty, unique set of exact locals.");

        foreach (TileLocalAliasRewrite rewrite in requested)
        {
            if (!rewrite.Method.HasBody || rewrite.LocalIndex < 0 || rewrite.LocalIndex >= rewrite.Method.Body.Variables.Count)
                throw new InvalidOperationException($"Tile local-alias rewrite does not identify a valid local in {rewrite.Method.FullName}.");
            VariableDefinition local = rewrite.Method.Body.Variables[rewrite.LocalIndex];
            if (local.VariableType.FullName != tileType.FullName)
                throw new InvalidOperationException($"Tile local-alias rewrite {rewrite.Method.FullName} local {rewrite.LocalIndex} is not an exact Tile local.");
            local.VariableType = handleType;
        }

        return requested.Length;
    }

    private static bool ContainsType(TypeReference type, string fullName)
    {
        if (type.FullName == fullName)
            return true;
        if (type is GenericInstanceType generic && generic.GenericArguments.Any(argument => ContainsType(argument, fullName)))
            return true;
        return type is TypeSpecification specification && ContainsType(specification.ElementType, fullName);
    }
}

public sealed record TileLocalAliasRewrite(MethodDefinition Method, int LocalIndex);
