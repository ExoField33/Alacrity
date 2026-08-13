using Mono.Cecil;
using Mono.Cecil.Cil;

// Version-locked static-descriptor cache hooks for the audited TileDrawing.DrawSingleTile call.
// The injected wrapper returns to Terraria's native method for every unsupported tile, so this
// patch never changes the renderer's broad control flow or exposes engine objects to plugins.
internal static partial class PermanentPatchPlan
{
    private const string StaticTileChunkWrapperMethodName = "AlacrityDrawStaticChunkAwareTile";

    private static void PatchStaticTileChunkPresentation(
        ModuleDefinition module,
        MethodReference tryDrawStaticTileChunk,
        MethodReference invalidateStaticTileChunks)
    {
        TypeDefinition tileDrawing = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.Drawing.TileDrawing");
        MethodDefinition draw = RequireSingleMethod(tileDrawing, "Draw", 3);
        MethodDefinition drawSingleTile = RequireSingleMethod(tileDrawing, "DrawSingleTile", 4);
        RequireAbsentMethod(tileDrawing, StaticTileChunkWrapperMethodName);

        MethodDefinition wrapper = CreateStaticTileChunkWrapper(
            module,
            tileDrawing,
            drawSingleTile,
            tryDrawStaticTileChunk);
        tileDrawing.Methods.Add(wrapper);
        ReplaceDrawSingleTileCall(draw, drawSingleTile, wrapper);

        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "PlaceTile", 7, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "KillTile", 5, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "KillWall", 3, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "ReplaceTile", 4, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "SlopeTile", 5, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "PoundTile", 2, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "paintTile", 5, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "paintCoatTile", 5, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "paintWall", 5, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "paintCoatWall", 5, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "SquareTileFrame", 3, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.WorldGen"), "SquareWallFrame", 3, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.Wiring"), "Actuate", 2, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.Wiring"), "ActuateForced", 2, invalidateStaticTileChunks);
        PatchStaticTileChunkMutation(CecilPatchPrimitives.RequireType(module, "Terraria.Main"), "OnTileChangeEvent", 4, invalidateStaticTileChunks);
    }


    private static MethodDefinition CreateStaticTileChunkWrapper(
        ModuleDefinition module,
        TypeDefinition tileDrawing,
        MethodDefinition drawSingleTile,
        MethodReference tryDrawStaticTileChunk)
    {
        var method = new MethodDefinition(
            StaticTileChunkWrapperMethodName,
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("screenPosition", ParameterAttributes.None, module.ImportReference(drawSingleTile.Parameters[0].ParameterType)));
        method.Parameters.Add(new ParameterDefinition("screenOffset", ParameterAttributes.None, module.ImportReference(drawSingleTile.Parameters[1].ParameterType)));
        method.Parameters.Add(new ParameterDefinition("tileX", ParameterAttributes.None, module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition("tileY", ParameterAttributes.None, module.TypeSystem.Int32));
        method.Parameters.Add(new ParameterDefinition("solidLayer", ParameterAttributes.None, module.TypeSystem.Boolean));

        Instruction native = Instruction.Create(OpCodes.Nop);
        ILProcessor il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[4]));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[0]));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[1]));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[2]));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[3]));
        il.Append(il.Create(OpCodes.Call, tryDrawStaticTileChunk));
        il.Append(il.Create(OpCodes.Brfalse, native));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(native);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[0]));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[1]));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[2]));
        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[3]));
        il.Append(il.Create(OpCodes.Call, drawSingleTile));
        il.Append(il.Create(OpCodes.Ret));
        return method;
    }

    private static void ReplaceDrawSingleTileCall(
        MethodDefinition draw,
        MethodDefinition drawSingleTile,
        MethodDefinition wrapper)
    {
        Instruction? call = null;
        for (int index = 0; index < draw.Body.Instructions.Count; index++)
        {
            Instruction instruction = draw.Body.Instructions[index];
            if (instruction.Operand is not MethodReference candidate ||
                !string.Equals(candidate.FullName, drawSingleTile.FullName, StringComparison.Ordinal))
            {
                continue;
            }

            if (call != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.Draw has multiple DrawSingleTile calls.");
            }

            call = instruction;
        }

        if (call == null)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 TileDrawing.Draw no longer contains the verified DrawSingleTile call.");
        }

        ILProcessor il = draw.Body.GetILProcessor();
        // The native call already has this, screen position, offset, x, and y on the stack.
        // Appending the first Draw argument makes the wrapper's final solid-layer argument.
        il.InsertBefore(call, il.Create(OpCodes.Ldarg_1));
        call.OpCode = OpCodes.Call;
        call.Operand = wrapper;
    }

    private static void PatchStaticTileChunkMutation(
        TypeDefinition type,
        string methodName,
        int parameterCount,
        MethodReference invalidateStaticTileChunks)
    {
        MethodDefinition method = RequireSingleMethod(type, methodName, parameterCount);
        if (method.Parameters.Count < 2)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + "." + methodName + " no longer exposes tile coordinates.");
        }

        Instruction first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + type.FullName + "." + methodName + " has no body.");
        ILProcessor il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[0]));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[1]));
        il.InsertBefore(first, il.Create(OpCodes.Call, invalidateStaticTileChunks));
    }
}
