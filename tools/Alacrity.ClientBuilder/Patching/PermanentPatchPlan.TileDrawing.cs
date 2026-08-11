using Mono.Cecil;
using Mono.Cecil.Cil;

// Version-locked common-path reduction for TileDrawing. Only the native lighting lookup whose
// result is unused by every tile except 637 and 638 is skipped; all texture, paint, liquid,
// tree, vine, special-tile, and asset-loading behavior remains native.
internal static partial class PermanentPatchPlan
{
    private const string TileDrawingOptimizationEnabledFieldName = "alacrityTileDrawingOptimizationEnabled";
    private const string LiquidBehindLayerInitializedFieldName = "alacrityLiquidBehindLayerInitialized";
    private const string GetTileDrawDataLightMethodName = "AlacrityGetTileDrawDataLight";
    private const string SetLiquidBehindLayerMethodName = "AlacritySetLiquidBehindLayer";

    private static void PatchTileDrawingPresentation(ModuleDefinition module, MethodReference isOptimizationEnabled)
    {
        TypeDefinition tileDrawing = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.Drawing.TileDrawing");
        MethodDefinition draw = RequireSingleMethod(tileDrawing, "Draw", 3);
        MethodDefinition drawLiquidBehindTiles = RequireSingleMethod(tileDrawing, "DrawLiquidBehindTiles", 1);
        MethodDefinition getTileDrawData = RequireSingleMethod(tileDrawing, "GetTileDrawData", 16);
        FieldDefinition optimizationEnabled = AddTileDrawingOptimizationField(tileDrawing, module);
        FieldDefinition liquidBehindLayerInitialized = AddTileDrawingField(
            tileDrawing,
            LiquidBehindLayerInitializedFieldName,
            module.TypeSystem.Boolean);

        RequireAbsentMethod(tileDrawing, GetTileDrawDataLightMethodName);
        RequireAbsentMethod(tileDrawing, SetLiquidBehindLayerMethodName);
        MethodReference nativeLightingGetColor = RequireLightingGetColor(getTileDrawData);
        MethodReference nativeSetLayer = RequireLiquidBehindSetLayer(drawLiquidBehindTiles);
        MethodDefinition getTileDrawDataLight = CreateGetTileDrawDataLightMethod(
            module,
            tileDrawing,
            optimizationEnabled,
            nativeLightingGetColor);
        MethodDefinition setLiquidBehindLayer = CreateSetLiquidBehindLayerMethod(
            module,
            tileDrawing,
            optimizationEnabled,
            liquidBehindLayerInitialized,
            nativeSetLayer);

        tileDrawing.Methods.Add(getTileDrawDataLight);
        tileDrawing.Methods.Add(setLiquidBehindLayer);
        InsertTileDrawingOptimizationState(draw, isOptimizationEnabled, optimizationEnabled);
        PatchTileDrawDataLighting(getTileDrawData, getTileDrawDataLight, nativeLightingGetColor);
        PatchLiquidBehindLayerState(drawLiquidBehindTiles, liquidBehindLayerInitialized, setLiquidBehindLayer);
    }

    private static FieldDefinition AddTileDrawingOptimizationField(TypeDefinition tileDrawing, ModuleDefinition module)
    {
        return AddTileDrawingField(tileDrawing, TileDrawingOptimizationEnabledFieldName, module.TypeSystem.Boolean);
    }

