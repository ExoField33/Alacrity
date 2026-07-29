using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class FixtureMetadataDiagnostics
{
    public static void Write(string assemblyPath)
    {
        string? assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (string.IsNullOrEmpty(assemblyDirectory))
            throw new InvalidOperationException("The transformed fixture must have a parent directory.");
        string reportPath = Path.Combine(assemblyDirectory, "tile-transform-metadata-report.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report = new StringBuilder();
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { InMemory = true });
        report.AppendLine($"Assembly: {assembly.Name.FullName}");
        foreach (TypeDefinition type in Flatten(assembly.MainModule.Types))
        {
            report.AppendLine($"TYPE {type.FullName} base={Describe(type.BaseType)} interfaces={string.Join(",", type.Interfaces.Select(value => Describe(value.InterfaceType)))}");
            foreach (FieldDefinition field in type.Fields)
                report.AppendLine($" FIELD {field.FullName} : {Describe(field.FieldType)}");
            foreach (MethodDefinition method in type.Methods)
            {
                report.AppendLine($" METHOD {method.FullName} cc={method.CallingConvention} this={method.HasThis}/{method.ExplicitThis} return={Describe(method.ReturnType)} params={string.Join(",", method.Parameters.Select(value => Describe(value.ParameterType)))}");
                if (!method.HasBody)
                    continue;
                report.AppendLine($"  LOCALS {string.Join(",", method.Body.Variables.Select(value => Describe(value.VariableType)))}");
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    string operand = DescribeOperand(instruction.Operand, method, instruction.Offset);
                    report.AppendLine($"  IL_{instruction.Offset:X4} {instruction.OpCode} {operand}");
                }
            }
        }
        File.WriteAllText(reportPath, report.ToString());
    }

    private static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types) { yield return type; foreach (TypeDefinition nested in Flatten(type.NestedTypes)) yield return nested; }
    }

    private static string DescribeOperand(object? operand, MethodDefinition caller, int offset)
    {
        if (operand is FieldReference field)
        {
            FieldDefinition? resolved = ResolveField(field);
            return $"FIELD {field.FullName} resolved={resolved?.FullName ?? "<unresolved>"} mismatch={resolved != null && (field.FieldType.FullName != resolved.FieldType.FullName || field.DeclaringType.FullName != resolved.DeclaringType.FullName)}";
        }
        if (operand is MethodReference method)
        {
            MethodDefinition? resolved = ResolveMethod(method);
            bool mismatch = resolved != null && (method.ReturnType.FullName != resolved.ReturnType.FullName || method.Parameters.Count != resolved.Parameters.Count || method.HasThis != resolved.HasThis || method.ExplicitThis != resolved.ExplicitThis || method.Parameters.Where((value, index) => value.ParameterType.FullName != resolved.Parameters[index].ParameterType.FullName).Any());
            return $"METHOD {method.FullName} resolved={resolved?.FullName ?? "<unresolved>"} mismatch={mismatch}";
        }
        return operand is TypeReference type ? "TYPE " + Describe(type) : operand?.ToString() ?? string.Empty;
    }

    private static FieldDefinition? ResolveField(FieldReference reference) { try { return reference.Resolve(); } catch { return null; } }
    private static MethodDefinition? ResolveMethod(MethodReference reference) { try { return reference.Resolve(); } catch { return null; } }

    private static string Describe(TypeReference? type) => type switch
    {
        null => "<null>",
        ArrayType array => $"array(rank={array.Rank},{Describe(array.ElementType)})",
        ByReferenceType byRef => "byref(" + Describe(byRef.ElementType) + ")",
        PointerType pointer => "ptr(" + Describe(pointer.ElementType) + ")",
        PinnedType pinned => "pinned(" + Describe(pinned.ElementType) + ")",
        RequiredModifierType required => "modreq(" + Describe(required.ModifierType) + "," + Describe(required.ElementType) + ")",
        OptionalModifierType optional => "modopt(" + Describe(optional.ModifierType) + "," + Describe(optional.ElementType) + ")",
        GenericInstanceType generic => "generic(" + Describe(generic.ElementType) + "<" + string.Join(",", generic.GenericArguments.Select(Describe)) + ">)",
        TypeSpecification specification => "spec(" + Describe(specification.ElementType) + ")",
        _ => type.FullName
    };
}
