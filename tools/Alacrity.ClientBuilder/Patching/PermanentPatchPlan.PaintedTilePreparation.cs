using Mono.Cecil;
using Mono.Cecil.Cil;

// Version-locked structural reductions for vanilla's synchronous painted-tile preparation path.
internal static partial class PermanentPatchPlan
{
    private const string PendingPaintRequestFieldName = "alacrityPendingPaintPreparation";
    private const string PaintPreparationEnabledFieldName = "alacrityPaintPreparationOptimizationEnabled";
    private const string TryMarkPendingMethodName = "TryMarkAlacrityPaintPreparationPending";
    private const string ClearPendingMethodName = "ClearAlacrityPaintPreparationPending";

    private static void PatchPaintedTilePreparation(
        ModuleDefinition module,
        MethodReference isOptimizationEnabled,
        MethodReference isExtraPreparationRelevant)
    {
        TypeDefinition paintSystem = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.TilePaintSystemV2");
        TypeDefinition holder = FindNestedType(paintSystem, "ARenderTargetHolder");
        FieldDefinition pendingField = AddPendingPaintRequestField(holder, module);
        MethodDefinition tryMarkPending = AddTryMarkPendingMethod(holder, pendingField, module);
        MethodDefinition clearPending = AddClearPendingMethod(holder, pendingField, module);

        PatchPaintRequestQueue(paintSystem, "RequestTile", tryMarkPending, isOptimizationEnabled);
        PatchPaintRequestQueue(paintSystem, "RequestCageTop", tryMarkPending, isOptimizationEnabled);
        PatchPaintRequestQueue(paintSystem, "RequestWall", tryMarkPending, isOptimizationEnabled);
        PatchPaintRequestQueue(paintSystem, "RequestTreeTop", tryMarkPending, isOptimizationEnabled);
        PatchPaintRequestQueue(paintSystem, "RequestTreeBranch", tryMarkPending, isOptimizationEnabled);
        PatchPaintRequestPreparation(paintSystem, clearPending);
        PatchPaintHolderClear(holder, clearPending);
        PatchExtraPaintPreparationPrefilter(module, isExtraPreparationRelevant);
        PatchLazyUnpaintedPreparation(module, isOptimizationEnabled);
    }

