using Mono.Cecil;
using Mono.Cecil.Cil;

// Version-locked local presentation gate. The endpoint sparkle and item icon are optional
// presentation; native Paladin shield range arcs and all gameplay logic remain untouched.
internal static partial class PermanentPatchPlan
{
    private static void PatchPaladinShieldIcon(TypeDefinition mainType, MethodReference shouldDrawPaladinShieldIcon)
    {
        MethodDefinition boundary = mainType.Methods.SingleOrDefault(method =>
            method.Name == "DrawPaladinsShieldBoundary" &&
            !method.IsStatic &&
            method.ReturnType.FullName == "System.Void" &&
            method.Parameters.Count == 2 &&
            method.Parameters[0].ParameterType.FullName == "Microsoft.Xna.Framework.Vector2" &&
            method.Parameters[1].ParameterType.FullName == "Microsoft.Xna.Framework.Vector2")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary signature did not match the verified Paladin shield icon boundary.");

        if (!boundary.HasBody)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary has no body.");
        }

        if (boundary.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called &&
            string.Equals(called.Name, shouldDrawPaladinShieldIcon.Name, StringComparison.Ordinal) &&
            string.Equals(called.DeclaringType.FullName, shouldDrawPaladinShieldIcon.DeclaringType.FullName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary already contains the Paladin shield icon gate.");
        }

        Instruction iconLoad = FindPaladinShieldIconLoad(boundary);
        Instruction indicatorStart = FindPaladinShieldIndicatorStart(boundary, iconLoad);
        Instruction iconDraw = FindPaladinShieldIconDraw(iconLoad);
        Instruction continuation = iconDraw.Next
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Paladin shield icon draw has no continuation.");

        ILProcessor il = boundary.Body.GetILProcessor();
        // The branch starts before the native sparkle and icon argument sequences. Branching
        // after either sequence has pushed values would leave stack entries at the return target
        // and produces invalid IL that Terraria discovers in its background ForceJIT pass.
        il.InsertBefore(indicatorStart, il.Create(OpCodes.Call, shouldDrawPaladinShieldIcon));
        il.InsertBefore(indicatorStart, il.Create(OpCodes.Brfalse, continuation));
    }

    private static Instruction FindPaladinShieldIconLoad(MethodDefinition boundary)
    {
        Instruction? match = null;
        for (int index = 0; index < boundary.Body.Instructions.Count; index++)
        {
            Instruction instruction = boundary.Body.Instructions[index];
            if (instruction.Operand is not MethodReference called ||
                !string.Equals(called.Name, "LoadItem", StringComparison.Ordinal) ||
                !string.Equals(called.DeclaringType.FullName, "Terraria.Main", StringComparison.Ordinal) ||
                called.Parameters.Count != 1 ||
                called.Parameters[0].ParameterType.FullName != "System.Int32")
            {
                continue;
            }

            Instruction? previous = instruction.Previous;
            while (previous != null && previous.OpCode == OpCodes.Nop)
            {
                previous = previous.Previous;
            }

            if (previous == null || !IsLoadInt(previous, 938))
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary contains multiple verified LoadItem(938) icon anchors.");
            }

            match = instruction;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary no longer contains the verified LoadItem(938) icon anchor.");
    }

    private static Instruction FindPaladinShieldIconDraw(Instruction iconLoad)
    {
        Instruction? match = null;
        for (Instruction? instruction = iconLoad.Next; instruction != null; instruction = instruction.Next)
        {
            if (instruction.Operand is not MethodReference called ||
                !string.Equals(called.Name, "Draw", StringComparison.Ordinal) ||
                !string.Equals(called.DeclaringType.FullName, "Microsoft.Xna.Framework.Graphics.SpriteBatch", StringComparison.Ordinal) ||
                called.Parameters.Count != 9 ||
                called.Parameters[0].ParameterType.FullName != "Microsoft.Xna.Framework.Graphics.Texture2D")
            {
                continue;
            }

            if (match != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary contains multiple SpriteBatch draws after the verified Paladin shield icon anchor.");
            }

            match = instruction;
        }

        return match ?? throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary no longer contains the verified Paladin shield icon SpriteBatch.Draw call.");
    }

    private static Instruction FindPaladinShieldIndicatorStart(MethodDefinition boundary, Instruction iconLoad)
    {
        Instruction? sparkle = null;
        for (Instruction? instruction = iconLoad.Previous; instruction != null; instruction = instruction.Previous)
        {
            if (instruction.Operand is not MethodReference called ||
                !string.Equals(called.Name, "DrawPrettyStarSparkle", StringComparison.Ordinal) ||
                !string.Equals(called.DeclaringType.FullName, "Terraria.Main", StringComparison.Ordinal))
            {
                continue;
            }

            if (sparkle != null)
            {
                throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary contains multiple sparkle calls before the Paladin shield endpoint icon.");
            }

            sparkle = instruction;
        }

        if (sparkle == null)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 Main.DrawPaladinsShieldBoundary no longer contains the verified Paladin shield endpoint sparkle.");
        }

        for (Instruction? instruction = sparkle.Previous; instruction != null; instruction = instruction.Previous)
        {
            if (instruction.OpCode.FlowControl != FlowControl.Cond_Branch ||
                instruction.Operand is not Instruction loopTarget ||
                loopTarget.Offset >= instruction.Offset)
            {
                continue;
            }

            Instruction? indicatorStart = instruction.Next;
            if (indicatorStart == null)
            {
                break;
            }

            return indicatorStart;
        }

        throw new InvalidOperationException("Terraria 1.4.5.6 Paladin shield endpoint sparkle no longer follows the verified loop-exit boundary.");
    }
}
