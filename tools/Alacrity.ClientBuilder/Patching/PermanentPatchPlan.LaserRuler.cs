using Mono.Cecil;
using Mono.Cecil.Cil;

// The native laser ruler is a UI presentation pass. This patch deliberately replaces neither
// player state nor ruler input; a false bridge result executes the original 1.4.5.6 method.
internal static partial class PermanentPatchPlan
{
    private static void PatchLaserRulerPresentation(ModuleDefinition module, MethodReference tryDrawPresentation)
    {
        TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        MethodDefinition drawLaserRuler = RequireSingleMethod(main, "DrawInterface_3_LaserRuler", 0);
        if (!drawLaserRuler.IsStatic ||
            drawLaserRuler.ReturnType.FullName != module.TypeSystem.Void.FullName ||
            !drawLaserRuler.HasBody ||
            drawLaserRuler.Body.Instructions.Count == 0)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawInterface_3_LaserRuler does not match the verified static void method shape.");
        }

        if (drawLaserRuler.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference method &&
            string.Equals(method.Name, "TryDrawLaserRulerPresentation", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawInterface_3_LaserRuler already contains the Alacrity laser-ruler bridge call.");
        }

        int reverseGravityCalls = drawLaserRuler.Body.Instructions.Count(instruction =>
            instruction.Operand is MethodReference method &&
            string.Equals(method.DeclaringType.FullName, "Terraria.Main", StringComparison.Ordinal) &&
            string.Equals(method.Name, "ReverseGravitySupport", StringComparison.Ordinal));
        if (reverseGravityCalls < 3)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawInterface_3_LaserRuler no longer contains the verified native ruler draw structure.");
        }

        Instruction nativeStart = drawLaserRuler.Body.Instructions[0];
        ILProcessor il = drawLaserRuler.Body.GetILProcessor();
        il.InsertBefore(nativeStart, il.Create(OpCodes.Call, tryDrawPresentation));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Brfalse, nativeStart));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Ret));
    }
}
