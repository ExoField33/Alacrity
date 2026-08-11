using Mono.Cecil;
using Mono.Cecil.Cil;

// Version-locked clothing-entity presentation reduction. The visible dictionaries are rebuilt
// every solid-tile draw, so their stored entity IDs are the only safe cache boundary.
internal static partial class PermanentPatchPlan
{
    // A full 1080p viewport can legitimately discover hundreds of clothing entities. These
    // dictionaries are cleared, not replaced, each draw, so reserving this capacity once avoids
    // repeated allocation and rehashing on the first dense room the player enters.
    private const int ClothingEntityInitialCapacity = 2048;

    private const string HatRackDrawMethodName = "DrawEntities_AlacrityHatRacks";
    private const string DisplayDollDrawMethodName = "DrawEntities_AlacrityDisplayDolls";
    private const string HatRackEntryDrawMethodName = "DrawEntities_AlacrityHatRackEntries";
    private const string DisplayDollEntryDrawMethodName = "DrawEntities_AlacrityDisplayDollEntries";
    private const string ClothingOptimizationEnabledFieldName = "alacrityClothingEntityPresentationOptimizationEnabled";
    private const string DisplayDollLastPointValidFieldName = "alacrityDisplayDollLastPointValid";
    private const string DisplayDollLastPointXFieldName = "alacrityDisplayDollLastPointX";
    private const string DisplayDollLastPointYFieldName = "alacrityDisplayDollLastPointY";
    private const string HatRackLastPointValidFieldName = "alacrityHatRackLastPointValid";
    private const string HatRackLastPointXFieldName = "alacrityHatRackLastPointX";
    private const string HatRackLastPointYFieldName = "alacrityHatRackLastPointY";

    private static void PatchClothingEntityPresentation(ModuleDefinition module, MethodReference isOptimizationEnabled)
    {
        TypeDefinition tileDrawing = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.Drawing.TileDrawing");
        MethodDefinition drawHatRacks = RequireSingleMethod(tileDrawing, "DrawEntities_HatRacks", 0);
        MethodDefinition drawDisplayDolls = RequireSingleMethod(tileDrawing, "DrawEntities_DisplayDolls", 0);
        MethodDefinition postDrawTiles = RequireSingleMethod(tileDrawing, "PostDrawTiles", 1);
        MethodDefinition clearCachedTileDraws = RequireSingleMethod(tileDrawing, "ClearCachedTileDraws", 1);
        MethodDefinition cacheSpecialDraws = RequireSingleMethod(tileDrawing, "CacheSpecialDraws_Part1", 6);
        FieldDefinition hatRackPositions = RequireField(tileDrawing, "_hatRackTileEntityPositions", "System.Collections.Generic.Dictionary`2<Microsoft.Xna.Framework.Point,System.Int32>");
        FieldDefinition displayDollPositions = RequireField(tileDrawing, "_displayDollTileEntityPositions", "System.Collections.Generic.Dictionary`2<Microsoft.Xna.Framework.Point,System.Int32>");
        ClothingDiscoveryState discoveryState = AddClothingDiscoveryState(tileDrawing, module);

        PatchInitialDictionaryCapacity(tileDrawing, hatRackPositions);
        PatchInitialDictionaryCapacity(tileDrawing, displayDollPositions);
        PatchClothingDiscoveryStateCapture(clearCachedTileDraws, discoveryState, isOptimizationEnabled);
        PatchRepeatedClothingDiscovery(cacheSpecialDraws, displayDollPositions, discoveryState, true);
        PatchRepeatedClothingDiscovery(cacheSpecialDraws, hatRackPositions, discoveryState, false);

        RequireAbsentMethod(tileDrawing, HatRackDrawMethodName);
        RequireAbsentMethod(tileDrawing, DisplayDollDrawMethodName);
        RequireAbsentMethod(tileDrawing, HatRackEntryDrawMethodName);
        RequireAbsentMethod(tileDrawing, DisplayDollEntryDrawMethodName);

        MethodDefinition drawHatRackEntries = CreateEntryDrawMethod(
            module,
            drawHatRacks,
            hatRackPositions,
            RequireHatRackContentCheck(module),
            HatRackEntryDrawMethodName);
        MethodDefinition drawDisplayDollEntries = CreateEntryDrawMethod(
            module,
            drawDisplayDolls,
            displayDollPositions,
            null,
            DisplayDollEntryDrawMethodName);
        MethodDefinition optimizedHatRacks = CreateOptimizedDrawMethod(
            module,
            tileDrawing,
            drawHatRacks,
            drawHatRackEntries,
            hatRackPositions,
            HatRackDrawMethodName);
        MethodDefinition optimizedDisplayDolls = CreateOptimizedDrawMethod(
            module,
            tileDrawing,
            drawDisplayDolls,
            drawDisplayDollEntries,
            displayDollPositions,
            DisplayDollDrawMethodName);

        tileDrawing.Methods.Add(drawHatRackEntries);
        tileDrawing.Methods.Add(drawDisplayDollEntries);
        tileDrawing.Methods.Add(optimizedHatRacks);
        tileDrawing.Methods.Add(optimizedDisplayDolls);
        ReplacePostDrawCalls(
            postDrawTiles,
            drawHatRacks,
            drawDisplayDolls,
            optimizedHatRacks,
            optimizedDisplayDolls,
            isOptimizationEnabled);
    }

