using Mono.Cecil;
using Mono.Cecil.Cil;

// Version-locked replacement of only the two LightMap blur passes and TileLightScanner export.
// The native range delegates remain intact, preserving all lighting math and deterministic tile
// randomization. The facade chooses the original FastParallel implementation when Kinesin is off.
internal static partial class PermanentPatchPlan
{
    private const string LightingParallelWrapperMethodName = "AlacrityRunLightingParallel";

    private static void PatchLightingParallelism(ModuleDefinition module, MethodReference runLightingParallel)
    {
        TypeDefinition lightMap = CecilPatchPrimitives.RequireType(module, "Terraria.Graphics.Light.LightMap");
        TypeDefinition scanner = CecilPatchPrimitives.RequireType(module, "Terraria.Graphics.Light.TileLightScanner");
        MethodDefinition blurPass = RequireSingleMethod(lightMap, "BlurPass", 0);
        MethodDefinition exportTo = RequireSingleMethod(scanner, "ExportTo", 3);

        ReplaceFastParallelCalls(lightMap, blurPass, runLightingParallel, 2);
        ReplaceFastParallelCalls(scanner, exportTo, runLightingParallel, 1);
    }

    private static void ReplaceFastParallelCalls(
        TypeDefinition owner,
        MethodDefinition method,
        MethodReference runLightingParallel,
        int expectedCount)
    {
        if (!method.HasBody)
        {
            throw new InvalidOperationException("Terraria lighting method has no body: " + method.FullName);
        }

        var matches = new List<Instruction>();
        for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
        {
            Instruction instruction = method.Body.Instructions[instructionIndex];
            if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) ||
                !(instruction.Operand is MethodReference target) ||
                !string.Equals(target.DeclaringType.FullName, "ReLogic.Threading.FastParallel", StringComparison.Ordinal) ||
                !string.Equals(target.Name, "For", StringComparison.Ordinal) ||
                target.Parameters.Count != 4 ||
                target.ReturnType.FullName != "System.Void")
            {
                continue;
            }

            if (target.Parameters[0].ParameterType.FullName != "System.Int32" ||
                target.Parameters[1].ParameterType.FullName != "System.Int32" ||
                target.Parameters[2].ParameterType.FullName != "ReLogic.Threading.ParallelForAction" ||
                target.Parameters[3].ParameterType.FullName != "System.Object")
            {
                continue;
            }

            matches.Add(instruction);
        }

        if (matches.Count != expectedCount)
        {
            throw new InvalidOperationException(
                "Terraria 1.4.5.6 " + method.FullName + " has " + matches.Count +
                " verified FastParallel.For calls; expected " + expectedCount + ".");
        }

        MethodReference nativeFastParallel = (MethodReference)matches[0].Operand;
        MethodDefinition wrapper = CreateFastParallelWrapper(
            method.Module,
            owner,
            nativeFastParallel,
            runLightingParallel);
        owner.Methods.Add(wrapper);

        for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            matches[matchIndex].OpCode = OpCodes.Call;
            matches[matchIndex].Operand = wrapper;
        }
    }

    private static MethodDefinition CreateFastParallelWrapper(
        ModuleDefinition module,
        TypeDefinition owner,
        MethodReference nativeFastParallel,
        MethodReference runLightingParallel)
    {
        if (owner.Methods.Any(method => method.Name == LightingParallelWrapperMethodName))
        {
            throw new InvalidOperationException(
                "Terraria lighting type already has generated wrapper " +
                LightingParallelWrapperMethodName + ": " + owner.FullName);
        }

        var wrapper = new MethodDefinition(
            LightingParallelWrapperMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        for (int parameterIndex = 0; parameterIndex < nativeFastParallel.Parameters.Count; parameterIndex++)
        {
            ParameterDefinition nativeParameter = nativeFastParallel.Parameters[parameterIndex];
            wrapper.Parameters.Add(new ParameterDefinition(
                nativeParameter.Name,
                ParameterAttributes.None,
                module.ImportReference(nativeParameter.ParameterType)));
        }

        Instruction nativeFallback = Instruction.Create(OpCodes.Nop);
        ILProcessor il = wrapper.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Call, runLightingParallel));
        il.Append(il.Create(OpCodes.Brfalse, nativeFallback));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(nativeFallback);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Call, nativeFastParallel));
        il.Append(il.Create(OpCodes.Ret));
        return wrapper;
    }
}
