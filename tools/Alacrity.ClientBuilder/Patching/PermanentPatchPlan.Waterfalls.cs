using Mono.Cecil;
using Mono.Cecil.Cil;

// Version-locked reductions for WaterfallManager's segment renderer. The patch deliberately
// leaves source discovery, live route evaluation, lighting, draw order, and RNG calls in the
// native method. It only removes repeated identical TileBatch state assignments and the
// exception-wrapped WorldGen.SolidTile helper from verified, already non-null tile paths.
internal static partial class PermanentPatchPlan
{
    private const string WaterfallOptimizationEnabledFieldName = "alacrityWaterfallOptimizationEnabled";
    private const string WaterfallLayerInitializedFieldName = "alacrityWaterfallLayerInitialized";
    private const string WaterfallLayerFieldName = "alacrityWaterfallLayer";
    private const string WaterfallLayerStackFieldName = "alacrityWaterfallLayerStack";
    private const string WaterfallSolidTileMethodName = "AlacrityIsWaterfallSolidTile";
    private const string WaterfallSetLayerMethodName = "AlacritySetWaterfallLayer";
    private const string WaterfallDiscoveryReuseMethodName = "AlacrityTryReuseWaterfallDiscovery";
    private const string WaterfallDiscoveryRememberMethodName = "AlacrityRememberWaterfallDiscovery";
    private const string WaterfallDiscoveryInvalidateMethodName = "AlacrityInvalidateWaterfallDiscovery";
    private const string WaterfallDiscoveryValidFieldName = "alacrityWaterfallDiscoveryValid";
    private const string WaterfallDiscoveryDirtyFieldName = "alacrityWaterfallDiscoveryDirty";
    private const string WaterfallDiscoveryTilesFieldName = "alacrityWaterfallDiscoveryTiles";
    private const string WaterfallDiscoveryScreenXFieldName = "alacrityWaterfallDiscoveryScreenX";
    private const string WaterfallDiscoveryScreenYFieldName = "alacrityWaterfallDiscoveryScreenY";
    private const string WaterfallDiscoveryWidthFieldName = "alacrityWaterfallDiscoveryWidth";
    private const string WaterfallDiscoveryHeightFieldName = "alacrityWaterfallDiscoveryHeight";
    private const string WaterfallDiscoveryQualityFieldName = "alacrityWaterfallDiscoveryQuality";
    private const string WaterfallDiscoveryMaximumFieldName = "alacrityWaterfallDiscoveryMaximum";

    private static void PatchWaterfallPresentation(ModuleDefinition module, MethodReference isOptimizationEnabled)
    {
        TypeDefinition waterfallManager = CecilPatchPrimitives.RequireType(module, "Terraria.WaterfallManager");
        TypeDefinition tileType = CecilPatchPrimitives.RequireType(module, "Terraria.Tile");
        MethodDefinition drawWaterfall = RequireSingleMethod(waterfallManager, "DrawWaterfall", 2);

        FieldDefinition optimizationEnabled = AddWaterfallField(
            waterfallManager,
            WaterfallOptimizationEnabledFieldName,
            module.TypeSystem.Boolean);
        FieldDefinition layerInitialized = AddWaterfallField(
            waterfallManager,
            WaterfallLayerInitializedFieldName,
            module.TypeSystem.Boolean);
        FieldDefinition layer = AddWaterfallField(
            waterfallManager,
            WaterfallLayerFieldName,
            module.TypeSystem.UInt32);
        FieldDefinition layerStack = AddWaterfallField(
            waterfallManager,
            WaterfallLayerStackFieldName,
            module.TypeSystem.UInt16);
        WaterfallDiscoveryFields discovery = AddWaterfallDiscoveryFields(module, waterfallManager);

        RequireAbsentMethod(waterfallManager, WaterfallSolidTileMethodName);
        RequireAbsentMethod(waterfallManager, WaterfallSetLayerMethodName);
        RequireAbsentMethod(waterfallManager, WaterfallDiscoveryReuseMethodName);
        RequireAbsentMethod(waterfallManager, WaterfallDiscoveryRememberMethodName);
        RequireAbsentMethod(waterfallManager, WaterfallDiscoveryInvalidateMethodName);

        MethodReference nativeSolidTile = RequireSolidTileCall(drawWaterfall);
        MethodReference nativeSetLayer = RequireSetLayerCall(drawWaterfall);
        MethodDefinition solidTile = CreateWaterfallSolidTileMethod(
            module,
            waterfallManager,
            tileType,
            nativeSolidTile);
        MethodDefinition setLayer = CreateWaterfallSetLayerMethod(
            module,
            waterfallManager,
            nativeSetLayer,
            optimizationEnabled,
            layerInitialized,
            layer,
            layerStack);
        MethodDefinition tryReuseDiscovery = CreateWaterfallDiscoveryReuseMethod(
            module,
            waterfallManager,
            discovery);
        MethodDefinition rememberDiscovery = CreateWaterfallDiscoveryRememberMethod(
            module,
            waterfallManager,
            discovery);
        MethodDefinition invalidateDiscovery = CreateWaterfallDiscoveryInvalidationMethod(
            module,
            waterfallManager,
            discovery.Dirty);

        waterfallManager.Methods.Add(solidTile);
        waterfallManager.Methods.Add(setLayer);
        waterfallManager.Methods.Add(tryReuseDiscovery);
        waterfallManager.Methods.Add(rememberDiscovery);
        waterfallManager.Methods.Add(invalidateDiscovery);
        InsertWaterfallOptimizationState(drawWaterfall, isOptimizationEnabled, optimizationEnabled, layerInitialized);
        InsertEmptyWaterfallFastPath(module, waterfallManager, drawWaterfall, optimizationEnabled);
        CacheWaterfallFrameState(drawWaterfall);
        ReplaceWaterfallSolidTileCalls(drawWaterfall, solidTile, optimizationEnabled);
        ReplaceWaterfallLayerCalls(drawWaterfall, setLayer);
        PatchWaterfallDiscoveryReuse(
            module,
            waterfallManager,
            tryReuseDiscovery,
            rememberDiscovery,
            isOptimizationEnabled);
        PatchWaterfallDiscoveryInvalidation(module, invalidateDiscovery);
    }