    private static FieldDefinition AddTileDrawingField(TypeDefinition tileDrawing, string name, TypeReference type)
    {
        for (int index = 0; index < tileDrawing.Fields.Count; index++)
        {
            if (string.Equals(tileDrawing.Fields[index].Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing already contains the Alacrity field " + name + ".");
            }
        }

        var field = new FieldDefinition(name, FieldAttributes.Private, type);
        tileDrawing.Fields.Add(field);
        return field;
    }

    private static void InsertTileDrawingOptimizationState(
        MethodDefinition draw,
        MethodReference isOptimizationEnabled,
        FieldDefinition optimizationEnabled)
    {
        Instruction first = draw.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.Draw has no body.");
        ILProcessor il = draw.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, isOptimizationEnabled));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, optimizationEnabled));
    }

    private static MethodDefinition CreateGetTileDrawDataLightMethod(
        ModuleDefinition module,
        TypeDefinition tileDrawing,
        FieldDefinition optimizationEnabled,
        MethodReference nativeLightingGetColor)
    {
        TypeReference colorType = module.ImportReference(nativeLightingGetColor.ReturnType);
        var method = new MethodDefinition(
            GetTileDrawDataLightMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            colorType);
        method.Parameters.Add(new ParameterDefinition("x", ParameterAttributes.None, module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition("y", ParameterAttributes.None, module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition("tileType", ParameterAttributes.None, module.TypeSystem.UInt16));
        method.Parameters.Add(new ParameterDefinition("drawing", ParameterAttributes.None, module.ImportReference(tileDrawing)));
        method.Body.InitLocals = true;
        var unusedColor = new VariableDefinition(colorType);
        method.Body.Variables.Add(unusedColor);

        Instruction native = Instruction.Create(OpCodes.Nop);
        ILProcessor il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldfld, optimizationEnabled));
        il.Append(il.Create(OpCodes.Brfalse, native));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldc_I4, 637));
        il.Append(il.Create(OpCodes.Beq, native));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldc_I4, 638));
        il.Append(il.Create(OpCodes.Beq, native));
        il.Append(il.Create(OpCodes.Ldloca, unusedColor));
        il.Append(il.Create(OpCodes.Initobj, colorType));
        il.Append(LoadLocal(il, unusedColor));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(native);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, nativeLightingGetColor));
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static MethodDefinition CreateSetLiquidBehindLayerMethod(
        ModuleDefinition module,
        TypeDefinition tileDrawing,
        FieldDefinition optimizationEnabled,
        FieldDefinition liquidBehindLayerInitialized,
        MethodReference nativeSetLayer)
    {
        var method = new MethodDefinition(
            SetLiquidBehindLayerMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("batch", ParameterAttributes.None, module.ImportReference(nativeSetLayer.DeclaringType)));
        method.Parameters.Add(new ParameterDefinition("layer", ParameterAttributes.None, module.TypeSystem.UInt32));
        method.Parameters.Add(new ParameterDefinition("stack", ParameterAttributes.None, module.TypeSystem.UInt16));
        method.Parameters.Add(new ParameterDefinition("drawing", ParameterAttributes.None, module.ImportReference(tileDrawing)));
        Instruction invokeNative = Instruction.Create(OpCodes.Nop);
        ILProcessor il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldfld, optimizationEnabled));
        il.Append(il.Create(OpCodes.Brfalse, invokeNative));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldfld, liquidBehindLayerInitialized));
        il.Append(il.Create(OpCodes.Brfalse, invokeNative));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(invokeNative);
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Stfld, liquidBehindLayerInitialized));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Callvirt, nativeSetLayer));
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static void PatchTileDrawDataLighting(
        MethodDefinition getTileDrawData,
        MethodDefinition optimizedLightingGetColor,
        MethodReference nativeLightingGetColor)
    {
        Instruction? call = null;
        for (int index = 0; index < getTileDrawData.Body.Instructions.Count; index++)
        {
            Instruction candidate = getTileDrawData.Body.Instructions[index];
            if (candidate.Operand is not MethodReference reference ||
                !string.Equals(reference.FullName, nativeLightingGetColor.FullName, StringComparison.Ordinal))
            {
                continue;
            }

            if (call != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.GetTileDrawData contains multiple Lighting.GetColor calls.");
            }

            call = candidate;
        }

        if (call == null)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.GetTileDrawData no longer contains the verified Lighting.GetColor call.");
        }

        ILProcessor il = getTileDrawData.Body.GetILProcessor();
        il.InsertBefore(call, il.Create(OpCodes.Ldarg, getTileDrawData.Parameters[3]));
        il.InsertBefore(call, il.Create(OpCodes.Ldarg_0));
        call.OpCode = OpCodes.Call;
        call.Operand = optimizedLightingGetColor;
    }

    private static void PatchLiquidBehindLayerState(
        MethodDefinition drawLiquidBehindTiles,
        FieldDefinition liquidBehindLayerInitialized,
        MethodDefinition optimizedSetLayer)
    {
        Instruction first = drawLiquidBehindTiles.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.DrawLiquidBehindTiles has no body.");
        ILProcessor il = drawLiquidBehindTiles.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Stfld, liquidBehindLayerInitialized));

        var count = 0;
        for (int index = 0; index < drawLiquidBehindTiles.Body.Instructions.Count; index++)
        {
            Instruction instruction = drawLiquidBehindTiles.Body.Instructions[index];
            if (instruction.Operand is not MethodReference candidate ||
                !string.Equals(candidate.DeclaringType.FullName, "Terraria.Graphics.TileBatch", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, "SetLayer", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 2 ||
                !string.Equals(candidate.Parameters[0].ParameterType.FullName, "System.UInt32", StringComparison.Ordinal) ||
                !string.Equals(candidate.Parameters[1].ParameterType.FullName, "System.UInt16", StringComparison.Ordinal))
            {
                continue;
            }

            il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = optimizedSetLayer;
            count++;
        }

        if (count != 1)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.DrawLiquidBehindTiles no longer has exactly one TileBatch.SetLayer call.");
        }
    }

    private static MethodReference RequireLiquidBehindSetLayer(MethodDefinition drawLiquidBehindTiles)
    {
        MethodReference? match = null;
        for (int index = 0; index < drawLiquidBehindTiles.Body.Instructions.Count; index++)
        {
            if (drawLiquidBehindTiles.Body.Instructions[index].Operand is not MethodReference candidate ||
                !string.Equals(candidate.DeclaringType.FullName, "Terraria.Graphics.TileBatch", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, "SetLayer", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 2 ||
                !string.Equals(candidate.Parameters[0].ParameterType.FullName, "System.UInt32", StringComparison.Ordinal) ||
                !string.Equals(candidate.Parameters[1].ParameterType.FullName, "System.UInt16", StringComparison.Ordinal) ||
                !string.Equals(candidate.ReturnType.FullName, "System.Void", StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.DrawLiquidBehindTiles has multiple TileBatch.SetLayer calls.");
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.DrawLiquidBehindTiles has no verified TileBatch.SetLayer call.");
    }

    private static MethodReference RequireLightingGetColor(MethodDefinition getTileDrawData)
    {
        MethodReference? match = null;
        for (int index = 0; index < getTileDrawData.Body.Instructions.Count; index++)
        {
            if (getTileDrawData.Body.Instructions[index].Operand is not MethodReference candidate ||
                !string.Equals(candidate.DeclaringType.FullName, "Terraria.Lighting", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, "GetColor", StringComparison.Ordinal) ||
                candidate.Parameters.Count != 2 ||
                !string.Equals(candidate.Parameters[0].ParameterType.FullName, "System.Int32", StringComparison.Ordinal) ||
                !string.Equals(candidate.Parameters[1].ParameterType.FullName, "System.Int32", StringComparison.Ordinal) ||
                !string.Equals(candidate.ReturnType.FullName, "Microsoft.Xna.Framework.Color", StringComparison.Ordinal))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.GetTileDrawData has multiple verified Lighting.GetColor calls.");
            }

            match = candidate;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.GetTileDrawData has no verified Lighting.GetColor call.");
    }
}
