using Mono.Cecil;
using Mono.Cecil.Cil;

// The native Rain.Update call remains exactly where Terraria placed it. This patch only swaps
// the single audited SpriteBatch.Draw invocation for a wrapper that either queues one instance
// or immediately performs the untouched native call when the optional presentation is inactive.
internal static partial class PermanentPatchPlan
{
    private const string RainDrawWrapperMethodName = "AlacrityDrawRainSprite";
    private const string RainWorldTransformFieldName = "alacrityRainUsesWorldTransform";

    private static void PatchRainPresentation(
        ModuleDefinition module,
        MethodReference tryBeginRainPresentation,
        MethodReference tryQueueRainPresentation,
        MethodReference endRainPresentation)
    {
        TypeDefinition main = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        MethodDefinition drawRain = main.Methods.SingleOrDefault(method =>
                string.Equals(method.Name, "DrawRain", StringComparison.Ordinal) &&
                !method.IsStatic &&
                method.ReturnType.FullName == module.TypeSystem.Void.FullName &&
                method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawRain no longer matches the verified instance void method shape.");

        if (!drawRain.HasBody || drawRain.Body.Instructions.Count == 0)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawRain has no verified method body.");
        }

        if (main.Methods.Any(method => string.Equals(method.Name, RainDrawWrapperMethodName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main already contains the Alacrity rain presentation wrapper.");
        }

        Instruction nativeDrawCall = FindNativeRainDrawCall(drawRain);
        MethodReference nativeDrawMethod = (MethodReference)nativeDrawCall.Operand;
        MethodDefinition wrapper = CreateRainDrawWrapper(module, nativeDrawMethod, tryQueueRainPresentation);
        main.Methods.Add(wrapper);
        FieldDefinition usesWorldTransform = new FieldDefinition(
            RainWorldTransformFieldName,
            FieldAttributes.Private | FieldAttributes.Static,
            module.TypeSystem.Boolean);
        main.Fields.Add(usesWorldTransform);
        PatchRainDrawCallContexts(main, drawRain, usesWorldTransform);

        VariableDefinition presentationActive = new VariableDefinition(module.TypeSystem.Boolean);
        drawRain.Body.Variables.Add(presentationActive);
        drawRain.Body.InitLocals = true;
        ILProcessor il = drawRain.Body.GetILProcessor();
        Instruction first = drawRain.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, usesWorldTransform));
        il.InsertBefore(first, il.Create(OpCodes.Call, tryBeginRainPresentation));
        il.InsertBefore(first, StoreLocal(il, presentationActive));

        // The original call has SpriteBatch plus its nine native arguments on the evaluation
        // stack. The generated static wrapper deliberately has that same stack signature.
        nativeDrawCall.OpCode = OpCodes.Call;
        nativeDrawCall.Operand = wrapper;

        List<Instruction> returns = drawRain.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ret)
            .ToList();
        if (returns.Count == 0)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawRain has no return instruction.");
        }

        foreach (Instruction returnInstruction in returns)
        {
            Instruction continueToReturn = il.Create(OpCodes.Nop);
            il.InsertBefore(returnInstruction, LoadLocal(il, presentationActive));
            il.InsertBefore(returnInstruction, il.Create(OpCodes.Brfalse, continueToReturn));
            il.InsertBefore(returnInstruction, il.Create(OpCodes.Call, endRainPresentation));
            il.InsertBefore(returnInstruction, continueToReturn);
        }
    }

    private static void PatchRainDrawCallContexts(
        TypeDefinition main,
        MethodDefinition drawRain,
        FieldDefinition usesWorldTransform)
    {
        List<Instruction> calls = new List<Instruction>();
        foreach (MethodDefinition method in main.Methods)
        {
            if (!method.HasBody || ReferenceEquals(method, drawRain))
            {
                continue;
            }

            for (int index = 0; index < method.Body.Instructions.Count; index++)
            {
                Instruction instruction = method.Body.Instructions[index];
                if (instruction.Operand is MethodReference called &&
                    string.Equals(called.FullName, drawRain.FullName, StringComparison.Ordinal))
                {
                    calls.Add(instruction);
                }
            }
        }

        if (calls.Count != 2)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 contains " + calls.Count + " DrawRain call sites; exactly the verified capture and world contexts are required.");
        }

        foreach (Instruction call in calls)
        {
            MethodDefinition? caller = call.Previous == null
                ? null
                : main.Methods.SingleOrDefault(method => method.HasBody && method.Body.Instructions.Contains(call));
            if (caller == null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 could not resolve a verified DrawRain caller.");
            }

            MethodReference begin = FindPreviousSpriteBatchBegin(call);
            bool worldTransform;
            if (begin.Parameters.Count == 0)
            {
                worldTransform = false;
            }
            else if (begin.Parameters.Count == 7)
            {
                worldTransform = true;
            }
            else
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 DrawRain caller " + caller.FullName + " does not retain a verified SpriteBatch.Begin context.");
            }

            ILProcessor il = caller.Body.GetILProcessor();
            il.InsertBefore(call, il.Create(worldTransform ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
            il.InsertBefore(call, il.Create(OpCodes.Stsfld, usesWorldTransform));
            Instruction continuation = call.Next
                ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawRain call has no continuation for context cleanup.");
            il.InsertBefore(continuation, il.Create(OpCodes.Ldc_I4_0));
            il.InsertBefore(continuation, il.Create(OpCodes.Stsfld, usesWorldTransform));
        }
    }

    private static MethodReference FindPreviousSpriteBatchBegin(Instruction drawRainCall)
    {
        for (Instruction? instruction = drawRainCall.Previous; instruction != null; instruction = instruction.Previous)
        {
            if (instruction.Operand is MethodReference called &&
                string.Equals(called.DeclaringType.FullName, "Microsoft.Xna.Framework.Graphics.SpriteBatch", StringComparison.Ordinal) &&
                string.Equals(called.Name, "Begin", StringComparison.Ordinal))
            {
                return called;
            }
        }

        throw new InvalidOperationException("Terraria 1.4.5.6 DrawRain caller no longer contains its verified SpriteBatch.Begin context.");
    }

    private static Instruction FindNativeRainDrawCall(MethodDefinition drawRain)
    {
        Instruction? match = null;
        for (int index = 0; index < drawRain.Body.Instructions.Count; index++)
        {
            Instruction instruction = drawRain.Body.Instructions[index];
            if (instruction.Operand is not MethodReference method ||
                !string.Equals(method.DeclaringType.FullName, "Microsoft.Xna.Framework.Graphics.SpriteBatch", StringComparison.Ordinal) ||
                !string.Equals(method.Name, "Draw", StringComparison.Ordinal) ||
                method.Parameters.Count != 9 ||
                method.Parameters[0].ParameterType.FullName != "Microsoft.Xna.Framework.Graphics.Texture2D" ||
                method.Parameters[1].ParameterType.FullName != "Microsoft.Xna.Framework.Vector2" ||
                method.Parameters[2].ParameterType.FullName != "System.Nullable`1<Microsoft.Xna.Framework.Rectangle>" ||
                method.Parameters[3].ParameterType.FullName != "Microsoft.Xna.Framework.Color" ||
                method.Parameters[4].ParameterType.FullName != "System.Single" ||
                method.Parameters[5].ParameterType.FullName != "Microsoft.Xna.Framework.Vector2" ||
                method.Parameters[6].ParameterType.FullName != "System.Single" ||
                method.Parameters[7].ParameterType.FullName != "Microsoft.Xna.Framework.Graphics.SpriteEffects" ||
                method.Parameters[8].ParameterType.FullName != "System.Single")
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawRain contains multiple matching SpriteBatch.Draw calls.");
            }

            match = instruction;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawRain no longer contains the verified rain SpriteBatch.Draw call.");
    }

    private static MethodDefinition CreateRainDrawWrapper(
        ModuleDefinition module,
        MethodReference nativeDrawMethod,
        MethodReference tryQueueRainPresentation)
    {
        var wrapper = new MethodDefinition(
            RainDrawWrapperMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        wrapper.Parameters.Add(new ParameterDefinition("spriteBatch", ParameterAttributes.None, module.ImportReference(nativeDrawMethod.DeclaringType)));
        for (int index = 0; index < nativeDrawMethod.Parameters.Count; index++)
        {
            ParameterDefinition parameter = nativeDrawMethod.Parameters[index];
            wrapper.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None, module.ImportReference(parameter.ParameterType)));
        }

        Instruction nativeDraw = Instruction.Create(OpCodes.Nop);
        ILProcessor il = wrapper.Body.GetILProcessor();
        for (int index = 1; index < wrapper.Parameters.Count; index++)
        {
            il.Append(il.Create(OpCodes.Ldarg, wrapper.Parameters[index]));
        }

        il.Append(il.Create(OpCodes.Call, tryQueueRainPresentation));
        il.Append(il.Create(OpCodes.Brfalse, nativeDraw));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(nativeDraw);
        for (int index = 0; index < wrapper.Parameters.Count; index++)
        {
            il.Append(il.Create(OpCodes.Ldarg, wrapper.Parameters[index]));
        }

        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(nativeDrawMethod)));
        il.Append(il.Create(OpCodes.Ret));
        return wrapper;
    }
}
