using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    private static void PatchRenderCulling(
        ModuleDefinition module,
        TypeDefinition mainType,
        TypeDefinition particleRendererType,
        MethodReference shouldDrawPlayer,
        MethodReference shouldDrawItem,
        MethodReference shouldDrawParticle)
    {
        PatchPlayerDrawOrder(module, mainType, shouldDrawPlayer);
        PatchDroppedItemDraw(mainType, shouldDrawItem);
        PatchWorldParticleDraw(module, particleRendererType, shouldDrawParticle);
    }

    private static void PatchPlayerDrawOrder(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldDrawPlayer)
    {
        MethodDefinition method = mainType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "RefreshPlayerDrawOrder" &&
            candidate.ReturnType.FullName == "System.Void" &&
            candidate.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 RefreshPlayerDrawOrder signature did not match the verified render-culling hook.");
        TypeDefinition playerType = CecilPatchPrimitives.RequireType(module, "Terraria.Player");
        FieldDefinition outOfRangeField = playerType.Fields.SingleOrDefault(field => field.Name == "outOfRange" && field.FieldType.FullName == "System.Boolean")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Player.outOfRange field did not match the verified player culling hook.");
        Instruction outOfRangeLoad = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldfld &&
            instruction.Operand is FieldReference field &&
            field.FullName == outOfRangeField.FullName)
            ?? throw new InvalidOperationException("RefreshPlayerDrawOrder outOfRange branch was not found.");
        if (!(outOfRangeLoad.Next?.Operand is Instruction loopContinue))
        {
            throw new InvalidOperationException("RefreshPlayerDrawOrder outOfRange branch did not have the verified loop continuation.");
        }

        Instruction insertionPoint = outOfRangeLoad.Next.Next
            ?? throw new InvalidOperationException("RefreshPlayerDrawOrder had no body after the outOfRange branch.");
        VariableDefinition playerLocal = method.Body.Variables.FirstOrDefault(variable => variable.VariableType.FullName == playerType.FullName)
            ?? throw new InvalidOperationException("RefreshPlayerDrawOrder did not contain the verified Player loop local.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, LoadLocal(il, playerLocal));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawPlayer));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue, insertionPoint));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Br, loopContinue));
    }

    internal static void PatchDroppedItemDraw(TypeDefinition mainType, MethodReference shouldDrawItem)
    {
        MethodDefinition method = mainType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "DrawItems" &&
            candidate.ReturnType.FullName == "System.Void" &&
            candidate.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawItems signature did not match the verified render-culling hook.");
        MethodReference drawItem = mainType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "DrawItem" &&
            candidate.ReturnType.FullName == "System.Void" &&
            candidate.Parameters.Count == 2 &&
            candidate.Parameters[1].ParameterType.FullName == "System.Int32")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawItem signature did not match the verified render-culling hook.");
        Instruction drawCall = method.Body.Instructions.SingleOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference called &&
            called.FullName == drawItem.FullName)
            ?? throw new InvalidOperationException("DrawItems did not contain the verified DrawItem call.");
        // The final DrawItem argument is the native loop index. Resolving that exact load rather
        // than the first Int32 local avoids binding an unrelated counter introduced by a compiler
        // or an upstream Terraria revision.
        VariableDefinition itemIndex = method.Body.Variables.SingleOrDefault(variable =>
            variable.VariableType.FullName == "System.Int32" && drawCall.Previous != null && drawCall.Previous.IsLdlocFor(variable))
            ?? throw new InvalidOperationException("DrawItems did not load one verified item-index local into DrawItem.");
        Instruction loopIncrement = method.Body.Instructions.SingleOrDefault(instruction =>
            instruction.OpCode == OpCodes.Add && instruction.Next != null && instruction.Next.IsStlocFor(itemIndex))
            ?? throw new InvalidOperationException("DrawItems did not contain one verified increment for the DrawItem index local.");
        Instruction? loopContinue = loopIncrement.Previous?.Previous;
        if (loopContinue == null || !loopContinue.IsLdlocFor(itemIndex))
        {
            throw new InvalidOperationException("DrawItems did not contain the verified start of the DrawItem index increment.");
        }
        Instruction insertionPoint = drawCall;
        while (insertionPoint != null && !(insertionPoint.OpCode == OpCodes.Ldsfld && insertionPoint.Operand is FieldReference field && field.Name == "item"))
        {
            insertionPoint = insertionPoint.Previous;
        }
        if (insertionPoint == null || !(insertionPoint.Operand is FieldReference itemField) || itemField.Name != "item")
        {
            throw new InvalidOperationException("DrawItems did not contain the verified Main.item load before DrawItem.");
        }

        // DrawItem is an instance member in the audited Terraria build, so its receiver is already
        // on the evaluation stack immediately before Main.item. Insert before that receiver rather
        // than leaving it live across the gate's false branch.
        if (drawItem.HasThis)
        {
            Instruction receiverLoad = insertionPoint.Previous;
            if (receiverLoad == null || receiverLoad.OpCode != OpCodes.Ldarg_0)
            {
                throw new InvalidOperationException("DrawItems did not contain the verified DrawItem receiver before Main.item.");
            }

            insertionPoint = receiverLoad;
        }

        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, LoadLocal(il, itemIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawItem));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue, insertionPoint));
        // A rejected item must still execute the native increment sequence. Branching to Add
        // would leave its operands missing; the verified local load starts that sequence.
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Br, loopContinue));
    }

    internal static void PatchWorldParticleDraw(ModuleDefinition module, TypeDefinition particleRendererType, MethodReference shouldDrawParticle)
    {
        MethodDefinition method = particleRendererType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "Draw" &&
            candidate.ReturnType.FullName == "System.Void" &&
            candidate.Parameters.Count == 1 &&
            candidate.Parameters[0].ParameterType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 ParticleRenderer.Draw signature did not match the verified render-culling hook.");
        TypeDefinition particleType = CecilPatchPrimitives.RequireType(module, "Terraria.Graphics.Renderers.IParticle");
        Instruction particleDraw = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference called &&
            called.Name == "Draw" &&
            called.DeclaringType.FullName == particleType.FullName)
            ?? throw new InvalidOperationException("ParticleRenderer.Draw did not contain the verified IParticle.Draw call.");
        Instruction removedLoad = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference called &&
            called.Name == "get_ShouldBeRemovedFromRenderer" &&
            called.DeclaringType.FullName == particleType.FullName)
            ?? throw new InvalidOperationException("ParticleRenderer.Draw removed-state check was not found.");
        if (!(removedLoad.Next?.Operand is Instruction loopContinue))
        {
            throw new InvalidOperationException("ParticleRenderer.Draw removed-state branch did not have the verified loop continuation.");
        }

        Instruction particleLoad = particleDraw;
        while (particleLoad.Previous != null)
        {
            if (particleLoad.Previous.OpCode == OpCodes.Callvirt &&
                particleLoad.Previous.Operand is MethodReference called &&
                called.Name == "get_Item" &&
                called.DeclaringType is GenericInstanceType listType &&
                listType.GenericArguments.Count == 1 &&
                listType.GenericArguments[0].FullName == particleType.FullName)
            {
                break;
            }
            particleLoad = particleLoad.Previous;
        }
        if (particleLoad.Previous == null || !(particleLoad.Previous.Operand is MethodReference))
        {
            throw new InvalidOperationException("ParticleRenderer.Draw did not contain the verified List<IParticle>.get_Item call before IParticle.Draw.");
        }

        // Terraria's actual loop uses a direct List<IParticle>.get_Item result rather than a
        // compiler local. Materialize it once so the gate can run without disturbing Draw's stack.
        // The bridge reads the renderer's anchor itself, keeping this injected call within the
        // native method's two-entry stack budget.
        var particleLocal = new VariableDefinition(module.ImportReference(particleType));
        method.Body.Variables.Add(particleLocal);
        var il = method.Body.GetILProcessor();
        Instruction itemLoad = particleLoad.Previous;
        Instruction inserted = StoreLocal(il, particleLocal);
        il.InsertAfter(itemLoad, inserted);
        inserted = il.Create(OpCodes.Ldarg_0);
        il.InsertAfter(itemLoad.Next, inserted);
        inserted = LoadLocal(il, particleLocal);
        il.InsertAfter(itemLoad.Next.Next, inserted);
        inserted = il.Create(OpCodes.Call, shouldDrawParticle);
        il.InsertAfter(itemLoad.Next.Next.Next, inserted);
        Instruction resumeNativeDraw = LoadLocal(il, particleLocal);
        il.InsertBefore(particleLoad, resumeNativeDraw);
        inserted = il.Create(OpCodes.Brtrue, resumeNativeDraw);
        il.InsertAfter(itemLoad.Next.Next.Next.Next, inserted);
        il.InsertAfter(itemLoad.Next.Next.Next.Next.Next, il.Create(OpCodes.Br, loopContinue));
    }
}
