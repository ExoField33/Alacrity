using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>Small strict Cecil helpers used only by the audited permanent patch catalog.</summary>
internal static class CecilPatchPrimitives
{
    internal static TypeDefinition RequireType(ModuleDefinition module, string fullName)
    {
        for (var index = 0; index < module.Types.Count; index++)
        {
            var found = FindType(module.Types[index], fullName);
            if (found != null)
            {
                return found;
            }
        }

        throw new ClientBuildException("Required type was not found: " + fullName + ".");
    }

    internal static MethodDefinition RequireMethod(TypeDefinition type, string name, string returnType, params string[] parameterTypes)
    {
        MethodDefinition? match = null;
        for (var index = 0; index < type.Methods.Count; index++)
        {
            var method = type.Methods[index];
            if (!string.Equals(method.Name, name, StringComparison.Ordinal) ||
                !string.Equals(method.ReturnType.FullName, returnType, StringComparison.Ordinal) ||
                method.Parameters.Count != parameterTypes.Length)
            {
                continue;
            }

            var compatible = true;
            for (var parameterIndex = 0; parameterIndex < parameterTypes.Length; parameterIndex++)
            {
                if (!string.Equals(method.Parameters[parameterIndex].ParameterType.FullName, parameterTypes[parameterIndex], StringComparison.Ordinal))
                {
                    compatible = false;
                    break;
                }
            }

            if (!compatible)
            {
                continue;
            }

            if (match != null)
            {
                throw new ClientBuildException("Required method is ambiguous: " + type.FullName + "::" + name + ".");
            }

            match = method;
        }

        return match ?? throw new ClientBuildException("Required method was not found: " + type.FullName + "::" + name + ".");
    }

    internal static Instruction RequireUniqueInstruction(MethodDefinition method, Func<Instruction, bool> predicate, string description)
    {
        Instruction? match = null;
        for (var index = 0; index < method.Body.Instructions.Count; index++)
        {
            var instruction = method.Body.Instructions[index];
            if (!predicate(instruction))
            {
                continue;
            }

            if (match != null)
            {
                throw new ClientBuildException("Expected one " + description + " in " + method.FullName + ", but found more than one.");
            }

            match = instruction;
        }

        return match ?? throw new ClientBuildException("Expected " + description + " in " + method.FullName + ".");
    }

    /// <summary>Inserts a small verified sequence before an existing anchor without retargeting branches or handlers.</summary>
    internal static void InsertBefore(ILProcessor processor, Instruction anchor, params Instruction[] instructions)
    {
        if (processor == null)
        {
            throw new ArgumentNullException(nameof(processor));
        }
        if (anchor == null)
        {
            throw new ArgumentNullException(nameof(anchor));
        }
        if (instructions == null || instructions.Length == 0)
        {
            throw new ArgumentException("At least one instruction is required.", nameof(instructions));
        }

        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] == null)
            {
                throw new ArgumentException("Insertion instructions cannot contain null.", nameof(instructions));
            }

            processor.InsertBefore(anchor, instructions[index]);
        }
    }

    private static TypeDefinition? FindType(TypeDefinition type, string fullName)
    {
        if (string.Equals(type.FullName, fullName, StringComparison.Ordinal))
        {
            return type;
        }

        for (var index = 0; index < type.NestedTypes.Count; index++)
        {
            var found = FindType(type.NestedTypes[index], fullName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
