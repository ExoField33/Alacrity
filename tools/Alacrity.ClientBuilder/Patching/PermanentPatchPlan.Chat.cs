using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    private static void PatchBetterChatInput(TypeDefinition mainType, MethodReference isActive, MethodReference process, MethodReference handlesInputAction)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "GetInputText" && candidate.ReturnType.FullName == "System.String" && candidate.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Boolean" }));
        var drawingChat = mainType.Fields.Single(field => field.Name == "drawingPlayerChat" && field.FieldType.FullName == "System.Boolean");
        var first = method.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("GetInputText has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, drawingChat));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Call, isActive));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, process));
        il.InsertBefore(first, il.Create(OpCodes.Ret));

        var updateChat = mainType.Methods.Single(candidate => candidate.Name == "DoUpdate_HandleChat" && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0);
        var nativeOffset = updateChat.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference target &&
            target.Name == "Offset" &&
            target.DeclaringType.FullName == "Terraria.GameContent.UI.Chat.IChatMonitor")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat native chat offset call was not found.");
        var upKeyCheck = updateChat.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldsflda &&
            instruction.Operand is FieldReference field &&
            field.Name == "keyState" &&
            instruction.Next?.OpCode == OpCodes.Ldc_I4_S &&
            Convert.ToInt32(instruction.Next.Operand) == 38)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat native Up navigation branch was not found.");
        var downKeyCheck = updateChat.Body.Instructions.FirstOrDefault(instruction =>
            instruction.Offset > upKeyCheck.Offset &&
            instruction.OpCode == OpCodes.Ldsflda &&
            instruction.Operand is FieldReference field &&
            field.Name == "keyState" &&
            instruction.Next?.OpCode == OpCodes.Ldc_I4_S &&
            Convert.ToInt32(instruction.Next.Operand) == 40)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat native Down navigation branch was not found.");
        var navigationIl = updateChat.Body.GetILProcessor();
        // Vanilla treats Up and Down as independent branches. Preserve that structure: owning Up
        // only skips the native Up branch, while a non-owned Down branch still reaches vanilla.
        navigationIl.InsertBefore(upKeyCheck, navigationIl.Create(OpCodes.Ldstr, "up"));
        navigationIl.InsertBefore(upKeyCheck, navigationIl.Create(OpCodes.Call, handlesInputAction));
        navigationIl.InsertBefore(upKeyCheck, navigationIl.Create(OpCodes.Brtrue, downKeyCheck));
        navigationIl.InsertBefore(downKeyCheck, navigationIl.Create(OpCodes.Ldstr, "down"));
        navigationIl.InsertBefore(downKeyCheck, navigationIl.Create(OpCodes.Call, handlesInputAction));
        navigationIl.InsertBefore(downKeyCheck, navigationIl.Create(OpCodes.Brtrue, nativeOffset));
    }

    private static void PatchPluginChatCommands(TypeDefinition mainType, MethodReference tryHandlePluginCommand, MethodReference recordSubmittedChatInput)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "DoUpdate_HandleChat" && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0);
        var chatText = mainType.Fields.Single(field => field.Name == "chatText" && field.FieldType.FullName == "System.String");
        var submitCheck = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.FullName == chatText.FullName &&
            instruction.Next?.OpCode == OpCodes.Ldstr && (string)instruction.Next.Operand == string.Empty &&
            instruction.Next.Next?.Operand is MethodReference comparison && comparison.Name == "op_Inequality")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat outgoing-message check was not found.");
        var closeChat = method.Body.Instructions.SkipWhile(instruction => instruction != submitCheck).FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == string.Empty &&
            instruction.Next?.OpCode == OpCodes.Stsfld && instruction.Next.Operand is FieldReference field && field.FullName == chatText.FullName)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat close-chat path was not found.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(submitCheck, il.Create(OpCodes.Ldsfld, chatText));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Call, recordSubmittedChatInput));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Ldsfld, chatText));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Call, tryHandlePluginCommand));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Brfalse, submitCheck));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Br, closeChat));
    }

    private static void PatchBetterChatStartup(TypeDefinition programType, MethodReference bootstrap)
    {
        var launchGame = programType.Methods.Single(method => method.Name == "LaunchGame" && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String[]", "System.Boolean" }));
        var first = launchGame.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("Terraria.Program.LaunchGame has no body.");
        launchGame.Body.GetILProcessor().InsertBefore(first, launchGame.Body.GetILProcessor().Create(OpCodes.Call, bootstrap));
    }

    private static void PatchBetterChatDraw(TypeDefinition mainType, MethodReference format)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "DrawPlayerChat" && !candidate.IsStatic && candidate.Parameters.Count == 0);
        var chatText = mainType.Fields.Single(field => field.Name == "chatText" && field.FieldType.FullName == "System.String");
        var textLocal = method.Body.Variables.ElementAtOrDefault(2) ?? throw new InvalidOperationException("DrawPlayerChat local layout is not the verified 1.4.5.6 layout.");
        if (textLocal.VariableType.FullName != "System.String")
            throw new InvalidOperationException("DrawPlayerChat text local did not match the verified 1.4.5.6 layout.");
        var load = method.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.FullName == chatText.FullName && instruction.Next != null && instruction.Next.IsStlocFor(textLocal))
            ?? throw new InvalidOperationException("Could not locate the verified DrawPlayerChat chatText capture.");
        var il = method.Body.GetILProcessor();
        il.InsertAfter(load, il.Create(OpCodes.Call, format));

        var cursor = method.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == "|")
            ?? throw new InvalidOperationException("Could not locate Terraria's DrawPlayerChat cursor literal.");
        var start = cursor;
        while (start.Previous != null && start.OpCode != OpCodes.Ldarg_0)
            start = start.Previous;
        var end = cursor;
        while (end.Next != null && !(end.OpCode == OpCodes.Callvirt && end.Operand is MethodReference reference && reference.Name == "Add"))
            end = end.Next;
        if (end.Next == null)
            throw new InvalidOperationException("Could not locate the verified DrawPlayerChat cursor append.");
        for (var current = start; current != end.Next; current = current.Next)
            current.OpCode = OpCodes.Nop;
    }

    private static void PatchBetterChatSnippet(TypeDefinition snippets, TypeDefinition chatManager, MethodReference hover, MethodReference click, MethodReference color, MethodReference copyContext)
    {
        var visible = snippets.Methods.Single(method => method.Name == "GetVisibleColor" && method.ReturnType.FullName == "Microsoft.Xna.Framework.Color" && method.Parameters.Count == 0);
        var wave = chatManager.Methods.Single(method => method.Name == "WaveColor" && method.ReturnType.FullName == "Microsoft.Xna.Framework.Color" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "Microsoft.Xna.Framework.Color");
        var colorField = snippets.Fields.Single(field => field.Name == "Color" && field.FieldType.FullName == "Microsoft.Xna.Framework.Color");
        ReplaceBody(visible, il =>
        {
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, colorField));
            il.Append(il.Create(OpCodes.Call, wave));
            il.Append(il.Create(OpCodes.Call, color));
            il.Append(il.Create(OpCodes.Ret));
        });
        var onHover = snippets.Methods.Single(method => method.Name == "OnHover" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0);
        ReplaceBody(onHover, il => { il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Call, hover)); il.Append(il.Create(OpCodes.Ret)); });
        var onClick = snippets.Methods.Single(method => method.Name == "OnClick" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0);
        ReplaceBody(onClick, il => { il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Call, click)); il.Append(il.Create(OpCodes.Pop)); il.Append(il.Create(OpCodes.Ret)); });

        var copyMorph = snippets.Methods.Single(method => method.Name == "CopyMorph" && method.ReturnType.FullName == snippets.FullName && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.String");
        var result = new VariableDefinition(copyMorph.ReturnType);
        copyMorph.Body.InitLocals = true;
        copyMorph.Body.Variables.Add(result);
        var returnInstruction = copyMorph.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret)
            ?? throw new InvalidOperationException("TextSnippet.CopyMorph has no return instruction.");
        var copyIl = copyMorph.Body.GetILProcessor();
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Stloc, result));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Ldarg_0));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Ldloc, result));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Call, copyContext));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Ldloc, result));
    }

    private static void PatchBetterChatParse(TypeDefinition chatManager, MethodReference decorate)
    {
        var method = chatManager.Methods.Single(candidate => candidate.Name == "ParseMessage" && candidate.ReturnType.FullName == "System.Collections.Generic.List`1<Terraria.UI.Chat.TextSnippet>" && candidate.Parameters.Count == 2);
        if (method.Parameters[0].ParameterType.FullName != "System.String" || method.Parameters[1].ParameterType.FullName != "Microsoft.Xna.Framework.Color")
            throw new InvalidOperationException("ParseMessage did not match the verified 1.4.5.6 signature.");
        var ret = method.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret) ?? throw new InvalidOperationException("ParseMessage has no return.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, decorate));
        il.InsertBefore(ret, il.Create(OpCodes.Castclass, method.ReturnType));
    }

    private static void PatchBetterChatVisibility(ModuleDefinition module, MethodReference networkVisibility, MethodReference localVisibility)
    {
        var chatHelper = module.Types.SelectMany(Flatten).Single(type => type.FullName == "Terraria.Chat.ChatHelper");
        var display = chatHelper.Methods.Single(method => method.Name == "DisplayMessage" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "Terraria.Localization.NetworkText", "Microsoft.Xna.Framework.Color", "System.Byte" }));
        InsertDisplayGate(display, networkVisibility, 2, "ChatHelper.DisplayMessage");

        var main = module.Types.Single(type => type.FullName == "Terraria.Main");
        var newText = main.Methods.Single(method => method.Name == "NewText" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Byte", "System.Byte", "System.Byte" }));
        InsertDisplayGate(newText, localVisibility, null, "Main.NewText");
        var newTextMultiline = main.Methods.Single(method => method.Name == "NewTextMultiline" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Boolean", "Microsoft.Xna.Framework.Color", "System.Int32" }));
        InsertDisplayGate(newTextMultiline, localVisibility, null, "Main.NewTextMultiline");
    }

    private static void InsertDisplayGate(MethodDefinition method, MethodReference gate, int? argumentIndex, string methodName)
    {
        if (!method.HasBody || method.Body.Instructions.Count == 0)
            throw new InvalidOperationException(methodName + " has no verified method body.");
        var first = method.Body.Instructions[0];
        var il = method.Body.GetILProcessor();
        if (argumentIndex.HasValue)
            il.InsertBefore(first, il.Create(OpCodes.Ldarg, argumentIndex.Value));
        il.InsertBefore(first, il.Create(OpCodes.Call, gate));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }
}
