using Mono.Cecil;
using Mono.Cecil.Cil;

// These changes intentionally target only work that is either duplicated within one DoDraw
// invocation or provably inert when its native projectile category is absent. They never change
// the order of draw calls, lighting passes, or background composition.
internal static partial class PermanentPatchPlan
{
    private const string DrawOrchestrationOptimizationEnabledFieldName = "alacrityDrawOrchestrationOptimizationEnabled";
    private const string ShouldSortProjectileCacheMethodName = "AlacrityShouldSortProjectileCache";

    private static void PatchDrawOrchestration(ModuleDefinition module, MethodReference isOptimizationEnabled)
    {
        TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        MethodDefinition doDraw = RequireSingleMethod(main, "DoDraw", 1);
        FieldDefinition optimizationEnabled = AddDrawOrchestrationField(main, module);

        RequireAbsentMethod(main, ShouldSortProjectileCacheMethodName);
        MethodDefinition shouldSortProjectileCache = CreateShouldSortProjectileCacheMethod(module, main);
        main.Methods.Add(shouldSortProjectileCache);

        InsertDrawOrchestrationState(doDraw, isOptimizationEnabled, optimizationEnabled);
        PatchRenderNowLightingArea(doDraw, optimizationEnabled);
        PatchProjectileCacheSort(main, "SortBabyBirdProjectiles", 759, optimizationEnabled, shouldSortProjectileCache);
        PatchProjectileCacheSort(main, "SortStardustDragonProjectiles", 628, optimizationEnabled, shouldSortProjectileCache);
    }

