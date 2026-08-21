using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    private static void PatchBannerSearch(TypeDefinition bannerClaimingUi, MethodReference shouldDisplay, MethodReference drawSearch, MethodReference keepMenuAvailable)
    {
        MethodDefinition updateEntries = bannerClaimingUi.Methods.Single(candidate =>
            candidate.Name == "UpdateAndGetClaimableItemsCount" &&
            candidate.ReturnType.FullName == "System.Int32" &&
            candidate.Parameters.Count == 0);
        FieldDefinition entryIndex = bannerClaimingUi.NestedTypes.Single(candidate => candidate.Name == "Entry")
            .Fields.Single(field => field.Name == "Index" && field.FieldType.FullName == "System.Int32");
        Instruction entryIndexStore = updateEntries.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Stfld &&
            instruction.Operand is FieldReference field &&
            field.FullName == entryIndex.FullName);
        VariableDefinition bannerIndex = GetLoadedLocalVariable(updateEntries, entryIndexStore.Previous)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 BannerClaimingUI banner index local was not found.");
        Instruction positiveCountBranch = updateEntries.Body.Instructions.Single(instruction =>
            (instruction.OpCode == OpCodes.Ble || instruction.OpCode == OpCodes.Ble_S) &&
            instruction.Previous?.OpCode == OpCodes.Ldc_I4_0 &&
            GetLoadedLocalVariable(updateEntries, instruction.Previous.Previous) != null &&
            instruction.Operand is Instruction);
        Instruction nativeSkip = (Instruction)positiveCountBranch.Operand;
        Instruction continueWithEntry = positiveCountBranch.Next
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 BannerClaimingUI claimable-entry branch has no continuation.");
        var updateIl = updateEntries.Body.GetILProcessor();
        Instruction loadIndex = LoadLocal(updateIl, bannerIndex);
        Instruction invokeFilter = updateIl.Create(OpCodes.Call, shouldDisplay);
        Instruction keepEntry = updateIl.Create(OpCodes.Brtrue, continueWithEntry);
        Instruction skipEntry = updateIl.Create(OpCodes.Br, nativeSkip);
        updateIl.InsertAfter(positiveCountBranch, loadIndex);
        updateIl.InsertAfter(loadIndex, invokeFilter);
        updateIl.InsertAfter(invokeFilter, keepEntry);
        updateIl.InsertAfter(keepEntry, skipEntry);

        Instruction availabilitySet = updateEntries.Body.Instructions.Single(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand is MethodReference reference &&
            reference.DeclaringType.FullName == "Terraria.UI.BannerClaimingUI" &&
            reference.Name == "set_AnyAvailableBanners" &&
            reference.Parameters.Count == 1 &&
            reference.Parameters[0].ParameterType.FullName == "System.Boolean");
        MethodReference setAvailability = (MethodReference)availabilitySet.Operand;
        Instruction continueAfterAvailability = availabilitySet.Next
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 BannerClaimingUI availability assignment has no continuation.");
        Instruction keepMenu = updateIl.Create(OpCodes.Call, keepMenuAvailable);
        Instruction preserveNativeAvailability = updateIl.Create(OpCodes.Brfalse, continueAfterAvailability);
        Instruction loadThis = updateIl.Create(OpCodes.Ldarg_0);
        Instruction loadTrue = updateIl.Create(OpCodes.Ldc_I4_1);
        Instruction setAvailable = updateIl.Create(OpCodes.Call, setAvailability);
        updateIl.InsertAfter(availabilitySet, keepMenu);
        updateIl.InsertAfter(keepMenu, preserveNativeAvailability);
        updateIl.InsertAfter(preserveNativeAvailability, loadThis);
        updateIl.InsertAfter(loadThis, loadTrue);
        updateIl.InsertAfter(loadTrue, setAvailable);

        PatchBannerSearchListField(
            bannerClaimingUi.Methods.Single(candidate => candidate.Name == "DrawBannersList" && candidate.Parameters.Count == 4),
            drawSearch,
            82,
            388);
    }

    private static void PatchBannerSearchListField(MethodDefinition method, MethodReference drawSearch, int x, int y)
    {
        Instruction updateClaimableCount = method.Body.Instructions.Single(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand is MethodReference reference &&
            reference.DeclaringType.FullName == "Terraria.UI.BannerClaimingUI" &&
            reference.Name == "UpdateAndGetClaimableItemsCount" &&
            reference.Parameters.Count == 0);
        Instruction storedCount = updateClaimableCount.Next;
        if (storedCount == null || !IsStoreLocal(storedCount))
        {
            throw new InvalidOperationException("Terraria 1.4.5.6 BannerClaimingUI did not store its refreshed claimable count.");
        }

        var il = method.Body.GetILProcessor();
        Instruction loadSpriteBatch = il.Create(OpCodes.Ldarg_1);
        Instruction loadX = il.Create(OpCodes.Ldc_I4, x);
        Instruction loadY = il.Create(OpCodes.Ldc_I4, y);
        Instruction loadListOffset = il.Create(OpCodes.Ldarg_2);
        Instruction addListOffset = il.Create(OpCodes.Add);
        il.InsertAfter(storedCount, loadSpriteBatch);
        il.InsertAfter(loadSpriteBatch, loadX);
        il.InsertAfter(loadX, loadY);
        il.InsertAfter(loadY, loadListOffset);
        il.InsertAfter(loadListOffset, addListOffset);
        il.InsertAfter(addListOffset, il.Create(OpCodes.Call, drawSearch));
    }

}
