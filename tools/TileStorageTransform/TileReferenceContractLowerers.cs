using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Lowers explicit Tile reference fields only after every field consumer has
/// been rewritten to handle semantics. This class intentionally does not try
/// to reinterpret raw field load/store IL: field assignment can preserve Tile
/// identity, while compact map writes copy state, so that decision belongs to
/// the consumer lowerer.
/// </summary>
public static class TileReferenceFieldLowerer
{
    public static int RewriteAfterConsumerLowering(
        TypeDefinition tileType,
        TypeReference handleType,
        IEnumerable<FieldDefinition> fields,
        IEnumerable<MethodDefinition> migratedConsumers)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(handleType);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(migratedConsumers);
        EnsureDistinctCompactHandle(tileType, handleType);

        FieldDefinition[] requested = fields.ToArray();
        if (requested.Length == 0 || requested.Distinct().Count() != requested.Length)
            throw new InvalidOperationException("Tile reference-field lowering requires a non-empty, unique field set.");

        var requestedFields = new HashSet<FieldDefinition>(requested);
        var consumers = new HashSet<MethodDefinition>(migratedConsumers);
        foreach (FieldDefinition field in requested)
        {
            if (field.Module != tileType.Module || field.FieldType.FullName != tileType.FullName)
                throw new InvalidOperationException($"Tile reference field {field.FullName} is not an exact in-module Tile field.");
        }

        ValidateAllConsumers(tileType.Module, requestedFields, consumers);
        foreach (FieldDefinition field in requested)
            field.FieldType = handleType;

        NormalizeConsumerReferences(consumers, requestedFields);
        return requested.Length;
    }

    private static void ValidateAllConsumers(ModuleDefinition module, ISet<FieldDefinition> fields, ISet<MethodDefinition> consumers)
    {
        foreach (MethodDefinition method in Flatten(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not FieldReference reference || !TryResolve(reference, out FieldDefinition? field) || field is null || !fields.Contains(field))
                    continue;
                if (!consumers.Contains(method))
                    throw new InvalidOperationException($"Tile reference field {field.FullName} has an unmigrated consumer at {method.FullName}@IL_{instruction.Offset:X4}.");
                if (instruction.OpCode != OpCodes.Ldfld && instruction.OpCode != OpCodes.Ldsfld &&
                    instruction.OpCode != OpCodes.Stfld && instruction.OpCode != OpCodes.Stsfld)
                {
                    throw new InvalidOperationException($"Tile reference field {field.FullName} has unsupported consumer opcode {instruction.OpCode} at {method.FullName}@IL_{instruction.Offset:X4}.");
                }
            }
        }
    }

    private static void NormalizeConsumerReferences(IEnumerable<MethodDefinition> consumers, ISet<FieldDefinition> fields)
    {
        foreach (MethodDefinition method in consumers)
        {
            if (!method.HasBody)
                throw new InvalidOperationException($"Tile reference field consumer {method.FullName} has no method body.");
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is FieldReference reference && TryResolve(reference, out FieldDefinition? field) && field is not null && fields.Contains(field))
                    instruction.Operand = field;
            }
        }
    }

    private static void EnsureDistinctCompactHandle(TypeDefinition tileType, TypeReference handleType)
    {
        if (handleType.Module != tileType.Module || ContainsType(handleType, tileType.FullName))
            throw new InvalidOperationException("The compact Tile handle must be a distinct type in the Tile module.");
    }

    private static bool TryResolve(FieldReference reference, out FieldDefinition? definition)
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

/// <summary>
/// Rewrites explicit <c>ref</c>/<c>out Tile</c> contracts to a compact handle
/// value after method bodies and every caller have already been lowered. Tile
/// itself is a reference object, so a live compact handle is the equivalent
/// receiver for mutation-only methods; a raw <c>ref CompactHandle</c> would be
/// a different aliasing contract. Methods that reassign their ref Tile input
/// must use a dedicated slot-replacement lowering instead.
/// </summary>
public static class TileByReferenceSignatureLowerer
{
    public static int RewriteAfterBodyLowering(
        TypeDefinition tileType,
        TypeReference handleType,
        IEnumerable<TileByReferenceSignatureRewrite> rewrites,
        IEnumerable<MethodDefinition> migratedCallers)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(handleType);
        ArgumentNullException.ThrowIfNull(rewrites);
        ArgumentNullException.ThrowIfNull(migratedCallers);
        if (handleType.Module != tileType.Module || ContainsType(handleType, tileType.FullName))
            throw new InvalidOperationException("The compact Tile handle must be a distinct type in the Tile module.");

