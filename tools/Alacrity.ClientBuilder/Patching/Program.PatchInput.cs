using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    private static void PatchPluginKeybindControls(ModuleDefinition module, MethodReference appendPluginKeybindControls)
    {
        var controls = module.Types.SingleOrDefault(type => type.FullName == "Terraria.GameContent.UI.States.UIManageControls")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UIManageControls type was not found.");
        var initialize = controls.Methods.SingleOrDefault(method => method.Name == "OnInitialize" && !method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UIManageControls.OnInitialize signature did not match the verified keybind-controls hook.");
        var finalReturn = initialize.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UIManageControls.OnInitialize has no return instruction.");
        var il = initialize.Body.GetILProcessor();
        CecilPatchPrimitives.InsertBefore(
            il,
            finalReturn,
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Call, appendPluginKeybindControls));
    }

    private static void PatchPluginInput(TypeDefinition mainType, MethodReference handleInput, MethodReference updatePluginKeybinds)
    {
        var method = mainType.Methods.Single(m => m.Name == "DoUpdate_HandleInput");
        var il = method.Body.GetILProcessor();
        var ret = method.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);

        // This is the verified post-input boundary. Keybind dispatch is kept out of rendering.
        il.InsertBefore(ret, il.Create(OpCodes.Call, updatePluginKeybinds));

        // The helper returns true when vanilla input should continue. Returning false
        // leaves Terraria's original input path untouched otherwise.
        il.InsertBefore(ret, il.Create(OpCodes.Call, handleInput));
        il.InsertBefore(ret, il.Create(OpCodes.Brtrue, ret));
    }

    private static void PatchPluginKeybindStateShape(ModuleDefinition module, MethodReference ensurePluginKeybindStateShape)
    {
        var playerInput = module.Types.SelectMany(Flatten).SingleOrDefault(type => type.FullName == "Terraria.GameInput.PlayerInput")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 PlayerInput type was not found.");
        var updateInput = playerInput.Methods.SingleOrDefault(method => method.Name == "UpdateInput" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 PlayerInput.UpdateInput signature did not match the verified keybind-state hook.");
        var first = updateInput.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference target && target.FullName == "System.Void Terraria.GameInput.PlayerInput/SettingsForUI::UpdateCounters()")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 PlayerInput.UpdateInput did not contain the verified input-update entry pattern.");

        // CopyKeyState directly indexes trigger dictionaries, so shape them before Terraria resets/copies input state.
        updateInput.Body.GetILProcessor().InsertBefore(first, updateInput.Body.GetILProcessor().Create(OpCodes.Call, ensurePluginKeybindStateShape));
    }
}