    private static TypeDefinition FindNestedType(TypeDefinition parent, string name)
    {
        for (int index = 0; index < parent.NestedTypes.Count; index++)
        {
            TypeDefinition candidate = parent.NestedTypes[index];
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2." + name + " was not found.");
    }

    private static FieldDefinition AddPendingPaintRequestField(TypeDefinition holder, ModuleDefinition module)
    {
        for (int index = 0; index < holder.Fields.Count; index++)
        {
            if (string.Equals(holder.Fields[index].Name, PendingPaintRequestFieldName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 paint holder already contains the Alacrity pending-request field.");
            }
        }

        var field = new FieldDefinition(PendingPaintRequestFieldName, FieldAttributes.Private, module.TypeSystem.Boolean);
        holder.Fields.Add(field);
        return field;
    }

    private static MethodDefinition AddTryMarkPendingMethod(TypeDefinition holder, FieldDefinition pendingField, ModuleDefinition module)
    {
        RequireAbsentMethod(holder, TryMarkPendingMethodName);
        var method = new MethodDefinition(
            TryMarkPendingMethodName,
            MethodAttributes.Public | MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        Instruction alreadyPending = Instruction.Create(OpCodes.Ldc_I4_0);
        ILProcessor il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, pendingField));
        il.Append(il.Create(OpCodes.Brtrue, alreadyPending));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stfld, pendingField));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(alreadyPending);
        il.Append(il.Create(OpCodes.Ret));
        holder.Methods.Add(method);
        return method;
    }

    private static MethodDefinition AddClearPendingMethod(TypeDefinition holder, FieldDefinition pendingField, ModuleDefinition module)
    {
        RequireAbsentMethod(holder, ClearPendingMethodName);
        var method = new MethodDefinition(
            ClearPendingMethodName,
            MethodAttributes.Public | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        ILProcessor il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stfld, pendingField));
        il.Append(il.Create(OpCodes.Ret));
        holder.Methods.Add(method);
        return method;
    }

    private static void PatchPaintRequestQueue(
        TypeDefinition paintSystem,
        string methodName,
        MethodReference tryMarkPending,
        MethodReference isOptimizationEnabled)
    {
        MethodDefinition method = RequireSingleMethod(paintSystem, methodName, 1);
        if (!method.HasBody)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2." + methodName + " has no body.");
        }

        Instruction readyCall = FindUniqueMethodCall(method, "get_IsReady", "System.Boolean");
        Instruction readyBranch = readyCall.Next;
        if (readyBranch == null ||
            (readyBranch.OpCode != OpCodes.Brfalse && readyBranch.OpCode != OpCodes.Brfalse_S) ||
            !(readyBranch.Operand is Instruction queueStart))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2." + methodName + " no longer has the verified unready-holder branch.");
        }

        Instruction skipQueue = readyBranch.Next
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2." + methodName + " has no ready-holder return.");

        VariableDefinition holderLocal = GetLoadedLocalVariable(method, readyCall.Previous)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2." + methodName + " did not load its holder local before IsReady.");
        ILProcessor il = method.Body.GetILProcessor();

        Instruction optimizationGate = il.Create(OpCodes.Call, isOptimizationEnabled);
        il.InsertBefore(queueStart, optimizationGate);
        il.InsertBefore(queueStart, il.Create(OpCodes.Brfalse, queueStart));
        il.InsertBefore(queueStart, LoadLocal(il, holderLocal));
        il.InsertBefore(queueStart, il.Create(OpCodes.Callvirt, tryMarkPending));
        il.InsertBefore(queueStart, il.Create(OpCodes.Brfalse, skipQueue));
        readyBranch.Operand = optimizationGate;
    }

    private static void PatchPaintRequestPreparation(TypeDefinition paintSystem, MethodReference clearPending)
    {
        MethodDefinition method = RequireSingleMethod(paintSystem, "PrepareAllRequests", 0);
        if (!method.HasBody)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2.PrepareAllRequests has no body.");
        }

        Instruction prepareCall = FindUniqueMethodCall(method, "Prepare", "System.Void");
        Instruction getItem = FindPreviousMethodCall(prepareCall, "get_Item", null)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2.PrepareAllRequests did not retrieve a pending holder from the request list.");
        VariableDefinition indexLocal = GetLoadedLocalVariable(method, getItem.Previous)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2.PrepareAllRequests did not load its request index before List.get_Item.");
        FieldDefinition requestsField = RequireField(paintSystem, "_requests", "System.Collections.Generic.List`1<Terraria.GameContent.TilePaintSystemV2/ARenderTargetHolder>");
        MethodReference getItemReference = (MethodReference)getItem.Operand;
        ILProcessor il = method.Body.GetILProcessor();

        il.InsertBefore(getItem, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(getItem, il.Create(OpCodes.Ldfld, requestsField));
        il.InsertBefore(getItem, LoadLocal(il, indexLocal));
        il.InsertBefore(getItem, il.Create(OpCodes.Callvirt, getItemReference));
        il.InsertBefore(getItem, il.Create(OpCodes.Callvirt, clearPending));
    }

    private static void PatchPaintHolderClear(TypeDefinition holder, MethodReference clearPending)
    {
        MethodDefinition clear = RequireSingleMethod(holder, "Clear", 0);
        Instruction first = clear.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TilePaintSystemV2.ARenderTargetHolder.Clear has no body.");
        ILProcessor il = clear.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, clearPending));
    }

    private static void PatchExtraPaintPreparationPrefilter(ModuleDefinition module, MethodReference isExtraPreparationRelevant)
    {
        TypeDefinition tileDrawing = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.Drawing.TileDrawing");
        MethodDefinition method = RequireSingleMethod(tileDrawing, "MakeExtraPreparations", 3);
        TypeDefinition tileType = CecilPatchPrimitives.RequireType(module, "Terraria.Tile");
        FieldDefinition typeField = RequireField(tileType, "type", "System.UInt16");
        Instruction first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.MakeExtraPreparations has no body.");
        ILProcessor il = method.Body.GetILProcessor();

        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Ldfld, module.ImportReference(typeField)));
        il.InsertBefore(first, il.Create(OpCodes.Call, isExtraPreparationRelevant));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchLazyUnpaintedPreparation(ModuleDefinition module, MethodReference isOptimizationEnabled)
    {
        TypeDefinition tileDrawing = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.Drawing.TileDrawing");
        MethodDefinition method = RequireSingleMethod(tileDrawing, "PrepareForAreaDrawing", 5);
        TypeDefinition tileType = CecilPatchPrimitives.RequireType(module, "Terraria.Tile");
        FieldDefinition enabledField = AddPaintPreparationEnabledField(tileDrawing, module);
        Instruction first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PrepareForAreaDrawing has no body.");
        Instruction loadTiles = FindUniqueMethodCall(method, "LoadTiles", "System.Void");
        Instruction loadWall = FindUniqueMethodCall(method, "LoadWall", "System.Void");
        Instruction loadTilesStart = RequireInstanceCallStart(loadTiles, "LoadTiles");
        Instruction loadWallStart = RequireInstanceCallStart(loadWall, "LoadWall");
        Instruction requestWall = FindUniqueMethodCall(method, "RequestWall", "System.Void");
        Instruction wallBlock = FindFirstFieldLoadAfter(method, loadTiles, tileType.FullName, "wall");
        Instruction wallBlockStart = wallBlock.Previous
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PrepareForAreaDrawing does not load the tile before its wall branch.");
        Instruction afterWallBlock = requestWall.Next
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PrepareForAreaDrawing has no continuation after RequestWall.");
        Instruction colorCall = FindUniqueMethodCall(method, "color", "System.Byte");
        Instruction wallColorCall = FindUniqueMethodCall(method, "wallColor", "System.Byte");
        VariableDefinition tileForColor = GetLoadedLocalVariable(method, colorCall.Previous)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PrepareForAreaDrawing did not load the tile before color.");
        VariableDefinition tileForWallColor = GetLoadedLocalVariable(method, wallColorCall.Previous)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PrepareForAreaDrawing did not load the tile before wallColor.");
        ParameterDefinition prepareLazily = method.Parameters[4];
        ILProcessor il = method.Body.GetILProcessor();

        // This flag is captured once per scan. The native calls remain available when no
        // rendering-optimization policy is active, so the patch fails back to vanilla behavior.
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, isOptimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, enabledField));

        // In the six-tick lazy scan, unpainted active tiles cannot create a paint request. Avoid
        // their asset request and tree/palm style walk; normal tile drawing still loads assets.
        InsertSkipWhenUnpainted(
            il,
            loadTilesStart,
            enabledField,
            prepareLazily,
            tileForColor,
            colorCall,
            wallBlockStart);

        // Walls follow the same rule. Jump to the existing post-wall continuation so the native
        // non-lazy MakeExtraPreparations branch remains untouched.
        InsertSkipWhenUnpainted(
            il,
            loadWallStart,
            enabledField,
            prepareLazily,
            tileForWallColor,
            wallColorCall,
            afterWallBlock);
    }

    private static FieldDefinition AddPaintPreparationEnabledField(TypeDefinition tileDrawing, ModuleDefinition module)
    {
        for (int index = 0; index < tileDrawing.Fields.Count; index++)
        {
            if (string.Equals(tileDrawing.Fields[index].Name, PaintPreparationEnabledFieldName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing already contains the Alacrity paint-preparation state field.");
            }
        }

        var field = new FieldDefinition(PaintPreparationEnabledFieldName, FieldAttributes.Private, module.TypeSystem.Boolean);
        tileDrawing.Fields.Add(field);
        return field;
    }

    private static void InsertSkipWhenUnpainted(
        ILProcessor il,
        Instruction nativeStart,
        FieldDefinition enabledField,
        ParameterDefinition prepareLazily,
        VariableDefinition tile,
        Instruction colorCall,
        Instruction skipTarget)
    {
        il.InsertBefore(nativeStart, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Ldfld, enabledField));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Brfalse, nativeStart));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Ldarg, prepareLazily));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Brfalse, nativeStart));
        il.InsertBefore(nativeStart, LoadLocal(il, tile));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Callvirt, colorCall.Operand as MethodReference
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing paint color accessor was not a method.")));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Brtrue, nativeStart));
        il.InsertBefore(nativeStart, il.Create(OpCodes.Br, skipTarget));
    }

    private static Instruction RequireInstanceCallStart(Instruction call, string methodName)
    {
        Instruction? valueLoad = call.Previous;
        Instruction? tileLoad = valueLoad?.Previous;
        Instruction? receiverLoad = tileLoad?.Previous;
        if (valueLoad == null || tileLoad == null || receiverLoad == null || receiverLoad.OpCode != OpCodes.Ldsfld)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PrepareForAreaDrawing no longer has the verified receiver/value stack shape for Main." + methodName + ".");
        }

        return receiverLoad;
    }

    private static MethodDefinition RequireSingleMethod(TypeDefinition type, string name, int parameterCount)
    {
        MethodDefinition? match = null;
        for (int index = 0; index < type.Methods.Count; index++)
        {
            MethodDefinition candidate = type.Methods[index];
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal) || candidate.Parameters.Count != parameterCount)
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + "." + name + " resolves ambiguously.");
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + "." + name + " did not match the verified signature.");
    }

    private static void RequireAbsentMethod(TypeDefinition type, string name)
    {
        for (int index = 0; index < type.Methods.Count; index++)
        {
            if (string.Equals(type.Methods[index].Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + " already contains the Alacrity pending-request helper.");
            }
        }
    }

    private static FieldDefinition RequireField(TypeDefinition type, string name, string fullTypeName)
    {
        for (int index = 0; index < type.Fields.Count; index++)
        {
            FieldDefinition candidate = type.Fields[index];
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
                string.Equals(candidate.FieldType.FullName, fullTypeName, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + "." + name + " did not match the verified field type.");
    }

    private static Instruction FindUniqueMethodCall(MethodDefinition method, string name, string returnType)
    {
        Instruction? match = null;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            Instruction candidate = method.Body.Instructions[index];
            if (!(candidate.Operand is MethodReference reference) ||
                !string.Equals(reference.Name, name, StringComparison.Ordinal) ||
                !string.Equals(reference.ReturnType.FullName, returnType, StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " contains multiple " + name + " calls.");
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " does not contain the verified " + name + " call.");
    }

    private static Instruction FindFirstFieldLoadAfter(MethodDefinition method, Instruction start, string declaringType, string name)
    {
        bool foundStart = false;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            Instruction candidate = method.Body.Instructions[index];
            if (ReferenceEquals(candidate, start))
            {
                foundStart = true;
                continue;
            }

            if (!foundStart)
            {
                continue;
            }

            if (!(candidate.Operand is FieldReference field) ||
                candidate.OpCode != OpCodes.Ldfld ||
                !string.Equals(field.DeclaringType.FullName, declaringType, StringComparison.Ordinal) ||
                !string.Equals(field.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            return candidate;
        }

        throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " does not contain the verified " + declaringType + "." + name + " field load after " + start + ".");
    }

    private static Instruction? FindPreviousMethodCall(Instruction start, string name, string? returnType)
    {
        for (Instruction current = start.Previous; current != null; current = current.Previous)
        {
            if (current.Operand is MethodReference reference &&
                string.Equals(reference.Name, name, StringComparison.Ordinal) &&
                (returnType == null || string.Equals(reference.ReturnType.FullName, returnType, StringComparison.Ordinal)))
            {
                return current;
            }
        }

        return null;
    }
}
