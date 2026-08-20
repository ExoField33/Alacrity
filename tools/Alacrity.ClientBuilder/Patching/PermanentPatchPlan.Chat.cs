using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    private static void PatchNativeTextInput(TypeDefinition mainType, MethodReference tryProcess, MethodReference reset)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "GetInputText" && candidate.ReturnType.FullName == "System.String" && candidate.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Boolean" }));
        var first = method.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("GetInputText has no body.");
        var result = new VariableDefinition(method.Module.TypeSystem.String);
        method.Body.InitLocals = true;
        method.Body.Variables.Add(result);
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Ldloca_S, result));
        il.InsertBefore(first, il.Create(OpCodes.Call, tryProcess));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, LoadLocal(il, result));
        il.InsertBefore(first, il.Create(OpCodes.Ret));

        var clear = mainType.Methods.Single(candidate => candidate.Name == "clrInput" && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0);
        var clearFirst = clear.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("clrInput has no body.");
        clear.Body.GetILProcessor().InsertBefore(clearFirst, clear.Body.GetILProcessor().Create(OpCodes.Call, reset));
    }

    private static void PatchNativeMenuTextPresentation(TypeDefinition mainType, MethodReference formatDisplay)
    {
        var drawMenu = mainType.Methods.Single(candidate => candidate.Name == "DrawMenu" && candidate.Parameters.Count == 1);
        var menuText = drawMenu.Body.Variables.ElementAtOrDefault(27)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu menu-text local was not found.");
        if (menuText.VariableType.FullName != "System.String[]")
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu menu-text local did not match the verified string array.");
        }

        Instruction firstMeasure = drawMenu.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Callvirt &&
            instruction.Operand is MethodReference reference &&
            reference.Name == "MeasureString" &&
            reference.Parameters.Count == 1 &&
            reference.Parameters[0].ParameterType.FullName == "System.String")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu menu-text measurement was not found.");
        int firstMeasureIndex = drawMenu.Body.Instructions.IndexOf(firstMeasure);
        var textLoads = new List<Instruction>();
        for (int index = firstMeasureIndex; index < drawMenu.Body.Instructions.Count; index++)
        {
            Instruction instruction = drawMenu.Body.Instructions[index];
            if (instruction.OpCode != OpCodes.Ldelem_Ref || instruction.Previous == null || instruction.Previous.Previous == null)
            {
                continue;
            }

            if (instruction.Previous.Previous.IsLdlocFor(menuText))
            {
                textLoads.Add(instruction);
            }
        }

        if (textLoads.Count != 4)
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu did not contain exactly four verified menu-text render loads.");
        }

        var il = drawMenu.Body.GetILProcessor();
        foreach (Instruction textLoad in textLoads)
        {
            il.InsertAfter(textLoad, il.Create(OpCodes.Castclass, drawMenu.Module.TypeSystem.String));
            il.InsertAfter(textLoad.Next, il.Create(OpCodes.Call, formatDisplay));
        }
    }

    private static void PatchNativeTextInputCaret(ModuleDefinition module, MethodReference getCaret, MethodReference drawSelection)
    {
        var textBox = CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.UI.Elements.UITextBox");
        var draw = textBox.Methods.Single(candidate => candidate.Name == "DrawSelf" && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 1 && candidate.Parameters[0].ParameterType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch");
        var cursor = textBox.Fields.Single(field => field.Name == "_cursor" && field.FieldType.FullName == "System.Int32");
        var assignment = draw.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Stfld &&
            instruction.Operand is FieldReference field &&
            field.FullName == cursor.FullName)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UITextBox cursor assignment was not found.");
        var length = assignment.Previous;
        var textGetter = length?.Previous;
        var textInstance = textGetter?.Previous;
        var fieldInstance = textInstance?.Previous;
        if (fieldInstance == null || textInstance == null || textGetter == null || length == null ||
            fieldInstance.OpCode != OpCodes.Ldarg_0 ||
            textInstance.OpCode != OpCodes.Ldarg_0 ||
            textGetter.Operand is not MethodReference getText || getText.Name != "get_Text" || getText.ReturnType.FullName != "System.String" ||
            (length.OpCode != OpCodes.Call && length.OpCode != OpCodes.Callvirt) ||
            length.Operand is not MethodReference getLength || getLength.Name != "get_Length" || getLength.DeclaringType.FullName != "System.String")
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 UITextBox cursor setup did not match the verified Text.Length assignment.");
        }

        var il = draw.Body.GetILProcessor();
        il.InsertBefore(fieldInstance, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(fieldInstance, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(fieldInstance, il.Create(OpCodes.Call, getText));
        il.InsertBefore(fieldInstance, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(fieldInstance, il.Create(OpCodes.Call, getCaret));
        il.InsertBefore(fieldInstance, il.Create(OpCodes.Stfld, cursor));

        fieldInstance.OpCode = OpCodes.Nop;
        textInstance.OpCode = OpCodes.Nop;
        textGetter.OpCode = OpCodes.Nop;
        length.OpCode = OpCodes.Nop;
        assignment.OpCode = OpCodes.Nop;

        var baseDraw = draw.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference reference &&
            reference.Name == "DrawSelf" &&
            reference.DeclaringType.FullName.StartsWith("Terraria.GameContent.UI.Elements.UITextPanel`1", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UITextBox base draw call was not found.");
        var textPosition = draw.Body.Instructions.FirstOrDefault(instruction =>
            instruction.Operand is MethodReference reference &&
            reference.Name == "get_TextDrawPosition" &&
            reference.ReturnType.FullName == "Microsoft.Xna.Framework.Vector2")?.Operand as MethodReference
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UITextBox text-position getter was not found.");
        var font = draw.Body.Instructions.FirstOrDefault(instruction =>
            instruction.Operand is MethodReference reference &&
            reference.Name == "get_Font" &&
            reference.ReturnType.FullName == "ReLogic.Graphics.DynamicSpriteFont")?.Operand as MethodReference
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UITextBox font getter was not found.");
        var textScale = draw.Body.Instructions.FirstOrDefault(instruction =>
            instruction.Operand is MethodReference reference &&
            reference.Name == "get_TextScale" &&
            reference.ReturnType.FullName == "System.Single")?.Operand as MethodReference
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UITextBox text-scale getter was not found.");
        var afterBaseDraw = baseDraw.Next ?? throw new InvalidOperationException("Terraria 1.4.5.6 UITextBox base draw has no continuation.");
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Call, getText));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Callvirt, textPosition));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Call, font));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Call, textScale));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(afterBaseDraw, il.Create(OpCodes.Call, drawSelection));

    }

    private static void PatchChatInputActionOwnership(TypeDefinition mainType, MethodReference handlesInputAction)
    {
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

    private static void PatchPluginChatCommands(TypeDefinition mainType, MethodReference tryHandlePluginCommand, MethodReference recordSubmittedChatInput, MethodReference tryDeferOutgoingMessage, MethodReference hasReadyOutgoingMessage)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "DoUpdate_HandleChat" && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0);
        var chatText = mainType.Fields.Single(field => field.Name == "chatText" && field.FieldType.FullName == "System.String");
        var chatRelease = mainType.Fields.Single(field => field.Name == "chatRelease" && field.FieldType.FullName == "System.Boolean");
        var inputTextEnter = mainType.Fields.Single(field => field.Name == "inputTextEnter" && field.FieldType.FullName == "System.Boolean");
        var submitCheck = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.FullName == chatText.FullName &&
            instruction.Next?.OpCode == OpCodes.Ldstr && (string)instruction.Next.Operand == string.Empty &&
            instruction.Next.Next?.Operand is MethodReference comparison && comparison.Name == "op_Inequality")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat outgoing-message check was not found.");
        var inputSubmitGate = method.Body.Instructions.TakeWhile(instruction => instruction != submitCheck).FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldsfld &&
            instruction.Operand is FieldReference field &&
            field.FullName == inputTextEnter.FullName &&
            instruction.Next != null &&
            (instruction.Next.OpCode == OpCodes.Brfalse || instruction.Next.OpCode == OpCodes.Brfalse_S))
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat native input submit gate was not found.");
        var closeChat = method.Body.Instructions.SkipWhile(instruction => instruction != submitCheck).FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == string.Empty &&
            instruction.Next?.OpCode == OpCodes.Stsfld && instruction.Next.Operand is FieldReference field && field.FullName == chatText.FullName)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat close-chat path was not found.");
        var il = method.Body.GetILProcessor();
        var nativeSubmitGate = il.Create(OpCodes.Nop);
        il.InsertBefore(inputSubmitGate, il.Create(OpCodes.Call, hasReadyOutgoingMessage));
        il.InsertBefore(inputSubmitGate, il.Create(OpCodes.Brfalse, nativeSubmitGate));
        il.InsertBefore(inputSubmitGate, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(inputSubmitGate, il.Create(OpCodes.Stsfld, inputTextEnter));
        il.InsertBefore(inputSubmitGate, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(inputSubmitGate, il.Create(OpCodes.Stsfld, chatRelease));
        il.InsertBefore(inputSubmitGate, nativeSubmitGate);
        var continueSubmission = il.Create(OpCodes.Nop);
        il.InsertBefore(submitCheck, il.Create(OpCodes.Ldsfld, chatText));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Call, tryDeferOutgoingMessage));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Brfalse, continueSubmission));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Stsfld, chatRelease));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Ret));
        il.InsertBefore(submitCheck, continueSubmission);
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

    private static void PatchBetterChatDraw(TypeDefinition mainType, MethodReference format, MethodReference drawSelection, MethodReference drawActionStrip)
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

        var drawInput = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference reference &&
            reference.Name == "DrawColorCodedStringWithShadow" &&
            reference.DeclaringType.FullName == "Terraria.UI.Chat.ChatManager")
            ?? throw new InvalidOperationException("Could not locate Terraria's DrawPlayerChat editable-text draw call.");
        var spriteBatch = mainType.Fields.Single(field => field.Name == "spriteBatch" && field.FieldType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch");
        il.InsertBefore(drawInput, il.Create(OpCodes.Ldsfld, spriteBatch));
        il.InsertBefore(drawInput, il.Create(OpCodes.Ldsfld, chatText));
        il.InsertBefore(drawInput, il.Create(OpCodes.Call, drawSelection));

        var drawChat = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Callvirt && instruction.Operand is MethodReference reference &&
            reference.Name == "DrawChat" && reference.DeclaringType.FullName == "Terraria.GameContent.UI.Chat.IChatMonitor")
            ?? throw new InvalidOperationException("Could not locate Terraria's DrawPlayerChat chat-monitor draw call.");
        method.Body.GetILProcessor().InsertAfter(drawChat, method.Body.GetILProcessor().Create(OpCodes.Call, drawActionStrip));
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

    private static void PatchStoredChatMessageDecoration(TypeDefinition chatContainer, MethodReference begin, MethodReference prepare, MethodReference end)
    {
        var refresh = chatContainer.Methods.Single(candidate => candidate.Name == "Refresh" && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0 && candidate.HasBody);
        var wordwrap = refresh.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference reference &&
            reference.Name == "WordwrapStringSmart" && reference.DeclaringType.FullName == "Terraria.Utils")
            ?? throw new InvalidOperationException("ChatMessageContainer.Refresh did not contain the verified WordwrapStringSmart call.");

        int wordwrapCount = refresh.Body.Instructions.Count(instruction =>
            instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference reference &&
            reference.Name == "WordwrapStringSmart" && reference.DeclaringType.FullName == "Terraria.Utils");
        if (wordwrapCount != 1)
        {
            throw new InvalidOperationException("ChatMessageContainer.Refresh did not contain exactly one verified WordwrapStringSmart call.");
        }

        var originalTextLoad = wordwrap.Previous;
        while (originalTextLoad != null && !(originalTextLoad.OpCode == OpCodes.Ldfld && originalTextLoad.Operand is FieldReference field && field.Name == "OriginalText" && field.DeclaringType.FullName == chatContainer.FullName))
        {
            originalTextLoad = originalTextLoad.Previous;
        }

        if (originalTextLoad == null || originalTextLoad.Previous == null || originalTextLoad.Previous.OpCode != OpCodes.Ldarg_0)
        {
            throw new InvalidOperationException("ChatMessageContainer.Refresh did not contain the verified OriginalText load before WordwrapStringSmart.");
        }

        var resultStore = wordwrap.Next;
        if (resultStore == null || !IsStoreLocal(resultStore))
        {
            throw new InvalidOperationException("ChatMessageContainer.Refresh did not store the verified WordwrapStringSmart result.");
        }

        if (wordwrap.Next != resultStore)
        {
            throw new InvalidOperationException("ChatMessageContainer.Refresh did not immediately store the verified WordwrapStringSmart result.");
        }

        var il = refresh.Body.GetILProcessor();
        il.InsertBefore(originalTextLoad.Previous, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(originalTextLoad.Previous, il.Create(OpCodes.Call, begin));
        il.InsertAfter(originalTextLoad, il.Create(OpCodes.Ldarg_0));
        il.InsertAfter(originalTextLoad.Next, il.Create(OpCodes.Call, prepare));
        il.InsertAfter(resultStore, il.Create(OpCodes.Call, end));
    }

    private static void PatchStoredChatMessagePresentationRefresh(TypeDefinition remadeChatMonitor, MethodReference refreshPresentations)
    {
        var update = remadeChatMonitor.Methods.Single(candidate =>
            candidate.Name == "Update" &&
            candidate.ReturnType.FullName == "System.Void" &&
            candidate.Parameters.Count == 0 &&
            candidate.HasBody);
        var first = update.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("RemadeChatMonitor.Update has no verified body.");
        update.Body.GetILProcessor().InsertBefore(first, update.Body.GetILProcessor().Create(OpCodes.Call, refreshPresentations));
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