    private static ClothingDiscoveryState AddClothingDiscoveryState(TypeDefinition tileDrawing, ModuleDefinition module)
    {
        return new ClothingDiscoveryState(
            AddPrivateBooleanField(tileDrawing, ClothingOptimizationEnabledFieldName, module),
            AddPrivateBooleanField(tileDrawing, DisplayDollLastPointValidFieldName, module),
            AddPrivateIntegerField(tileDrawing, DisplayDollLastPointXFieldName, module),
            AddPrivateIntegerField(tileDrawing, DisplayDollLastPointYFieldName, module),
            AddPrivateBooleanField(tileDrawing, HatRackLastPointValidFieldName, module),
            AddPrivateIntegerField(tileDrawing, HatRackLastPointXFieldName, module),
            AddPrivateIntegerField(tileDrawing, HatRackLastPointYFieldName, module));
    }

    private static FieldDefinition AddPrivateBooleanField(TypeDefinition type, string name, ModuleDefinition module)
    {
        return AddPrivateField(type, name, module.TypeSystem.Boolean, module);
    }

    private static FieldDefinition AddPrivateIntegerField(TypeDefinition type, string name, ModuleDefinition module)
    {
        return AddPrivateField(type, name, module.TypeSystem.Int32, module);
    }

    private static FieldDefinition AddPrivateField(TypeDefinition type, string name, TypeReference fieldType, ModuleDefinition module)
    {
        for (int index = 0; index < type.Fields.Count; index++)
        {
            if (string.Equals(type.Fields[index].Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + " already contains the Alacrity clothing-discovery field " + name + ".");
            }
        }

        var field = new FieldDefinition(name, FieldAttributes.Private, module.ImportReference(fieldType));
        type.Fields.Add(field);
        return field;
    }

