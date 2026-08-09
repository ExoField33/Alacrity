using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    private static void PatchPluginRuntimeDraw(TypeDefinition mainType, MethodReference drawNotifications)
    {
        var method = mainType.Methods.SingleOrDefault(method => method.Name == "DrawInterface_33_MouseText" && !method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawInterface_33_MouseText signature did not match the verified plugin draw boundary.");
        var spriteBatch = mainType.Fields.SingleOrDefault(field => field.Name == "spriteBatch" && field.FieldType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 spriteBatch field did not match the verified plugin draw boundary.");
        var first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawInterface_33_MouseText has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, spriteBatch));
        il.InsertBefore(first, il.Create(OpCodes.Call, drawNotifications));
    }

    private static void PatchSwingHitboxCapture(ModuleDefinition module, MethodReference captureSwingHitbox)
    {
        var playerType = module.Types.SingleOrDefault(type => type.FullName == "Terraria.Player")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Player type was not found.");
        var method = playerType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "ItemCheck_GetMeleeHitbox" &&
            !candidate.IsStatic &&
            candidate.ReturnType.FullName == "System.Void" &&
            candidate.Parameters.Count == 4 &&
            candidate.Parameters[0].ParameterType.FullName == "Terraria.Item" &&
            candidate.Parameters[1].ParameterType.FullName == "Microsoft.Xna.Framework.Rectangle" &&
            candidate.Parameters[2].ParameterType.FullName == "System.Boolean&" &&
            candidate.Parameters[3].ParameterType.FullName == "Microsoft.Xna.Framework.Rectangle&")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Player.ItemCheck_GetMeleeHitbox signature did not match the verified hitbox capture hook.");
        if (!method.HasBody)
            throw new InvalidOperationException("Terraria 1.4.5.6 Player.ItemCheck_GetMeleeHitbox has no body.");

        var returns = method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray();
        if (returns.Length == 0)
            throw new InvalidOperationException("Terraria 1.4.5.6 Player.ItemCheck_GetMeleeHitbox has no return instructions.");

        foreach (Instruction ret in returns)
        {
            var il = method.Body.GetILProcessor();
            var firstCapture = il.Create(OpCodes.Ldarg_0);
            il.InsertBefore(ret, firstCapture);
            il.InsertBefore(ret, il.Create(OpCodes.Ldarg_3));
            il.InsertBefore(ret, il.Create(OpCodes.Ldind_I1));
            il.InsertBefore(ret, il.Create(OpCodes.Ldarg_S, method.Parameters[3]));
            il.InsertBefore(ret, il.Create(OpCodes.Ldobj, module.ImportReference(method.Parameters[3].ParameterType.GetElementType())));
            il.InsertBefore(ret, il.Create(OpCodes.Call, captureSwingHitbox));
            RetargetInstructionReferences(method, ret, firstCapture);
        }
    }

    private static void PatchHitboxWorldOverlay(TypeDefinition mainType, MethodReference drawHitboxes)
    {
        var method = mainType.Methods.SingleOrDefault(candidate => candidate.Name == "DrawInterface_1_1_DrawEmoteBubblesInWorld" && candidate.IsStatic && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawInterface_1_1_DrawEmoteBubblesInWorld signature did not match the verified Hitboxes draw boundary.");
        var spriteBatch = mainType.Fields.SingleOrDefault(field => field.Name == "spriteBatch" && field.FieldType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 spriteBatch field did not match the verified Hitboxes draw boundary.");
        var drawAll = method.Body.Instructions.SingleOrDefault(instruction => instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference target && target.FullName == "System.Void Terraria.GameContent.UI.EmoteBubble::DrawAll(Microsoft.Xna.Framework.Graphics.SpriteBatch)")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 emote-bubble draw call did not match the verified Hitboxes draw boundary.");
        Instruction insertionPoint = drawAll.Next ?? throw new InvalidOperationException("Terraria 1.4.5.6 emote-bubble draw call has no continuation.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldsfld, spriteBatch));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, drawHitboxes));
    }
}
