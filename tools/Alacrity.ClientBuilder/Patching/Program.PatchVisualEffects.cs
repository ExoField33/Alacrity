using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class Program
{
    private static void PatchVisualEffects(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldRunDustSystem, MethodReference shouldCreateDust, MethodReference shouldUpdateDustInstance, MethodReference shouldDrawDustInstance, MethodReference shouldRunGoreSystem)
    {
        var dustType = module.Types.SingleOrDefault(type => type.FullName == "Terraria.Dust")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Dust type was not found.");
        var goreType = module.Types.SingleOrDefault(type => type.FullName == "Terraria.Gore")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Gore type was not found.");
        var drawDust = mainType.Methods.SingleOrDefault(method => method.Name == "DrawDust" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawDust signature did not match the verified visual-effects hook.");
        var updateDust = dustType.Methods.SingleOrDefault(method => method.Name == "UpdateDust" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Dust.UpdateDust signature did not match the verified visual-effects hook.");
        var newDust = dustType.Methods.SingleOrDefault(method => method.Name == "NewDust" && method.ReturnType.FullName == "System.Int32" && method.Parameters.Count == 9 && method.Parameters[3].ParameterType.FullName == "System.Int32")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Dust.NewDust signature did not match the verified visual-effects hook.");

        PatchRenderGate(drawDust, shouldRunDustSystem);
        PatchDustCreationTypeGuard(newDust, shouldCreateDust);
        PatchVoidReturnGate(updateDust, shouldRunDustSystem);
        PatchDustInstanceGuard(updateDust, shouldUpdateDustInstance);
        PatchDustDrawInstanceGuard(module, drawDust, shouldDrawDustInstance);

        foreach (string name in new[] { "DrawGore", "DrawGoreBehind", "DrawBackGore" })
        {
            var drawGore = mainType.Methods.SingleOrDefault(method => method.Name == name && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
                ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + name + " signature did not match the verified visual-effects hook.");
            PatchRenderGate(drawGore, shouldRunGoreSystem);
        }
        var newGore = goreType.Methods.SingleOrDefault(method => method.Name == "NewGore" && method.ReturnType.FullName == "System.Int32")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Gore.NewGore signature did not match the verified visual-effects hook.");
        var updateGore = goreType.Methods.SingleOrDefault(method => method.Name == "Update" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Gore.Update signature did not match the verified visual-effects hook.");
        PatchIntReturnGate(newGore, shouldRunGoreSystem, 600);
        PatchVoidReturnGate(updateGore, shouldRunGoreSystem);
    }

    private static void PatchDustCreationTypeGuard(MethodDefinition method, MethodReference shouldCreateDust)
    {
        var first = method.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("Dust.NewDust has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_3));
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldCreateDust));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 6000));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchDustInstanceGuard(MethodDefinition method, MethodReference shouldUpdateDustInstance)
    {
        var dustType = method.DeclaringType;
        var dustLocal = method.Body.Variables.FirstOrDefault(variable => variable.VariableType.FullName == dustType.FullName)
            ?? throw new InvalidOperationException("Dust.UpdateDust does not contain the verified Dust loop local.");
        var activeField = dustType.Fields.SingleOrDefault(field => field.Name == "active" && field.FieldType.FullName == "System.Boolean")
            ?? throw new InvalidOperationException("Terraria.Dust.active field did not match the verified visual-effects hook.");
        var activeLoad = method.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference field && field.FullName == activeField.FullName)
            ?? throw new InvalidOperationException("Dust.UpdateDust active check was not found.");
        if (!(activeLoad.Next?.Operand is Instruction loopContinue))
            throw new InvalidOperationException("Dust.UpdateDust active branch did not have the verified loop continuation.");
        var insertionPoint = activeLoad.Next.Next ?? throw new InvalidOperationException("Dust.UpdateDust active branch had no body after the continuation.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, LoadLocal(il, dustLocal));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldUpdateDustInstance));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue, insertionPoint));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Br, loopContinue));
    }

    /// <summary>
    /// Skips an individual dust draw only after Terraria has selected the current Dust loop item.
    /// This preserves the whole-system fast path while allowing explicitly permitted exception IDs.
    /// </summary>
    private static void PatchDustDrawInstanceGuard(ModuleDefinition module, MethodDefinition method, MethodReference shouldDrawDustInstance)
    {
        var dustType = CecilPatchPrimitives.RequireType(module, "Terraria.Dust");
        var dustLocal = method.Body.Variables.FirstOrDefault(variable => variable.VariableType.FullName == dustType.FullName)
            ?? throw new InvalidOperationException("DrawDust does not contain the verified Dust loop local.");
        var activeField = dustType.Fields.SingleOrDefault(field => field.Name == "active" && field.FieldType.FullName == "System.Boolean")
            ?? throw new InvalidOperationException("Terraria.Dust.active field did not match the verified draw hook.");
        var activeLoad = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldfld &&
            instruction.Operand is FieldReference field &&
            field.FullName == activeField.FullName)
            ?? throw new InvalidOperationException("DrawDust active check was not found.");
        var activeBranch = activeLoad.Next;
        if (!(activeBranch?.Operand is Instruction loopContinue))
        {
            throw new InvalidOperationException("DrawDust active branch did not have the verified loop continuation.");
        }

        var insertionPoint = activeBranch.Next
            ?? throw new InvalidOperationException("DrawDust active branch had no body after the continuation.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, LoadLocal(il, dustLocal));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawDustInstance));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue, insertionPoint));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Br, loopContinue));
    }

    private static void PatchRenderGate(MethodDefinition method, MethodReference shouldRun)
    {
        PatchVoidReturnGate(method, shouldRun);
    }

    private static void PatchVoidReturnGate(MethodDefinition method, MethodReference shouldRun)
    {
        var first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException(method.FullName + " has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldRun));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchIntReturnGate(MethodDefinition method, MethodReference shouldRun, int disabledReturnValue)
    {
        var first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException(method.FullName + " has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldRun));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, disabledReturnValue));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }
}