    private static void PatchClothingDiscoveryStateCapture(
        MethodDefinition clearCachedTileDraws,
        ClothingDiscoveryState state,
        MethodReference isOptimizationEnabled)
    {
        Instruction first = clearCachedTileDraws.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.ClearCachedTileDraws has no body.");
        ParameterDefinition solidLayer = clearCachedTileDraws.Parameters[0];
        ILProcessor il = clearCachedTileDraws.Body.GetILProcessor();

        // These dictionaries are used only for the solid layer. Capture the policy and reset the
        // per-draw duplicate markers before Terraria begins repopulating either dictionary.
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, solidLayer));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, isOptimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, state.Enabled));
        ResetDiscoveryMarker(il, first, state.DisplayDollLastPointValid);
        ResetDiscoveryMarker(il, first, state.HatRackLastPointValid);
    }

    private static void ResetDiscoveryMarker(ILProcessor il, Instruction insertionPoint, FieldDefinition marker)
    {
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Stfld, marker));
    }

    private static void PatchRepeatedClothingDiscovery(
        MethodDefinition method,
        FieldDefinition positions,
        ClothingDiscoveryState state,
        bool displayDoll)
    {
        Instruction containsKey = FindPositionsContainsKeyCall(method, positions);
        Instruction branchWhenKnown = containsKey.Next;
        if (branchWhenKnown == null ||
            (branchWhenKnown.OpCode != OpCodes.Brtrue && branchWhenKnown.OpCode != OpCodes.Brtrue_S) ||
            !(branchWhenKnown.Operand is Instruction afterNativeDiscovery))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " no longer has the verified clothing dictionary ContainsKey branch for " + positions.Name + ".");
        }

        PromoteShortBranchesTargeting(method, afterNativeDiscovery);

        VariableDefinition point = GetLoadedLocalVariable(method, containsKey.Previous)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " did not load the clothing position before " + positions.Name + ".ContainsKey.");
        Instruction nativeLookupStart = containsKey.Previous?.Previous?.Previous
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " did not load the clothing dictionary before " + positions.Name + ".ContainsKey.");
        if (nativeLookupStart.OpCode != OpCodes.Ldarg_0)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " no longer has the verified receiver start for " + positions.Name + ".ContainsKey.");
        }
        FieldReference pointX = RequirePointField(method, "X");
        FieldReference pointY = RequirePointField(method, "Y");
        FieldDefinition valid = displayDoll ? state.DisplayDollLastPointValid : state.HatRackLastPointValid;
        FieldDefinition lastX = displayDoll ? state.DisplayDollLastPointX : state.HatRackLastPointX;
        FieldDefinition lastY = displayDoll ? state.DisplayDollLastPointY : state.HatRackLastPointY;
        ILProcessor il = method.Body.GetILProcessor();
        Instruction nativeLookup = il.Create(OpCodes.Nop);

        // A multi-tile display doll or hat rack presents the identical top-left point for every
        // visible segment. Vanilla hashes that point for each segment. With the policy enabled,
        // bypass the repeated dictionary lookup while preserving the first native lookup exactly.
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldfld, state.Enabled));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Brfalse, nativeLookup));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldfld, valid));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Brfalse, nativeLookup));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldfld, lastX));
        il.InsertBefore(nativeLookupStart, LoadLocal(il, point));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldfld, pointX));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Bne_Un, nativeLookup));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldfld, lastY));
        il.InsertBefore(nativeLookupStart, LoadLocal(il, point));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Ldfld, pointY));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Bne_Un, nativeLookup));
        il.InsertBefore(nativeLookupStart, il.Create(OpCodes.Br, afterNativeDiscovery));
        il.InsertBefore(nativeLookupStart, nativeLookup);
        StoreDiscoveryPoint(il, nativeLookupStart, point, pointX, pointY, valid, lastX, lastY);
    }

    private static void PromoteShortBranchesTargeting(MethodDefinition method, Instruction target)
    {
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            Instruction instruction = method.Body.Instructions[index];
            if (!ReferenceEquals(instruction.Operand, target))
            {
                continue;
            }

            instruction.OpCode = instruction.OpCode.Code switch
            {
                Code.Br_S => OpCodes.Br,
                Code.Brfalse_S => OpCodes.Brfalse,
                Code.Brtrue_S => OpCodes.Brtrue,
                Code.Beq_S => OpCodes.Beq,
                Code.Bge_S => OpCodes.Bge,
                Code.Bge_Un_S => OpCodes.Bge_Un,
                Code.Bgt_S => OpCodes.Bgt,
                Code.Bgt_Un_S => OpCodes.Bgt_Un,
                Code.Ble_S => OpCodes.Ble,
                Code.Ble_Un_S => OpCodes.Ble_Un,
                Code.Blt_S => OpCodes.Blt,
                Code.Blt_Un_S => OpCodes.Blt_Un,
                Code.Bne_Un_S => OpCodes.Bne_Un,
                _ => instruction.OpCode
            };
        }
    }

    private static void StoreDiscoveryPoint(
        ILProcessor il,
        Instruction insertionPoint,
        VariableDefinition point,
        FieldReference pointX,
        FieldReference pointY,
        FieldDefinition valid,
        FieldDefinition lastX,
        FieldDefinition lastY)
    {
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Stfld, valid));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertionPoint, LoadLocal(il, point));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldfld, pointX));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Stfld, lastX));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertionPoint, LoadLocal(il, point));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldfld, pointY));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Stfld, lastY));
    }

    private static Instruction FindPositionsContainsKeyCall(MethodDefinition method, FieldDefinition positions)
    {
        Instruction? match = null;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            Instruction candidate = method.Body.Instructions[index];
            if (!(candidate.Operand is MethodReference reference) ||
                !string.Equals(reference.Name, "ContainsKey", StringComparison.Ordinal) ||
                candidate.Previous?.Previous?.Operand is not FieldReference field ||
                !string.Equals(field.FullName, positions.FullName, StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " has multiple verified ContainsKey calls for " + positions.Name + ".");
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " does not have the verified ContainsKey call for " + positions.Name + ".");
    }

    private sealed class ClothingDiscoveryState
    {
        internal ClothingDiscoveryState(
            FieldDefinition enabled,
            FieldDefinition displayDollLastPointValid,
            FieldDefinition displayDollLastPointX,
            FieldDefinition displayDollLastPointY,
            FieldDefinition hatRackLastPointValid,
            FieldDefinition hatRackLastPointX,
            FieldDefinition hatRackLastPointY)
        {
            Enabled = enabled;
            DisplayDollLastPointValid = displayDollLastPointValid;
            DisplayDollLastPointX = displayDollLastPointX;
            DisplayDollLastPointY = displayDollLastPointY;
            HatRackLastPointValid = hatRackLastPointValid;
            HatRackLastPointX = hatRackLastPointX;
            HatRackLastPointY = hatRackLastPointY;
        }

        internal FieldDefinition Enabled { get; }

        internal FieldDefinition DisplayDollLastPointValid { get; }

        internal FieldDefinition DisplayDollLastPointX { get; }

        internal FieldDefinition DisplayDollLastPointY { get; }

        internal FieldDefinition HatRackLastPointValid { get; }

        internal FieldDefinition HatRackLastPointX { get; }

        internal FieldDefinition HatRackLastPointY { get; }
    }

    private static void PatchInitialDictionaryCapacity(TypeDefinition tileDrawing, FieldDefinition positions)
    {
        MethodDefinition constructor = RequireSingleMethod(tileDrawing, ".ctor", 1);
        Instruction? assignment = null;
        for (int index = 0; index < constructor.Body.Instructions.Count; index++)
        {
            Instruction candidate = constructor.Body.Instructions[index];
            if (candidate.OpCode == OpCodes.Stfld &&
                candidate.Operand is FieldReference field &&
                string.Equals(field.FullName, positions.FullName, StringComparison.Ordinal))
            {
                if (assignment != null)
                {
                    throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing initializes " + positions.Name + " more than once.");
                }

                assignment = candidate;
            }
        }

        if (assignment == null || assignment.Previous == null || assignment.Previous.OpCode != OpCodes.Newobj ||
            !(assignment.Previous.Operand is MethodReference vanillaConstructor) || vanillaConstructor.Parameters.Count != 0)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing does not use the verified default Dictionary constructor for " + positions.Name + ".");
        }

        var capacityConstructor = new MethodReference(
            ".ctor",
            tileDrawing.Module.TypeSystem.Void,
            tileDrawing.Module.ImportReference(positions.FieldType))
        {
            HasThis = true
        };
        capacityConstructor.Parameters.Add(new ParameterDefinition(tileDrawing.Module.TypeSystem.Int32));

        ILProcessor il = constructor.Body.GetILProcessor();
        il.InsertBefore(assignment.Previous, il.Create(OpCodes.Ldc_I4, ClothingEntityInitialCapacity));
        assignment.Previous.Operand = capacityConstructor;
    }

    private static MethodReference RequireHatRackContentCheck(ModuleDefinition module)
    {
        TypeDefinition definition = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.Tile_Entities.TEHatRack");
        MethodDefinition? match = null;
        for (int index = 0; index < definition.Methods.Count; index++)
        {
            MethodDefinition candidate = definition.Methods[index];
            if (!string.Equals(candidate.Name, "ContainsItems", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 0 ||
                !string.Equals(candidate.ReturnType.FullName, "System.Boolean", StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 TEHatRack.ContainsItems resolves ambiguously.");
            }

            match = candidate;
        }

        return match == null
            ? throw new InvalidOperationException("Terraria 1.4.5.6 TEHatRack.ContainsItems was not found.")
            : module.ImportReference(match);
    }

    private static MethodDefinition CreateEntryDrawMethod(
        ModuleDefinition module,
        MethodDefinition vanillaMethod,
        FieldDefinition positions,
        MethodReference? contentCheck,
        string name)
    {
        MethodReference getEnumerator = RequireCall(vanillaMethod, "GetEnumerator", null);
        MethodReference getCurrent = RequireCall(vanillaMethod, "get_Current", null);
        // Cecil exposes the original Dictionary<TKey, TValue> accessors with !0/!1 return
        // tokens. Their unique call positions in the verified vanilla body establish the shape.
        MethodReference getValue = RequireCall(vanillaMethod, "get_Value", null);
        MethodReference getKey = RequireCall(vanillaMethod, "get_Key", null);
        MethodReference moveNext = RequireCall(vanillaMethod, "MoveNext", "System.Boolean");
        MethodReference draw = RequireCall(vanillaMethod, "Draw", "System.Void");
        FieldReference pointX = RequirePointField(vanillaMethod, "X");
        FieldReference pointY = RequirePointField(vanillaMethod, "Y");
        MethodReference tryGet = CreateEntityIdLookup(module, draw.DeclaringType);
        if (!(positions.FieldType is GenericInstanceType dictionaryType) || dictionaryType.GenericArguments.Count != 2)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 clothing-entity position storage is not the verified Dictionary<Point, Int32> shape.");
        }

        TypeReference positionType = module.ImportReference(dictionaryType.GenericArguments[0]);
        TypeReference valueType = module.ImportReference(dictionaryType.GenericArguments[1]);
        TypeReference enumeratorType = CloseGenericType(module, getEnumerator.ReturnType, positionType, valueType);
        TypeReference pairType = CloseGenericType(module, getCurrent.ReturnType, positionType, valueType);
        var method = new MethodDefinition(
            name,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        var enumerator = new VariableDefinition(enumeratorType);
        var pair = new VariableDefinition(pairType);
        var id = new VariableDefinition(module.TypeSystem.Int32);
        var entity = new VariableDefinition(module.ImportReference(draw.DeclaringType));
        var position = new VariableDefinition(positionType);
        method.Body.Variables.Add(enumerator);
        method.Body.Variables.Add(pair);
        method.Body.Variables.Add(id);
        method.Body.Variables.Add(entity);
        method.Body.Variables.Add(position);
        ILProcessor il = method.Body.GetILProcessor();
        Instruction loopBody = il.Create(OpCodes.Nop);
        Instruction loopCheck = il.Create(OpCodes.Nop);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, positions));
        il.Append(il.Create(OpCodes.Callvirt, getEnumerator));
        il.Append(StoreLocal(il, enumerator));
        il.Append(il.Create(OpCodes.Br, loopCheck));
        il.Append(loopBody);
        il.Append(LoadLocalAddress(il, enumerator));
        il.Append(il.Create(OpCodes.Call, getCurrent));
        il.Append(StoreLocal(il, pair));
        il.Append(LoadLocalAddress(il, pair));
        il.Append(il.Create(OpCodes.Call, getValue));
        il.Append(StoreLocal(il, id));
        il.Append(LoadLocal(il, id));
        il.Append(il.Create(OpCodes.Ldc_I4_M1));
        il.Append(il.Create(OpCodes.Beq, loopCheck));
        il.Append(LoadLocal(il, id));
        il.Append(LoadLocalAddress(il, entity));
        il.Append(il.Create(OpCodes.Call, tryGet));
        il.Append(il.Create(OpCodes.Brfalse, loopCheck));
        if (contentCheck != null)
        {
            il.Append(LoadLocal(il, entity));
            il.Append(il.Create(OpCodes.Callvirt, contentCheck));
            il.Append(il.Create(OpCodes.Brfalse, loopCheck));
        }
        il.Append(LoadLocalAddress(il, pair));
        il.Append(il.Create(OpCodes.Call, getKey));
        il.Append(StoreLocal(il, position));
        il.Append(LoadLocal(il, entity));
        il.Append(LoadLocalAddress(il, position));
        il.Append(il.Create(OpCodes.Ldfld, pointX));
        il.Append(LoadLocalAddress(il, position));
        il.Append(il.Create(OpCodes.Ldfld, pointY));
        il.Append(il.Create(OpCodes.Callvirt, draw));
        il.Append(loopCheck);
        il.Append(LoadLocalAddress(il, enumerator));
        il.Append(il.Create(OpCodes.Call, moveNext));
        il.Append(il.Create(OpCodes.Brtrue, loopBody));
        il.Append(il.Create(OpCodes.Ret));

        return method;
    }

    private static TypeReference CloseGenericType(
        ModuleDefinition module,
        TypeReference openType,
        TypeReference firstArgument,
        TypeReference secondArgument)
    {
        TypeReference definition = openType.GetElementType();
        var closed = new GenericInstanceType(module.ImportReference(definition));
        closed.GenericArguments.Add(module.ImportReference(firstArgument));
        closed.GenericArguments.Add(module.ImportReference(secondArgument));
        return closed;
    }

    private static MethodDefinition CreateOptimizedDrawMethod(
        ModuleDefinition module,
        TypeDefinition tileDrawing,
        MethodDefinition vanillaMethod,
        MethodDefinition drawEntries,
        FieldDefinition positions,
        string name)
    {
        MethodReference begin = RequireCall(vanillaMethod, "Begin", "System.Void");
        MethodReference end = RequireCall(vanillaMethod, "End", "System.Void");
        FieldReference spriteBatch = RequireStaticField(vanillaMethod, "Terraria.Main", "spriteBatch");
        FieldReference alphaBlend = RequireStaticField(vanillaMethod, "Microsoft.Xna.Framework.Graphics.BlendState", "AlphaBlend");
        FieldReference noDepth = RequireStaticField(vanillaMethod, "Microsoft.Xna.Framework.Graphics.DepthStencilState", "None");
        FieldReference rasterizer = RequireStaticField(vanillaMethod, "Terraria.Main", "Rasterizer");
        MethodReference defaultSampler = RequireCall(vanillaMethod, "get_DefaultSamplerState", "Microsoft.Xna.Framework.Graphics.SamplerState");
        MethodReference transform = RequireCall(vanillaMethod, "get_Transform", "Microsoft.Xna.Framework.Matrix");
        MethodReference getCount = CreateDictionaryCountGetter(module, positions.FieldType);
        var method = new MethodDefinition(
            name,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        ILProcessor il = method.Body.GetILProcessor();
        Instruction complete = il.Create(OpCodes.Ret);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, positions));
        il.Append(il.Create(OpCodes.Callvirt, getCount));
        il.Append(il.Create(OpCodes.Brfalse, complete));
        il.Append(il.Create(OpCodes.Ldsfld, spriteBatch));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ldsfld, alphaBlend));
        il.Append(il.Create(OpCodes.Call, defaultSampler));
        il.Append(il.Create(OpCodes.Ldsfld, noDepth));
        il.Append(il.Create(OpCodes.Ldsfld, rasterizer));
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Call, transform));
        il.Append(il.Create(OpCodes.Callvirt, begin));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, drawEntries));
        il.Append(il.Create(OpCodes.Ldsfld, spriteBatch));
        il.Append(il.Create(OpCodes.Callvirt, end));
        il.Append(complete);

        return method;
    }

    private static void ReplacePostDrawCalls(
        MethodDefinition postDrawTiles,
        MethodDefinition vanillaHatRacks,
        MethodDefinition vanillaDisplayDolls,
        MethodDefinition optimizedHatRacks,
        MethodDefinition optimizedDisplayDolls,
        MethodReference isOptimizationEnabled)
    {
        Instruction hatRackCall = FindExactCall(postDrawTiles, vanillaHatRacks);
        Instruction displayDollCall = FindExactCall(postDrawTiles, vanillaDisplayDolls);
        Instruction hatRackReceiver = hatRackCall.Previous
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PostDrawTiles did not load the hat-rack receiver.");
        if (hatRackReceiver.OpCode != OpCodes.Ldarg_0)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PostDrawTiles hat-rack call no longer has the verified receiver load.");
        }

        Instruction afterVanillaCalls = displayDollCall.Next
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.PostDrawTiles display-doll call has no continuation.");
        ILProcessor il = postDrawTiles.Body.GetILProcessor();
        il.InsertBefore(hatRackReceiver, il.Create(OpCodes.Call, isOptimizationEnabled));
        il.InsertBefore(hatRackReceiver, il.Create(OpCodes.Brfalse, hatRackReceiver));
        il.InsertBefore(hatRackReceiver, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(hatRackReceiver, il.Create(OpCodes.Call, optimizedHatRacks));
        il.InsertBefore(hatRackReceiver, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(hatRackReceiver, il.Create(OpCodes.Call, optimizedDisplayDolls));
        il.InsertBefore(hatRackReceiver, il.Create(OpCodes.Br, afterVanillaCalls));
    }

    private static MethodReference CreateEntityIdLookup(ModuleDefinition module, TypeReference entityType)
    {
        TypeDefinition tileEntity = CecilPatchPrimitives.RequireType(module, "Terraria.DataStructures.TileEntity");
        MethodDefinition? definition = null;
        for (int index = 0; index < tileEntity.Methods.Count; index++)
        {
            MethodDefinition candidate = tileEntity.Methods[index];
            if (string.Equals(candidate.Name, "TryGet", StringComparison.Ordinal) &&
                candidate.GenericParameters.Count == 1 &&
                candidate.Parameters.Count == 2)
            {
                definition = candidate;
                break;
            }
        }

        if (definition == null)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TileEntity.TryGet<T>(Int32, out T) was not found.");
        }

        var generic = new GenericInstanceMethod(module.ImportReference(definition));
        generic.GenericArguments.Add(module.ImportReference(entityType));
        return generic;
    }

    private static MethodReference CreateDictionaryCountGetter(ModuleDefinition module, TypeReference dictionaryType)
    {
        return new MethodReference("get_Count", module.TypeSystem.Int32, module.ImportReference(dictionaryType))
        {
            HasThis = true
        };
    }

    private static MethodReference RequireCall(MethodDefinition method, string name, string? returnType)
    {
        MethodReference? match = null;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            if (!(method.Body.Instructions[index].Operand is MethodReference candidate) ||
                !string.Equals(candidate.Name, name, StringComparison.Ordinal) ||
                (returnType != null && !string.Equals(candidate.ReturnType.FullName, returnType, StringComparison.Ordinal)))
            {
                continue;
            }

            if (match != null)
            {
                if (!string.Equals(match.FullName, candidate.FullName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " contains ambiguous verified calls to " + name + ".");
                }

                continue;
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " does not contain the verified call to " + name + ".");
    }

    private static FieldReference RequirePointField(MethodDefinition method, string name)
    {
        FieldReference? match = null;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            if (!(method.Body.Instructions[index].Operand is FieldReference candidate) ||
                !string.Equals(candidate.DeclaringType.FullName, "Microsoft.Xna.Framework.Point", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (match == null)
            {
                match = candidate;
            }
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " does not contain Point." + name + ".");
    }

    private static FieldReference RequireStaticField(MethodDefinition method, string declaringType, string name)
    {
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            if (method.Body.Instructions[index].Operand is FieldReference candidate &&
                string.Equals(candidate.DeclaringType.FullName, declaringType, StringComparison.Ordinal) &&
                string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " does not contain " + declaringType + "." + name + ".");
    }

    private static Instruction FindExactCall(MethodDefinition method, MethodDefinition target)
    {
        Instruction? match = null;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            Instruction candidate = method.Body.Instructions[index];
            if (!(candidate.Operand is MethodReference reference) ||
                !string.Equals(reference.FullName, target.FullName, StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " calls " + target.Name + " ambiguously.");
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + method.FullName + " does not call " + target.Name + ".");
    }

    private static Instruction LoadLocalAddress(ILProcessor il, VariableDefinition variable)
    {
        return variable.Index <= byte.MaxValue
            ? il.Create(OpCodes.Ldloca_S, variable)
            : il.Create(OpCodes.Ldloca, variable);
    }
}
