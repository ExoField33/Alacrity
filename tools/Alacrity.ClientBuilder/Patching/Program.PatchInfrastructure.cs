using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class Program
{
    internal static DefaultAssemblyResolver CreateResolver(string executablePath)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(executablePath)!);
        AddXnaSearchDirectories(resolver);
        return resolver;
    }

    private static void AddXnaSearchDirectories(DefaultAssemblyResolver resolver)
    {
        var xnaRoot = Environment.GetEnvironmentVariable("ALACRITY_XNA_REFERENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(xnaRoot))
        {
            xnaRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Microsoft.NET",
                "assembly",
                "GAC_32");
        }

        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Game", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Graphics", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Xact", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Content.Pipeline", "v4.0_4.0.0.0__842cf8be1de50553"));
    }

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition nested in type.NestedTypes)
        {
            foreach (TypeDefinition child in Flatten(nested))
            {
                yield return child;
            }
        }
    }

    private static void AddIfExists(DefaultAssemblyResolver resolver, string path)
    {
        if (Directory.Exists(path))
            resolver.AddSearchDirectory(path);
    }

    private static MethodReference ImportRuntimeMethod(ModuleDefinition module, string sourceExecutablePath, string name, string returnType, params string[] parameterTypes)
    {
        var facadePath = Path.Combine(Path.GetDirectoryName(sourceExecutablePath)!, "bin", "Alacrity.PluginUiRuntime.dll");
        if (!File.Exists(facadePath))
        {
            throw new ClientBuildException("Required staged ABI facade was not found: " + facadePath);
        }

        using var facade = ModuleDefinition.ReadModule(facadePath);
        var bridgeType = CecilPatchPrimitives.RequireType(facade, "AlacrityTerraria.PluginUiRuntime");
        var method = CecilPatchPrimitives.RequireMethod(bridgeType, name, returnType, parameterTypes);
        if (!method.IsPublic || !method.IsStatic || method.GenericParameters.Count != 0)
        {
            throw new ClientBuildException("Required staged bridge method is not a public non-generic static ABI method: " + method.FullName);
        }

        return module.ImportReference(method);
    }

    private static Instruction InsertAfter(ILProcessor il, Instruction target, Instruction instruction)
    {
        il.InsertAfter(target, instruction);
        return instruction;
    }

    private static void RetargetInstructionReferences(MethodDefinition method, Instruction from, Instruction to)
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (ReferenceEquals(instruction.Operand, from))
                instruction.Operand = to;
            else if (instruction.Operand is Instruction[] targets)
            {
                for (int index = 0; index < targets.Length; index++)
                    if (ReferenceEquals(targets[index], from))
                        targets[index] = to;
            }
        }

        foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
        {
            if (ReferenceEquals(handler.TryStart, from)) handler.TryStart = to;
            if (ReferenceEquals(handler.TryEnd, from)) handler.TryEnd = to;
            if (ReferenceEquals(handler.HandlerStart, from)) handler.HandlerStart = to;
            if (ReferenceEquals(handler.HandlerEnd, from)) handler.HandlerEnd = to;
            if (ReferenceEquals(handler.FilterStart, from)) handler.FilterStart = to;
        }
    }

    private static void ReplaceBody(MethodDefinition method, Action<ILProcessor> write)
    {
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.Instructions.Clear();
        write(method.Body.GetILProcessor());
    }

    private static bool IsStlocFor(this Instruction instruction, VariableDefinition variable)
    {
        var index = variable.Index;
        if (instruction.OpCode == OpCodes.Stloc_0)
            return index == 0;
        if (instruction.OpCode == OpCodes.Stloc_1)
            return index == 1;
        if (instruction.OpCode == OpCodes.Stloc_2)
            return index == 2;
        if (instruction.OpCode == OpCodes.Stloc_3)
            return index == 3;
        if (instruction.OpCode == OpCodes.Stloc || instruction.OpCode == OpCodes.Stloc_S)
            return instruction.Operand == variable;
        return false;
    }

    private static bool IsLdlocFor(this Instruction instruction, VariableDefinition variable)
    {
        var index = variable.Index;
        if (instruction.OpCode == OpCodes.Ldloc_0)
            return index == 0;
        if (instruction.OpCode == OpCodes.Ldloc_1)
            return index == 1;
        if (instruction.OpCode == OpCodes.Ldloc_2)
            return index == 2;
        if (instruction.OpCode == OpCodes.Ldloc_3)
            return index == 3;
        if (instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S)
            return instruction.Operand == variable;
        return false;
    }

    private static Instruction LoadLocal(ILProcessor il, VariableDefinition variable)
    {
        return variable.Index switch
        {
            0 => il.Create(OpCodes.Ldloc_0),
            1 => il.Create(OpCodes.Ldloc_1),
            2 => il.Create(OpCodes.Ldloc_2),
            3 => il.Create(OpCodes.Ldloc_3),
            <= byte.MaxValue => il.Create(OpCodes.Ldloc_S, variable),
            _ => il.Create(OpCodes.Ldloc, variable)
        };
    }

    private static Instruction StoreLocal(ILProcessor il, VariableDefinition variable)
    {
        return variable.Index switch
        {
            0 => il.Create(OpCodes.Stloc_0),
            1 => il.Create(OpCodes.Stloc_1),
            2 => il.Create(OpCodes.Stloc_2),
            3 => il.Create(OpCodes.Stloc_3),
            <= byte.MaxValue => il.Create(OpCodes.Stloc_S, variable),
            _ => il.Create(OpCodes.Stloc, variable)
        };
    }

    private static bool IsLoadInt(Instruction instruction, int value)
    {
        if (instruction.OpCode == OpCodes.Ldc_I4)
            return instruction.Operand is int operand && operand == value;
        if (instruction.OpCode == OpCodes.Ldc_I4_S)
            return instruction.Operand is sbyte operand && operand == value;
        if (value == -1 && instruction.OpCode == OpCodes.Ldc_I4_M1)
            return true;
        if (value >= 0 && value <= 8)
        {
            return value switch
            {
                0 => instruction.OpCode == OpCodes.Ldc_I4_0,
                1 => instruction.OpCode == OpCodes.Ldc_I4_1,
                2 => instruction.OpCode == OpCodes.Ldc_I4_2,
                3 => instruction.OpCode == OpCodes.Ldc_I4_3,
                4 => instruction.OpCode == OpCodes.Ldc_I4_4,
                5 => instruction.OpCode == OpCodes.Ldc_I4_5,
                6 => instruction.OpCode == OpCodes.Ldc_I4_6,
                7 => instruction.OpCode == OpCodes.Ldc_I4_7,
                8 => instruction.OpCode == OpCodes.Ldc_I4_8,
                _ => false
            };
        }
        return false;
    }

    private static VariableDefinition? GetLoadedLocalVariable(MethodDefinition method, Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldloc_0)
            return method.Body.Variables[0];
        if (instruction.OpCode == OpCodes.Ldloc_1)
            return method.Body.Variables[1];
        if (instruction.OpCode == OpCodes.Ldloc_2)
            return method.Body.Variables[2];
        if (instruction.OpCode == OpCodes.Ldloc_3)
            return method.Body.Variables[3];
        if (instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S)
            return instruction.Operand as VariableDefinition;
        return null;
    }
}