    private sealed class WaterfallDiscoveryFields
    {
        internal WaterfallDiscoveryFields(
            FieldDefinition valid,
            FieldDefinition dirty,
            FieldDefinition tiles,
            FieldDefinition screenX,
            FieldDefinition screenY,
            FieldDefinition width,
            FieldDefinition height,
            FieldDefinition quality,
            FieldDefinition maximum)
        {
            Valid = valid;
            Dirty = dirty;
            Tiles = tiles;
            ScreenX = screenX;
            ScreenY = screenY;
            Width = width;
            Height = height;
            Quality = quality;
            Maximum = maximum;
        }

        internal FieldDefinition Valid { get; }
        internal FieldDefinition Dirty { get; }
        internal FieldDefinition Tiles { get; }
        internal FieldDefinition ScreenX { get; }
        internal FieldDefinition ScreenY { get; }
        internal FieldDefinition Width { get; }
        internal FieldDefinition Height { get; }
        internal FieldDefinition Quality { get; }
        internal FieldDefinition Maximum { get; }
    }

    private static WaterfallDiscoveryFields AddWaterfallDiscoveryFields(ModuleDefinition module, TypeDefinition waterfallManager)
    {
        FieldDefinition tiles = RequireField(CecilPatchPrimitives.RequireType(module, "Terraria.Main"), "tile", "Terraria.Tile[0...,0...]");
        return new WaterfallDiscoveryFields(
            AddWaterfallField(waterfallManager, WaterfallDiscoveryValidFieldName, module.TypeSystem.Boolean),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryDirtyFieldName, module.TypeSystem.Boolean),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryTilesFieldName, module.ImportReference(tiles.FieldType)),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryScreenXFieldName, module.TypeSystem.Single),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryScreenYFieldName, module.TypeSystem.Single),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryWidthFieldName, module.TypeSystem.Int32),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryHeightFieldName, module.TypeSystem.Int32),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryQualityFieldName, module.TypeSystem.Single),
            AddWaterfallField(waterfallManager, WaterfallDiscoveryMaximumFieldName, module.TypeSystem.Int32));
    }

    // FindWaterfalls is scheduled every thirty updates, but its native source scan still walks
    // the entire expanded view even when neither geometry nor liquid state can have changed.
    // Reuse is intentionally narrow: any tracked tile mutation, active liquid simulation,
    // viewport change, quality change, or forced lookup returns to the verified native scan.
    private static MethodDefinition CreateWaterfallDiscoveryReuseMethod(
        ModuleDefinition module,
        TypeDefinition waterfallManager,
        WaterfallDiscoveryFields discovery)
    {
        TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        TypeDefinition liquid = CecilPatchPrimitives.RequireType(module, "Terraria.Liquid");
        TypeDefinition liquidBuffer = CecilPatchPrimitives.RequireType(module, "Terraria.LiquidBuffer");
        FieldDefinition tiles = RequireField(main, "tile", "Terraria.Tile[0...,0...]");
        FieldDefinition screenPosition = RequireField(main, "screenPosition", "Microsoft.Xna.Framework.Vector2");
        FieldDefinition screenWidth = RequireField(main, "screenWidth", "System.Int32");
        FieldDefinition screenHeight = RequireField(main, "screenHeight", "System.Int32");
        FieldDefinition gfxQuality = RequireField(main, "gfxQuality", "System.Single");
        FieldDefinition numLiquid = RequireField(liquid, "numLiquid", "System.Int32");
        FieldDefinition numLiquidBuffer = RequireField(liquidBuffer, "numLiquidBuffer", "System.Int32");
        TypeReference screenPositionType = module.ImportReference(screenPosition.FieldType);
        var vectorX = new FieldReference("X", module.TypeSystem.Single, screenPositionType);
        var vectorY = new FieldReference("Y", module.TypeSystem.Single, screenPositionType);
        FieldDefinition maximum = RequireField(waterfallManager, "maxWaterfallCount", "System.Int32");
        var method = new MethodDefinition(
            WaterfallDiscoveryReuseMethodName,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition("forced", ParameterAttributes.None, module.TypeSystem.Boolean));
        method.Parameters.Add(new ParameterDefinition("optimized", ParameterAttributes.None, module.TypeSystem.Boolean));

        Instruction reject = Instruction.Create(OpCodes.Ldc_I4_0);
        Instruction checkPolicy = Instruction.Create(OpCodes.Nop);
        Instruction checkValidity = Instruction.Create(OpCodes.Nop);
        Instruction checkDirty = Instruction.Create(OpCodes.Nop);
        Instruction checkTiles = Instruction.Create(OpCodes.Nop);
        Instruction checkScreenX = Instruction.Create(OpCodes.Nop);
        Instruction checkScreenY = Instruction.Create(OpCodes.Nop);
        Instruction checkWidth = Instruction.Create(OpCodes.Nop);
        Instruction checkHeight = Instruction.Create(OpCodes.Nop);
        Instruction checkQuality = Instruction.Create(OpCodes.Nop);
        Instruction checkMaximum = Instruction.Create(OpCodes.Nop);
        Instruction checkLiquid = Instruction.Create(OpCodes.Nop);
        Instruction checkBufferedLiquid = Instruction.Create(OpCodes.Nop);
        Instruction accept = Instruction.Create(OpCodes.Ldc_I4_1);
        ILProcessor il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Brfalse, checkPolicy));
        il.Append(reject);
        il.Append(il.Create(OpCodes.Ret));
        il.Append(checkPolicy);
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Brtrue, checkValidity));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkValidity);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.Valid));
        il.Append(il.Create(OpCodes.Brtrue, checkDirty));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkDirty);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.Dirty));
        il.Append(il.Create(OpCodes.Brfalse, checkTiles));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkTiles);
        il.Append(il.Create(OpCodes.Ldsfld, tiles));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.Tiles));
        il.Append(il.Create(OpCodes.Ceq));
        il.Append(il.Create(OpCodes.Brtrue, checkScreenX));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkScreenX);
        il.Append(il.Create(OpCodes.Ldsfld, screenPosition));
        il.Append(il.Create(OpCodes.Ldfld, vectorX));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.ScreenX));
        il.Append(il.Create(OpCodes.Beq, checkScreenY));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkScreenY);
        il.Append(il.Create(OpCodes.Ldsfld, screenPosition));
        il.Append(il.Create(OpCodes.Ldfld, vectorY));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.ScreenY));
        il.Append(il.Create(OpCodes.Beq, checkWidth));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkWidth);
        il.Append(il.Create(OpCodes.Ldsfld, screenWidth));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.Width));
        il.Append(il.Create(OpCodes.Beq, checkHeight));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkHeight);
        il.Append(il.Create(OpCodes.Ldsfld, screenHeight));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.Height));
        il.Append(il.Create(OpCodes.Beq, checkQuality));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkQuality);
        il.Append(il.Create(OpCodes.Ldsfld, gfxQuality));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.Quality));
        il.Append(il.Create(OpCodes.Beq, checkMaximum));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkMaximum);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, maximum));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, discovery.Maximum));
        il.Append(il.Create(OpCodes.Beq, checkLiquid));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkLiquid);
        il.Append(il.Create(OpCodes.Ldsfld, numLiquid));
        il.Append(il.Create(OpCodes.Brfalse, checkBufferedLiquid));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(checkBufferedLiquid);
        il.Append(il.Create(OpCodes.Ldsfld, numLiquidBuffer));
        il.Append(il.Create(OpCodes.Brfalse, accept));
        il.Append(il.Create(OpCodes.Br, reject));
        il.Append(accept);
        il.Append(il.Create(OpCodes.Ret));
        il.Append(reject);
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static MethodDefinition CreateWaterfallDiscoveryRememberMethod(
        ModuleDefinition module,
        TypeDefinition waterfallManager,
        WaterfallDiscoveryFields discovery)
    {
        TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        FieldDefinition tiles = RequireField(main, "tile", "Terraria.Tile[0...,0...]");
        FieldDefinition screenPosition = RequireField(main, "screenPosition", "Microsoft.Xna.Framework.Vector2");
        FieldDefinition screenWidth = RequireField(main, "screenWidth", "System.Int32");
        FieldDefinition screenHeight = RequireField(main, "screenHeight", "System.Int32");
        FieldDefinition gfxQuality = RequireField(main, "gfxQuality", "System.Single");
        TypeReference screenPositionType = module.ImportReference(screenPosition.FieldType);
        var vectorX = new FieldReference("X", module.TypeSystem.Single, screenPositionType);
        var vectorY = new FieldReference("Y", module.TypeSystem.Single, screenPositionType);
        FieldDefinition maximum = RequireField(waterfallManager, "maxWaterfallCount", "System.Int32");
        var method = new MethodDefinition(
            WaterfallDiscoveryRememberMethodName,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("optimized", ParameterAttributes.None, module.TypeSystem.Boolean));
        Instruction finish = Instruction.Create(OpCodes.Ret);
        ILProcessor il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Brfalse, finish));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldsfld, tiles));
        il.Append(il.Create(OpCodes.Stfld, discovery.Tiles));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldsfld, screenPosition));
        il.Append(il.Create(OpCodes.Ldfld, vectorX));
        il.Append(il.Create(OpCodes.Stfld, discovery.ScreenX));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldsfld, screenPosition));
        il.Append(il.Create(OpCodes.Ldfld, vectorY));
        il.Append(il.Create(OpCodes.Stfld, discovery.ScreenY));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldsfld, screenWidth));
        il.Append(il.Create(OpCodes.Stfld, discovery.Width));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldsfld, screenHeight));
        il.Append(il.Create(OpCodes.Stfld, discovery.Height));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldsfld, gfxQuality));
        il.Append(il.Create(OpCodes.Stfld, discovery.Quality));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, maximum));
        il.Append(il.Create(OpCodes.Stfld, discovery.Maximum));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stfld, discovery.Dirty));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stfld, discovery.Valid));
        il.Append(finish);
        return method;
    }

    private static MethodDefinition CreateWaterfallDiscoveryInvalidationMethod(
        ModuleDefinition module,
        TypeDefinition waterfallManager,
        FieldDefinition dirty)
    {
        TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        FieldDefinition manager = RequireField(main, "waterfallManager", "Terraria.WaterfallManager");
        FieldDefinition instance = RequireField(main, "instance", "Terraria.Main");
        var method = new MethodDefinition(
            WaterfallDiscoveryInvalidateMethodName,
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        Instruction finish = Instruction.Create(OpCodes.Ret);
        ILProcessor il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldsfld, instance));
        il.Append(il.Create(OpCodes.Brfalse, finish));
        il.Append(il.Create(OpCodes.Ldsfld, instance));
        il.Append(il.Create(OpCodes.Ldfld, manager));
        il.Append(il.Create(OpCodes.Brfalse, finish));
        il.Append(il.Create(OpCodes.Ldsfld, instance));
        il.Append(il.Create(OpCodes.Ldfld, manager));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stfld, dirty));
        il.Append(finish);
        return method;
    }

    private static void PatchWaterfallDiscoveryReuse(
        ModuleDefinition module,
        TypeDefinition waterfallManager,
        MethodDefinition tryReuse,
        MethodDefinition remember,
        MethodReference isOptimizationEnabled)
    {
        MethodDefinition findWaterfalls = RequireSingleMethod(waterfallManager, "FindWaterfalls", 1);
        FieldDefinition findWaterfallCount = RequireField(waterfallManager, "findWaterfallCount", "System.Int32");
        var optimizationEnabled = new VariableDefinition(module.TypeSystem.Boolean);
        findWaterfalls.Body.Variables.Add(optimizationEnabled);
        Instruction? reset = null;
        for (int index = 1; index < findWaterfalls.Body.Instructions.Count; index++)
        {
            Instruction instruction = findWaterfalls.Body.Instructions[index];
            if (instruction.OpCode == OpCodes.Stfld &&
                ReferenceEquals(instruction.Operand, findWaterfallCount) &&
                IsLoadInt(findWaterfalls.Body.Instructions[index - 1], 0))
            {
                if (reset is not null)
                {
                    throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.FindWaterfalls has ambiguous discovery-counter resets.");
                }

                reset = instruction;
            }
        }

        if (reset is null || reset.Next is null)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.FindWaterfalls no longer has the verified discovery-counter reset.");
        }

        ILProcessor il = findWaterfalls.Body.GetILProcessor();
        Instruction continueNative = reset.Next;
        Instruction loadPolicy = il.Create(OpCodes.Call, isOptimizationEnabled);
        Instruction storePolicy = StoreLocal(il, optimizationEnabled);
        Instruction loadManager = il.Create(OpCodes.Ldarg_0);
        Instruction loadForced = il.Create(OpCodes.Ldarg_1);
        Instruction loadPolicyLocal = LoadLocal(il, optimizationEnabled);
        Instruction tryReuseCall = il.Create(OpCodes.Call, tryReuse);
        Instruction continueWhenNeeded = il.Create(OpCodes.Brfalse, continueNative);
        Instruction returnWhenReused = il.Create(OpCodes.Ret);
        il.InsertAfter(reset, loadPolicy);
        il.InsertAfter(loadPolicy, storePolicy);
        il.InsertAfter(storePolicy, loadManager);
        il.InsertAfter(loadManager, loadForced);
        il.InsertAfter(loadForced, loadPolicyLocal);
        il.InsertAfter(loadPolicyLocal, tryReuseCall);
        il.InsertAfter(tryReuseCall, continueWhenNeeded);
        il.InsertAfter(continueWhenNeeded, returnWhenReused);

        Instruction? addTime = null;
        for (int index = 0; index < findWaterfalls.Body.Instructions.Count; index++)
        {
            if (findWaterfalls.Body.Instructions[index].Operand is MethodReference candidate &&
                string.Equals(candidate.DeclaringType.FullName, "Terraria.TimeLogger/TimeLogData", StringComparison.Ordinal) &&
                string.Equals(candidate.Name, "AddTime", StringComparison.Ordinal) &&
                candidate.Parameters.Count == 1)
            {
                if (addTime is not null)
                {
                    throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.FindWaterfalls has ambiguous TimeLogger.AddTime calls.");
                }

                addTime = findWaterfalls.Body.Instructions[index];
            }
        }

        if (addTime is null)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.FindWaterfalls no longer records discovery timing.");
        }

        il.InsertBefore(addTime, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(addTime, LoadLocal(il, optimizationEnabled));
        il.InsertBefore(addTime, il.Create(OpCodes.Call, remember));
    }

    private static void PatchWaterfallDiscoveryInvalidation(ModuleDefinition module, MethodDefinition invalidate)
    {
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "PlaceTile", 7, invalidate);
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "KillTile", 5, invalidate);
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "ReplaceTile", 4, invalidate);
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "SlopeTile", 5, invalidate);
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "PoundTile", 2, invalidate);
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.Wiring"), "Actuate", 2, invalidate);
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.Wiring"), "ActuateForced", 2, invalidate);
        PatchWaterfallDiscoveryMutation(CecilPatchPrimitives.RequireType(module, "Terraria.Main"), "OnTileChangeEvent", 4, invalidate);
    }

    private static void PatchWaterfallDiscoveryMutation(
        TypeDefinition type,
        string methodName,
        int parameterCount,
        MethodDefinition invalidate)
    {
        MethodDefinition method = RequireSingleMethod(type, methodName, parameterCount);
        Instruction first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + "." + methodName + " has no body.");
        method.Body.GetILProcessor().InsertBefore(first, method.Body.GetILProcessor().Create(OpCodes.Call, invalidate));
    }

    private static FieldDefinition AddWaterfallField(TypeDefinition type, string name, TypeReference fieldType)
    {
        for (int index = 0; index < type.Fields.Count; index++)
        {
            if (string.Equals(type.Fields[index].Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager already contains the Alacrity field " + name + ".");
            }
        }

        var field = new FieldDefinition(name, FieldAttributes.Private, fieldType);
        type.Fields.Add(field);
        return field;
    }

    private static MethodReference RequireSolidTileCall(MethodDefinition method)
    {
        MethodReference? match = null;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            if (method.Body.Instructions[index].Operand is not MethodReference candidate ||
                !string.Equals(candidate.DeclaringType.FullName, "Terraria.WorldGen", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, "SolidTile", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 1 ||
                !string.Equals(candidate.Parameters[0].ParameterType.FullName, "Terraria.Tile", StringComparison.Ordinal) ||
                !string.Equals(candidate.ReturnType.FullName, "System.Boolean", StringComparison.Ordinal))
            {
                continue;
            }

            match ??= candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.DrawWaterfall has no verified WorldGen.SolidTile(Tile) call.");
    }

    private static MethodReference RequireSetLayerCall(MethodDefinition method)
    {
        MethodReference? match = null;
        var count = 0;
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            if (method.Body.Instructions[index].Operand is not MethodReference candidate ||
                !string.Equals(candidate.DeclaringType.FullName, "Terraria.Graphics.TileBatch", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, "SetLayer", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 2 ||
                !string.Equals(candidate.Parameters[0].ParameterType.FullName, "System.UInt32", StringComparison.Ordinal) ||
                !string.Equals(candidate.Parameters[1].ParameterType.FullName, "System.UInt16", StringComparison.Ordinal) ||
                !string.Equals(candidate.ReturnType.FullName, "System.Void", StringComparison.Ordinal))
            {
                continue;
            }

            match ??= candidate;
            count++;
        }

        if (count != 2)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.DrawWaterfall no longer has exactly two verified TileBatch.SetLayer calls.");
        }

        return match!;
    }

    private static MethodDefinition CreateWaterfallSolidTileMethod(
        ModuleDefinition module,
        TypeDefinition waterfallManager,
        TypeDefinition tileType,
        MethodReference nativeSolidTile)
    {
        FieldDefinition tileTypeField = RequireField(tileType, "type", "System.UInt16");
        MethodReference active = RequireTileMethod(tileType, "nactive", "System.Boolean");
        MethodReference halfBrick = RequireTileMethod(tileType, "halfBrick", "System.Boolean");
        MethodReference slope = RequireTileMethod(tileType, "slope", "System.Byte");
        MethodDefinition nativeSolidTileDefinition = nativeSolidTile.Resolve()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 WorldGen.SolidTile(Tile) could not be resolved.");
        FieldReference tileSolid = RequireStaticField(nativeSolidTileDefinition, "Terraria.Main", "tileSolid");
        FieldReference tileSolidTop = RequireStaticField(nativeSolidTileDefinition, "Terraria.Main", "tileSolidTop");
        TypeReference tileReference = module.ImportReference(tileType);
        var method = new MethodDefinition(
            WaterfallSolidTileMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition("tile", ParameterAttributes.None, tileReference));
        method.Parameters.Add(new ParameterDefinition("optimized", ParameterAttributes.None, module.TypeSystem.Boolean));
        var native = Instruction.Create(OpCodes.Nop);
        var notSolid = Instruction.Create(OpCodes.Ldc_I4_0);
        var checkTile = Instruction.Create(OpCodes.Nop);
        ILProcessor il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Brtrue, checkTile));
        il.Append(native);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, nativeSolidTile));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(checkTile);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Brfalse, notSolid));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, active));
        il.Append(il.Create(OpCodes.Brfalse, notSolid));
        il.Append(il.Create(OpCodes.Ldsfld, tileSolid));
        il.Append(il.Create(OpCodes.Brfalse, notSolid));
        il.Append(il.Create(OpCodes.Ldsfld, tileSolidTop));
        il.Append(il.Create(OpCodes.Brfalse, notSolid));
        il.Append(il.Create(OpCodes.Ldsfld, tileSolid));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, tileTypeField));
        il.Append(il.Create(OpCodes.Ldelem_U1));
        il.Append(il.Create(OpCodes.Brfalse, notSolid));
        il.Append(il.Create(OpCodes.Ldsfld, tileSolidTop));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, tileTypeField));
        il.Append(il.Create(OpCodes.Ldelem_U1));
        il.Append(il.Create(OpCodes.Brtrue, notSolid));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, halfBrick));
        il.Append(il.Create(OpCodes.Brtrue, notSolid));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Callvirt, slope));
        il.Append(il.Create(OpCodes.Brtrue, notSolid));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(notSolid);
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static MethodDefinition CreateWaterfallSetLayerMethod(
        ModuleDefinition module,
        TypeDefinition waterfallManager,
        MethodReference nativeSetLayer,
        FieldDefinition optimizationEnabled,
        FieldDefinition layerInitialized,
        FieldDefinition layer,
        FieldDefinition layerStack)
    {
        var method = new MethodDefinition(
            WaterfallSetLayerMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("batch", ParameterAttributes.None, module.ImportReference(nativeSetLayer.DeclaringType)));
        method.Parameters.Add(new ParameterDefinition("layer", ParameterAttributes.None, module.TypeSystem.UInt32));
        method.Parameters.Add(new ParameterDefinition("stack", ParameterAttributes.None, module.TypeSystem.UInt16));
        method.Parameters.Add(new ParameterDefinition("manager", ParameterAttributes.None, module.ImportReference(waterfallManager)));
        Instruction invokeNative = Instruction.Create(OpCodes.Nop);
        ILProcessor il = method.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldfld, optimizationEnabled));
        il.Append(il.Create(OpCodes.Brfalse, invokeNative));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldfld, layerInitialized));
        il.Append(il.Create(OpCodes.Brfalse, invokeNative));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldfld, layer));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Bne_Un, invokeNative));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldfld, layerStack));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Bne_Un, invokeNative));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(invokeNative);
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stfld, layerInitialized));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Stfld, layer));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Stfld, layerStack));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Callvirt, nativeSetLayer));
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static void InsertWaterfallOptimizationState(
        MethodDefinition drawWaterfall,
        MethodReference isOptimizationEnabled,
        FieldDefinition optimizationEnabled,
        FieldDefinition layerInitialized)
    {
        Instruction first = drawWaterfall.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.DrawWaterfall has no body.");
        ILProcessor il = drawWaterfall.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, isOptimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, optimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, layerInitialized));
    }

    // Draw() can invoke DrawWaterfall for active liquid styles even when discovery produced no
    // waterfalls. Preserve the native per-pass state reset, then avoid all route-loop setup.
    private static void InsertEmptyWaterfallFastPath(
        ModuleDefinition module,
        TypeDefinition waterfallManager,
        MethodDefinition drawWaterfall,
        FieldDefinition optimizationEnabled)
    {
        TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        FieldDefinition currentMax = RequireField(waterfallManager, "currentMax", "System.Int32");
        FieldDefinition drewLava = RequireField(main, "drewLava", "System.Boolean");
        FieldDefinition ambientWaterfallX = RequireField(main, "ambientWaterfallX", "System.Single");
        FieldDefinition ambientWaterfallY = RequireField(main, "ambientWaterfallY", "System.Single");
        FieldDefinition ambientWaterfallStrength = RequireField(main, "ambientWaterfallStrength", "System.Single");
        FieldDefinition ambientLavafallX = RequireField(main, "ambientLavafallX", "System.Single");
        FieldDefinition ambientLavafallY = RequireField(main, "ambientLavafallY", "System.Single");
        FieldDefinition ambientLavafallStrength = RequireField(main, "ambientLavafallStrength", "System.Single");
        FieldDefinition tileSolid = RequireField(main, "tileSolid", "System.Boolean[]");
        Instruction first = drawWaterfall.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.DrawWaterfall has no body.");
        ILProcessor il = drawWaterfall.Body.GetILProcessor();

        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldfld, optimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldfld, currentMax));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, drewLava));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_R4, -1f));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, ambientWaterfallX));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_R4, -1f));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, ambientWaterfallY));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_R4, 0f));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, ambientWaterfallStrength));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_R4, -1f));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, ambientLavafallX));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_R4, -1f));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, ambientLavafallY));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_R4, 0f));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, ambientLavafallStrength));
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, tileSolid));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 546));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(first, il.Create(OpCodes.Stelem_I1));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void CacheWaterfallFrameState(MethodDefinition drawWaterfall)
    {
        FieldReference tiles = RequireStaticField(drawWaterfall, "Terraria.Main", "tile");
        FieldReference screenPosition = RequireStaticField(drawWaterfall, "Terraria.Main", "screenPosition");
        FieldReference screenWidth = RequireStaticField(drawWaterfall, "Terraria.Main", "screenWidth");
        FieldReference screenHeight = RequireStaticField(drawWaterfall, "Terraria.Main", "screenHeight");
        var tilesLocal = new VariableDefinition(drawWaterfall.Module.ImportReference(tiles.FieldType));
        var screenPositionLocal = new VariableDefinition(drawWaterfall.Module.ImportReference(screenPosition.FieldType));
        var screenWidthLocal = new VariableDefinition(drawWaterfall.Module.TypeSystem.Int32);
        var screenHeightLocal = new VariableDefinition(drawWaterfall.Module.TypeSystem.Int32);
        drawWaterfall.Body.Variables.Add(tilesLocal);
        drawWaterfall.Body.Variables.Add(screenPositionLocal);
        drawWaterfall.Body.Variables.Add(screenWidthLocal);
        drawWaterfall.Body.Variables.Add(screenHeightLocal);

        Instruction first = drawWaterfall.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.DrawWaterfall has no body.");
        ILProcessor il = drawWaterfall.Body.GetILProcessor();
        Instruction tilesLoad = il.Create(OpCodes.Ldsfld, tiles);
        Instruction screenPositionLoad = il.Create(OpCodes.Ldsfld, screenPosition);
        Instruction screenWidthLoad = il.Create(OpCodes.Ldsfld, screenWidth);
        Instruction screenHeightLoad = il.Create(OpCodes.Ldsfld, screenHeight);
        il.InsertBefore(first, tilesLoad);
        il.InsertBefore(first, StoreLocal(il, tilesLocal));
        il.InsertBefore(first, screenPositionLoad);
        il.InsertBefore(first, StoreLocal(il, screenPositionLocal));
        il.InsertBefore(first, screenWidthLoad);
        il.InsertBefore(first, StoreLocal(il, screenWidthLocal));
        il.InsertBefore(first, screenHeightLoad);
        il.InsertBefore(first, StoreLocal(il, screenHeightLocal));

        for (int index = 0; index < drawWaterfall.Body.Instructions.Count; index++)
        {
            Instruction instruction = drawWaterfall.Body.Instructions[index];
            if (ReferenceEquals(instruction, tilesLoad) ||
                ReferenceEquals(instruction, screenPositionLoad) ||
                ReferenceEquals(instruction, screenWidthLoad) ||
                ReferenceEquals(instruction, screenHeightLoad) ||
                instruction.OpCode != OpCodes.Ldsfld ||
                instruction.Operand is not FieldReference field)
            {
                continue;
            }

            if (string.Equals(field.FullName, tiles.FullName, StringComparison.Ordinal))
            {
                RewriteAsLoadLocal(instruction, tilesLocal);
            }
            else if (string.Equals(field.FullName, screenPosition.FullName, StringComparison.Ordinal))
            {
                RewriteAsLoadLocal(instruction, screenPositionLocal);
            }
            else if (string.Equals(field.FullName, screenWidth.FullName, StringComparison.Ordinal))
            {
                RewriteAsLoadLocal(instruction, screenWidthLocal);
            }
            else if (string.Equals(field.FullName, screenHeight.FullName, StringComparison.Ordinal))
            {
                RewriteAsLoadLocal(instruction, screenHeightLocal);
            }
        }
    }

    private static void RewriteAsLoadLocal(Instruction instruction, VariableDefinition variable)
    {
        switch (variable.Index)
        {
            case 0:
                instruction.OpCode = OpCodes.Ldloc_0;
                instruction.Operand = null;
                break;
            case 1:
                instruction.OpCode = OpCodes.Ldloc_1;
                instruction.Operand = null;
                break;
            case 2:
                instruction.OpCode = OpCodes.Ldloc_2;
                instruction.Operand = null;
                break;
            case 3:
                instruction.OpCode = OpCodes.Ldloc_3;
                instruction.Operand = null;
                break;
            default:
                instruction.OpCode = variable.Index <= byte.MaxValue ? OpCodes.Ldloc_S : OpCodes.Ldloc;
                instruction.Operand = variable;
                break;
        }
    }

    private static void ReplaceWaterfallSolidTileCalls(
        MethodDefinition drawWaterfall,
        MethodDefinition optimizedSolidTile,
        FieldDefinition optimizationEnabled)
    {
        var count = 0;
        ILProcessor il = drawWaterfall.Body.GetILProcessor();
        for (int index = 0; index < drawWaterfall.Body.Instructions.Count; index++)
        {
            Instruction instruction = drawWaterfall.Body.Instructions[index];
            if (instruction.Operand is not MethodReference candidate ||
                !string.Equals(candidate.DeclaringType.FullName, "Terraria.WorldGen", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, "SolidTile", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 1 ||
                !string.Equals(candidate.Parameters[0].ParameterType.FullName, "Terraria.Tile", StringComparison.Ordinal))
            {
                continue;
            }

            il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(instruction, il.Create(OpCodes.Ldfld, optimizationEnabled));
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = optimizedSolidTile;
            count++;
        }

        if (count < 5)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.DrawWaterfall no longer has the expected guarded solidity hot-path calls.");
        }
    }

    private static void ReplaceWaterfallLayerCalls(MethodDefinition drawWaterfall, MethodDefinition optimizedSetLayer)
    {
        var count = 0;
        ILProcessor il = drawWaterfall.Body.GetILProcessor();
        for (int index = 0; index < drawWaterfall.Body.Instructions.Count; index++)
        {
            Instruction instruction = drawWaterfall.Body.Instructions[index];
            if (instruction.Operand is not MethodReference candidate ||
                !string.Equals(candidate.DeclaringType.FullName, "Terraria.Graphics.TileBatch", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, "SetLayer", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 2)
            {
                continue;
            }

            il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = optimizedSetLayer;
            count++;
        }

        if (count != 2)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 WaterfallManager.DrawWaterfall no longer has exactly two layer-state calls.");
        }
    }

    private static MethodReference RequireTileMethod(TypeDefinition tileType, string name, string returnType)
    {
        MethodDefinition? match = null;
        for (int index = 0; index < tileType.Methods.Count; index++)
        {
            MethodDefinition candidate = tileType.Methods[index];
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal) ||
                candidate.Parameters.Count != 0 ||
                !string.Equals(candidate.ReturnType.FullName, returnType, StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 Tile." + name + " resolves ambiguously.");
            }

            match = candidate;
        }

        return match == null
            ? throw new InvalidOperationException("Terraria 1.4.5.6 Tile." + name + " was not found.")
            : tileType.Module.ImportReference(match);
    }
}
