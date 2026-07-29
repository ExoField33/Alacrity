using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Rewrites explicitly approved Tile value signatures after their bodies and
/// callers already produce compact handles. It intentionally rejects ref/out,
/// null-sensitive, or partially migrated paths; those require dedicated
/// lowerers rather than an unsafe broad type substitution.
/// </summary>
public static class TileValueSignatureLowerer
{
    public static int RewriteAfterBodyLowering(
        TypeDefinition tileType,
        TypeReference handleType,
        IEnumerable<TileValueSignatureRewrite> rewrites,
        IEnumerable<MethodDefinition> migratedCallers)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(handleType);
        ArgumentNullException.ThrowIfNull(rewrites);
        ArgumentNullException.ThrowIfNull(migratedCallers);
        if (handleType.Module != tileType.Module || ContainsType(handleType, tileType.FullName))
            throw new InvalidOperationException("The compact handle type must be a distinct type in the Tile module.");

        TileValueSignatureRewrite[] requested = rewrites.ToArray();
        if (requested.Length == 0 || requested.Select(rewrite => rewrite.Method).Distinct().Count() != requested.Length)
            throw new InvalidOperationException("Tile value-signature rewriting requires a non-empty, unique set of explicit methods.");
        var requestedMethods = new HashSet<MethodDefinition>(requested.Select(rewrite => rewrite.Method));
        var callers = new HashSet<MethodDefinition>(migratedCallers);
        ModuleDefinition module = tileType.Module;

        foreach (TileValueSignatureRewrite rewrite in requested)
            ValidateRewrite(tileType, rewrite);
        foreach (MethodDefinition caller in callers)
            ValidateBody(caller, tileType.FullName, requestedMethods);
        ValidateAllIncomingCalls(module, requestedMethods, callers);

        foreach (TileValueSignatureRewrite rewrite in requested)
        {
            if (rewrite.RewriteReturn)
                rewrite.Method.ReturnType = handleType;
            foreach (int parameterIndex in rewrite.ParameterIndexes)
                rewrite.Method.Parameters[parameterIndex].ParameterType = handleType;
        }

        foreach (MethodDefinition caller in callers)
        {
            foreach (Instruction instruction in caller.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference reference || !TryResolve(reference, out MethodDefinition? target) || target is null)
                    continue;
                if (requestedMethods.Contains(target))
                    instruction.Operand = target;
            }
        }

        foreach (TileValueSignatureRewrite rewrite in requested)
        {
            if (rewrite.RewriteReturn && rewrite.Method.ReturnType.FullName != handleType.FullName)
                throw new InvalidOperationException($"The compact return signature was not applied to {rewrite.Method.FullName}.");
            foreach (int parameterIndex in rewrite.ParameterIndexes)
            {
                if (rewrite.Method.Parameters[parameterIndex].ParameterType.FullName != handleType.FullName)
                    throw new InvalidOperationException($"The compact parameter signature was not applied to {rewrite.Method.FullName} parameter {parameterIndex}.");
            }
        }

        return requested.Length;
    }

    private static void ValidateRewrite(TypeDefinition tileType, TileValueSignatureRewrite rewrite)
    {
        MethodDefinition method = rewrite.Method ?? throw new InvalidOperationException("A Tile signature rewrite did not specify a method.");
        if (method.Module != tileType.Module || !method.HasBody || method.HasGenericParameters)
            throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} is not a supported concrete in-module method.");
        if (!rewrite.RewriteReturn && rewrite.ParameterIndexes.Count == 0)
            throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} does not select a Tile return or parameter.");
        if (rewrite.RewriteReturn && method.ReturnType.FullName != tileType.FullName)
            throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} does not have the expected Tile return type.");
        foreach (int index in rewrite.ParameterIndexes)
        {
            if (index < 0 || index >= method.Parameters.Count || method.Parameters[index].ParameterType.FullName != tileType.FullName)
                throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} parameter {index} is not an exact Tile value parameter.");
        }

        ValidateBody(method, tileType.FullName, allowedTargets: null);
    }

    private static void ValidateAllIncomingCalls(ModuleDefinition module, ISet<MethodDefinition> targets, ISet<MethodDefinition> callers)
    {
        foreach (MethodDefinition method in Flatten(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference reference || !TryResolve(reference, out MethodDefinition? target) || target is null || !targets.Contains(target))
                    continue;
                if (!callers.Contains(method))
                    throw new InvalidOperationException($"Tile signature target {target.FullName} has an unmigrated incoming call at {method.FullName}@IL_{instruction.Offset:X4}.");
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    throw new InvalidOperationException($"Tile signature target {target.FullName} is used by unsupported opcode {instruction.OpCode} at {method.FullName}@IL_{instruction.Offset:X4}.");
            }
        }
    }

    private static void ValidateBody(MethodDefinition method, string tileTypeName, ISet<MethodDefinition>? allowedTargets)
    {
        if (method.Body.Variables.Any(variable => ContainsType(variable.VariableType, tileTypeName)))
            throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} still has a Tile local.");
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (instruction.Operand is FieldReference field && ContainsType(field.DeclaringType, tileTypeName))
                throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} still references Tile field {field.FullName}.");
            if (instruction.Operand is MethodReference reference)
            {
                if (ContainsType(reference.DeclaringType, tileTypeName))
                    throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} still calls Tile member {reference.FullName}.");
                if (allowedTargets is not null && TryResolve(reference, out MethodDefinition? target) && target is not null && allowedTargets.Contains(target))
                    continue;
                if (ContainsType(reference.ReturnType, tileTypeName) || reference.Parameters.Any(parameter => ContainsType(parameter.ParameterType, tileTypeName)))
                    throw new InvalidOperationException($"Tile signature rewrite target {method.FullName} has an unrelated Tile signature call {reference.FullName}.");
            }
        }
    }

    private static bool TryResolve(MethodReference reference, out MethodDefinition? definition)
    {
        try
        {
            definition = reference.Resolve();
            return definition is not null;
        }
        catch (AssemblyResolutionException)
        {
            definition = null;
            return false;
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

    private static bool ContainsType(TypeReference type, string fullName)
    {
        if (type.FullName == fullName)
            return true;
        if (type is GenericInstanceType generic && generic.GenericArguments.Any(argument => ContainsType(argument, fullName)))
            return true;
        return type is TypeSpecification specification && ContainsType(specification.ElementType, fullName);
    }
}

public sealed record TileValueSignatureRewrite(MethodDefinition Method, bool RewriteReturn, IReadOnlyList<int> ParameterIndexes);