    private static FieldDefinition AddDrawOrchestrationField(TypeDefinition main, ModuleDefinition module)
    {
        for (int index = 0; index < main.Fields.Count; index++)
        {
            if (string.Equals(main.Fields[index].Name, DrawOrchestrationOptimizationEnabledFieldName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 Main already contains the Alacrity draw-orchestration state field.");
            }
        }

        var field = new FieldDefinition(DrawOrchestrationOptimizationEnabledFieldName, FieldAttributes.Private, module.TypeSystem.Boolean);
        main.Fields.Add(field);
        return field;
    }

    private static void InsertDrawOrchestrationState(
        MethodDefinition doDraw,
        MethodReference isOptimizationEnabled,
        FieldDefinition optimizationEnabled)
    {
        Instruction first = doDraw.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Main.DoDraw has no body.");
        ILProcessor il = doDraw.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, isOptimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, optimizationEnabled));
    }

    private static void PatchRenderNowLightingArea(MethodDefinition doDraw, FieldDefinition optimizationEnabled)
    {
        var calls = new List<Instruction>();
        for (int index = 0; index < doDraw.Body.Instructions.Count; index++)
        {
            if (doDraw.Body.Instructions[index].Operand is MethodReference method &&
                string.Equals(method.DeclaringType.FullName, "Terraria.Main", StringComparison.Ordinal) &&
                string.Equals(method.Name, "GetAreaToLight", StringComparison.Ordinal) &&
                method.Parameters.Count == 0 &&
                string.Equals(method.ReturnType.FullName, "Microsoft.Xna.Framework.Rectangle", StringComparison.Ordinal))
            {
                calls.Add(doDraw.Body.Instructions[index]);
            }
        }

        if (calls.Count < 2)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DoDraw no longer contains the verified renderNow lighting-area pair.");
        }

        Instruction firstCall = calls[0];
        Instruction secondCall = calls[1];
        if (firstCall.Next?.Operand is not MethodReference firstLight ||
            secondCall.Next?.Operand is not MethodReference secondLight ||
            !IsLightingTilesCall(firstLight) ||
            !IsLightingTilesCall(secondLight))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DoDraw no longer has the consecutive renderNow Lighting.LightTiles area calls.");
        }

        var cachedArea = new VariableDefinition(doDraw.Module.ImportReference(((MethodReference)firstCall.Operand).ReturnType));
        doDraw.Body.Variables.Add(cachedArea);
        ILProcessor il = doDraw.Body.GetILProcessor();
        il.InsertAfter(firstCall, il.Create(OpCodes.Dup));
        il.InsertAfter(firstCall.Next, StoreLocal(il, cachedArea));

        Instruction reuseArea = il.Create(OpCodes.Ldloc, cachedArea);
        Instruction resume = il.Create(OpCodes.Nop);
        il.InsertBefore(secondCall, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(secondCall, il.Create(OpCodes.Ldfld, optimizationEnabled));
        il.InsertBefore(secondCall, il.Create(OpCodes.Brfalse, secondCall));
        il.InsertBefore(secondCall, reuseArea);
        il.InsertBefore(secondCall, il.Create(OpCodes.Br, resume));
        il.InsertAfter(secondCall, resume);
    }

    private static bool IsLightingTilesCall(MethodReference method)
    {
        return string.Equals(method.DeclaringType.FullName, "Terraria.Lighting", StringComparison.Ordinal) &&
            string.Equals(method.Name, "LightTiles", StringComparison.Ordinal) &&
            method.Parameters.Count == 1 &&
            string.Equals(method.Parameters[0].ParameterType.FullName, "Microsoft.Xna.Framework.Rectangle", StringComparison.Ordinal);
    }

    private static void PatchProjectileCacheSort(
        TypeDefinition main,
        string methodName,
        int projectileType,
        FieldDefinition optimizationEnabled,
        MethodDefinition shouldSortProjectileCache)
    {
        MethodDefinition method = RequireSingleMethod(main, methodName, 1);
        if (!string.Equals(method.Parameters[0].ParameterType.FullName, "System.Collections.Generic.List`1<System.Int32>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main." + methodName + " no longer has the verified List<Int32> parameter.");
        }

        Instruction first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Main." + methodName + " has no body.");
        ILProcessor il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, projectileType));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldfld, optimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldSortProjectileCache));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static MethodDefinition CreateShouldSortProjectileCacheMethod(ModuleDefinition module, TypeDefinition main)
    {
        FieldDefinition projectiles = RequireField(main, "projectile", "Terraria.Projectile[]");
        TypeDefinition projectile = CecilPatchPrimitives.RequireType(module, "Terraria.Projectile");
        FieldDefinition type = RequireField(projectile, "type", "System.Int32");
        // Reuse Terraria's exact generic List<Int32> metadata. Creating these method
        // references manually is unsafe: List<T>.get_Item returns !0 in the target metadata,
        // not a concrete Int32, and the CLR rejects the altered signature during ForceJIT.
        MethodDefinition nativeSort = RequireSingleMethod(main, "SortBabyBirdProjectiles", 1);
        TypeReference listOfInt = module.ImportReference(nativeSort.Parameters[0].ParameterType);
        MethodReference getCount = RequireListAccessor(nativeSort, "get_Count", 0);
        MethodReference getItem = RequireListAccessor(nativeSort, "get_Item", 1);
        var method = new MethodDefinition(
            ShouldSortProjectileCacheMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition("entries", ParameterAttributes.None, listOfInt));
        method.Parameters.Add(new ParameterDefinition("projectileType", ParameterAttributes.None, module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition("enabled", ParameterAttributes.None, module.TypeSystem.Boolean));
        method.Body.InitLocals = true;
        var index = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(index);

        Instruction scan = Instruction.Create(OpCodes.Nop);
        Instruction check = Instruction.Create(OpCodes.Nop);
        Instruction found = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction keepNative = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction noMatch = Instruction.Create(OpCodes.Ldc_I4_0);
        ILProcessor il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Brtrue, scan));
        il.Append(keepNative);
        il.Append(il.Create(OpCodes.Ret));
        il.Append(scan);
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(StoreLocal(il, index));
        il.Append(check);
        il.Append(LoadLocal(il, index));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, getCount));
        il.Append(il.Create(OpCodes.Bge, noMatch));
        il.Append(il.Create(OpCodes.Ldsfld, projectiles));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(LoadLocal(il, index));
        il.Append(il.Create(OpCodes.Callvirt, getItem));
        il.Append(il.Create(OpCodes.Ldelem_Ref));
        il.Append(il.Create(OpCodes.Ldfld, type));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Beq, found));
        il.Append(LoadLocal(il, index));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(StoreLocal(il, index));
        il.Append(il.Create(OpCodes.Br, check));
        il.Append(found);
        il.Append(il.Create(OpCodes.Ret));
        il.Append(noMatch);
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static MethodReference RequireListAccessor(MethodDefinition method, string name, int parameterCount)
    {
        MethodReference? match = null;

        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (instruction.Operand is not MethodReference candidate ||
                !string.Equals(candidate.Name, name, StringComparison.Ordinal) ||
                candidate.Parameters.Count != parameterCount ||
                !string.Equals(candidate.DeclaringType.FullName, "System.Collections.Generic.List`1<System.Int32>", StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException(
                    "Terraria 1.4.5.6 Main." + method.Name + " has ambiguous List<Int32>." + name + " accessor calls.");
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException(
            "Terraria 1.4.5.6 Main." + method.Name + " no longer contains a verified List<Int32>." + name + " accessor call.");
    }

}