        TileByReferenceSignatureRewrite[] requested = rewrites.ToArray();
        if (requested.Length == 0 || requested.Select(rewrite => rewrite.Method).Distinct().Count() != requested.Length)
            throw new InvalidOperationException("Tile by-reference signature lowering requires a non-empty, unique method set.");

        var methods = new HashSet<MethodDefinition>(requested.Select(rewrite => rewrite.Method));
        var callers = new HashSet<MethodDefinition>(migratedCallers);
        foreach (TileByReferenceSignatureRewrite rewrite in requested)
            ValidateRewrite(tileType, rewrite);
        ValidateIncomingCalls(tileType.Module, methods, callers);

        foreach (TileByReferenceSignatureRewrite rewrite in requested)
            rewrite.Method.Parameters[rewrite.ParameterIndex].ParameterType = handleType;
        foreach (MethodDefinition caller in callers)
        {
            foreach (Instruction instruction in caller.Body.Instructions)
            {
                if (instruction.Operand is MethodReference reference && TryResolve(reference, out MethodDefinition? target) && target is not null && methods.Contains(target))
                    instruction.Operand = target;
            }
        }

        return requested.Length;
    }

    private static void ValidateRewrite(TypeDefinition tileType, TileByReferenceSignatureRewrite rewrite)
    {
        MethodDefinition method = rewrite.Method ?? throw new InvalidOperationException("A Tile by-reference signature rewrite did not specify a method.");
        if (method.Module != tileType.Module || !method.HasBody || method.HasGenericParameters)
            throw new InvalidOperationException($"Tile by-reference target {method.FullName} is not a supported concrete in-module method.");
        if (rewrite.ParameterIndex < 0 || rewrite.ParameterIndex >= method.Parameters.Count)
            throw new InvalidOperationException($"Tile by-reference target {method.FullName} has no parameter {rewrite.ParameterIndex}.");
        if (method.Parameters[rewrite.ParameterIndex].ParameterType is not ByReferenceType byReference || byReference.ElementType.FullName != tileType.FullName)
            throw new InvalidOperationException($"Tile by-reference target {method.FullName} parameter {rewrite.ParameterIndex} is not an exact ref/out Tile parameter.");
        if (method.Parameters[rewrite.ParameterIndex].IsOut)
            throw new InvalidOperationException($"Tile by-reference target {method.FullName} parameter {rewrite.ParameterIndex} is an out Tile contract and requires the dedicated out-signature lowerer.");
        if (method.Body.Variables.Any(variable => ContainsType(variable.VariableType, tileType.FullName)))
            throw new InvalidOperationException($"Tile by-reference target {method.FullName} still contains a Tile local.");
        if (method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Starg && instruction.Operand is ParameterDefinition parameter && ReferenceEquals(parameter, method.Parameters[rewrite.ParameterIndex])))
            throw new InvalidOperationException($"Tile by-reference target {method.FullName} reassigns its ref/out Tile slot and requires a dedicated slot-replacement lowering.");
    }

    private static void ValidateIncomingCalls(ModuleDefinition module, ISet<MethodDefinition> targets, ISet<MethodDefinition> callers)
    {
        foreach (MethodDefinition method in Flatten(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference reference || !TryResolve(reference, out MethodDefinition? target) || target is null || !targets.Contains(target))
                    continue;
                if (!callers.Contains(method))
                    throw new InvalidOperationException($"Tile by-reference target {target.FullName} has an unmigrated incoming call at {method.FullName}@IL_{instruction.Offset:X4}.");
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    throw new InvalidOperationException($"Tile by-reference target {target.FullName} is used by unsupported opcode {instruction.OpCode} at {method.FullName}@IL_{instruction.Offset:X4}.");
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

public sealed record TileByReferenceSignatureRewrite(MethodDefinition Method, int ParameterIndex);

/// <summary>
/// Lowers explicit <c>out Tile</c> contracts to <c>out CompactHandle</c>.
/// Unlike mutation-only ref Tile parameters, an out parameter carries a nullable
/// result and therefore retains a managed by-reference wrapper around the
/// compact handle value.
/// </summary>
public static class TileOutSignatureLowerer
{
    public static int RewriteAfterBodyLowering(
        TypeDefinition tileType,
        TypeReference handleType,
        IEnumerable<TileOutSignatureRewrite> rewrites,
        IEnumerable<MethodDefinition> migratedCallers)
    {
        ArgumentNullException.ThrowIfNull(tileType);
        ArgumentNullException.ThrowIfNull(handleType);
        ArgumentNullException.ThrowIfNull(rewrites);
        ArgumentNullException.ThrowIfNull(migratedCallers);
        if (handleType.Module != tileType.Module || ContainsType(handleType, tileType.FullName))
            throw new InvalidOperationException("The compact Tile handle must be a distinct type in the Tile module.");

        TileOutSignatureRewrite[] requested = rewrites.ToArray();
        if (requested.Length == 0 || requested.Select(rewrite => rewrite.Method).Distinct().Count() != requested.Length)
            throw new InvalidOperationException("Tile out-signature lowering requires a non-empty, unique method set.");
        var methods = new HashSet<MethodDefinition>(requested.Select(rewrite => rewrite.Method));
        var callers = new HashSet<MethodDefinition>(migratedCallers);
        foreach (TileOutSignatureRewrite rewrite in requested)
            ValidateRewrite(tileType, rewrite);
        ValidateIncomingCalls(tileType.Module, methods, callers);

        foreach (TileOutSignatureRewrite rewrite in requested)
            rewrite.Method.Parameters[rewrite.ParameterIndex].ParameterType = new ByReferenceType(handleType);
        foreach (MethodDefinition caller in callers)
        {
            foreach (Instruction instruction in caller.Body.Instructions)
            {
                if (instruction.Operand is MethodReference reference && TryResolve(reference, out MethodDefinition? target) && target is not null && methods.Contains(target))
                    instruction.Operand = target;
            }
        }
        return requested.Length;
    }

    private static void ValidateRewrite(TypeDefinition tileType, TileOutSignatureRewrite rewrite)
    {
        MethodDefinition method = rewrite.Method ?? throw new InvalidOperationException("A Tile out-signature rewrite did not specify a method.");
        if (method.Module != tileType.Module || !method.HasBody || method.HasGenericParameters)
            throw new InvalidOperationException($"Tile out-signature target {method.FullName} is not a supported concrete in-module method.");
        if (rewrite.ParameterIndex < 0 || rewrite.ParameterIndex >= method.Parameters.Count ||
            method.Parameters[rewrite.ParameterIndex].ParameterType is not ByReferenceType byReference ||
            byReference.ElementType.FullName != tileType.FullName || !method.Parameters[rewrite.ParameterIndex].IsOut)
        {
            throw new InvalidOperationException($"Tile out-signature target {method.FullName} parameter {rewrite.ParameterIndex} is not an exact out Tile parameter.");
        }
        if (method.Body.Variables.Any(variable => ContainsType(variable.VariableType, tileType.FullName)))
            throw new InvalidOperationException($"Tile out-signature target {method.FullName} still contains a Tile local.");
    }

    private static void ValidateIncomingCalls(ModuleDefinition module, ISet<MethodDefinition> targets, ISet<MethodDefinition> callers)
    {
        foreach (MethodDefinition method in Flatten(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference reference || !TryResolve(reference, out MethodDefinition? target) || target is null || !targets.Contains(target))
                    continue;
                if (!callers.Contains(method))
                    throw new InvalidOperationException($"Tile out-signature target {target.FullName} has an unmigrated incoming call at {method.FullName}@IL_{instruction.Offset:X4}.");
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    throw new InvalidOperationException($"Tile out-signature target {target.FullName} is used by unsupported opcode {instruction.OpCode} at {method.FullName}@IL_{instruction.Offset:X4}.");
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

public sealed record TileOutSignatureRewrite(MethodDefinition Method, int ParameterIndex);
