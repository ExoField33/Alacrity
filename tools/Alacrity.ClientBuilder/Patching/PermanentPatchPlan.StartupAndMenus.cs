using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    private static string ReadAlacrityVersion(string exePath)
    {
        var versionPath = Path.Combine(Path.GetDirectoryName(exePath)!, "VERSION");
        if (!File.Exists(versionPath))
            throw new InvalidOperationException($"Missing Alacrity version file: {versionPath}");

        var version = File.ReadAllText(versionPath).Trim();
        if (!Version.TryParse(version, out _))
            throw new InvalidOperationException($"Alacrity version must be numeric (for example 0.1.0): {versionPath}");

        return "Alacrity v" + version;
    }

    private static void PatchAlacrityVersionDraw(TypeDefinition mainType, MethodReference drawAlacrityVersion, string versionText)
    {
        var drawMenu = mainType.Methods.Single(method => method.Name == "DrawMenu" && method.Parameters.Count == 1);
        var color = drawMenu.Body.Variables[3];
        var verticalOffset = drawMenu.Body.Variables[31];
        if (color.VariableType.FullName != "Microsoft.Xna.Framework.Color" || verticalOffset.VariableType.FullName != "System.Single")
            throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu version display locals did not match the verified layout.");

        var versionDraw = drawMenu.Body.Instructions.SingleOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference reference &&
            reference.FullName == "System.Void Terraria.Main::DrawVersionNumber(Microsoft.Xna.Framework.Color,System.Single)")
            ?? throw new InvalidOperationException("Could not find Terraria's verified version-number draw call.");

        var il = drawMenu.Body.GetILProcessor();
        var insertAfter = versionDraw;
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldloc, color));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldloc, verticalOffset));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldc_R4, 22f));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Add));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldstr, versionText));
        InsertAfter(il, insertAfter, il.Create(OpCodes.Call, drawAlacrityVersion));
    }

    private static void PatchTerrariaVersionLabels(TypeDefinition mainType)
    {
        var constructor = mainType.Methods.Single(method => method.Name == ".cctor" && method.IsStatic);
        var labels = new[] { "versionNumber", "versionNumber2" };
        foreach (string fieldName in labels)
        {
            var field = mainType.Fields.Single(candidate => candidate.Name == fieldName && candidate.FieldType.FullName == "System.String");
            var assignment = constructor.Body.Instructions.SingleOrDefault(instruction =>
                instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference reference && reference.Resolve() == field)
                ?? throw new InvalidOperationException("Could not find Terraria's verified " + fieldName + " assignment.");
            var value = assignment.Previous;
            if (value == null || value.OpCode != OpCodes.Ldstr || !string.Equals((string)value.Operand, "v1.4.5.6", StringComparison.Ordinal))
                throw new InvalidOperationException("Terraria 1.4.5.6 " + fieldName + " label did not match the verified value.");
            value.Operand = "Terraria v1.4.5.6";
        }
    }

    private static void PatchPluginMenuEntry(ModuleDefinition module, TypeDefinition mainType, MethodReference openPluginManager)
    {
        var drawMenu = mainType.Methods.Single(m => m.Name == "DrawMenu" && m.Parameters.Count == 1);
        var il = drawMenu.Body.GetILProcessor();
        var stringArray = drawMenu.Body.Variables[27];
        var menuItemCount = drawMenu.Body.Variables[9];
        var menuIndex = drawMenu.Body.Variables[45];
        if (stringArray.VariableType.FullName != "System.String[]" || menuItemCount.VariableType.FullName != "System.Int32" || menuIndex.VariableType.FullName != "System.Int32")
            throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu local layout did not match the verified plugin insertion boundary.");

        var workshopType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.Social.SocialAPI");
        var workshopField = workshopType.Fields.Single(f => f.Name == "Workshop");
        var selectedMenu = mainType.Fields.Single(f => f.Name == "selectedMenu");
        var workshopLoad = drawMenu.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldsfld && i.Operand == workshopField)
            ?? throw new InvalidOperationException("Could not find the verified SocialAPI.Workshop menu boundary.");
        var workshopIndex = drawMenu.Body.Instructions.IndexOf(workshopLoad);
        var originalItemCount = drawMenu.Body.Instructions.Take(workshopIndex).LastOrDefault(i =>
            i.OpCode == OpCodes.Ldc_I4_7 && i.Next?.IsStlocFor(menuItemCount) == true)
            ?? throw new InvalidOperationException("Could not find the verified seven-row main-menu item count.");
        var insertionPoint = workshopLoad;

        originalItemCount.OpCode = OpCodes.Ldc_I4_8;

        // DrawMenu owns the menu list, hover state, mouse hit-testing, and controller navigation.
        // Insert directly before Workshop so the original Workshop and Settings rows shift down intact.
        il.InsertBefore(insertionPoint, LoadLocal(il, stringArray));
        il.InsertBefore(insertionPoint, LoadLocal(il, menuIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldstr, "Plugins"));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Stelem_Ref));

        var advanceToNextItem = il.Create(OpCodes.Ldloc, menuIndex);
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldfld, selectedMenu));
        il.InsertBefore(insertionPoint, LoadLocal(il, menuIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Bne_Un, advanceToNextItem));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, openPluginManager));
        il.InsertBefore(insertionPoint, advanceToNextItem);
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Add));
        il.InsertBefore(insertionPoint, StoreLocal(il, menuIndex));
    }

    private static void PatchIngamePluginSettings(
        ModuleDefinition module,
        TypeDefinition ingameOptionsType,
        MethodReference openIngamePluginSettings,
        MethodReference drawIngamePluginSettings)
    {
        var draw = ingameOptionsType.Methods.Single(method => method.Name == "Draw" && method.Parameters.Count == 2);
        var langType = module.Types.First(type => type.FullName == "Terraria.Lang");
        var menuField = langType.Fields.Single(field => field.Name == "menu");
        var il = draw.Body.GetILProcessor();

        // This exact Lang.menu[118] load is Terraria 1.4.5.6's Close Menu row.
        // Keeping the native draw helper intact preserves its layout, hover, and input behavior.
        var closeMenuLabel = draw.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldsfld &&
            instruction.Operand == menuField &&
            IsLoadInt32(instruction.Next, 118) &&
            instruction.Next?.Next?.OpCode == OpCodes.Ldelem_Ref &&
            instruction.Next.Next.Next?.Operand is MethodReference reference && reference.Name == "get_Value")
            ?? throw new InvalidOperationException("Could not find Terraria 1.4.5.6's verified Close Menu label.");
        var afterNativeLabel = closeMenuLabel.Next!.Next!.Next!.Next
            ?? throw new InvalidOperationException("Close Menu label has no continuation point.");

        il.InsertBefore(closeMenuLabel, il.Create(OpCodes.Ldstr, "Plugins"));
        il.InsertBefore(closeMenuLabel, il.Create(OpCodes.Br, afterNativeLabel));

        var closeMenuAction = draw.Body.Instructions
            .Skip(draw.Body.Instructions.IndexOf(afterNativeLabel))
            .FirstOrDefault(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference reference &&
                reference.FullName == "System.Void Terraria.IngameOptions::Close()")
            ?? throw new InvalidOperationException("Could not find Terraria 1.4.5.6's verified Close Menu action.");
        closeMenuAction.Operand = openIngamePluginSettings;

        var drawThickCursor = draw.Body.Instructions.LastOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference reference &&
            reference.DeclaringType.FullName == "Terraria.Main" &&
            reference.Name == "DrawThickCursor" &&
            reference.Parameters.Count == 1)
            ?? throw new InvalidOperationException("Could not find final cursor draw in IngameOptions.Draw.");
        var insertionPoint = drawThickCursor.Previous
            ?? throw new InvalidOperationException("Final cursor draw has no safe insertion point.");
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, drawIngamePluginSettings));
    }

    private static bool IsLoadInt32(Instruction instruction, int value)
    {
        if (instruction == null)
            return false;

        return value switch
        {
            -1 => instruction.OpCode == OpCodes.Ldc_I4_M1,
            0 => instruction.OpCode == OpCodes.Ldc_I4_0,
            1 => instruction.OpCode == OpCodes.Ldc_I4_1,
            2 => instruction.OpCode == OpCodes.Ldc_I4_2,
            3 => instruction.OpCode == OpCodes.Ldc_I4_3,
            4 => instruction.OpCode == OpCodes.Ldc_I4_4,
            5 => instruction.OpCode == OpCodes.Ldc_I4_5,
            6 => instruction.OpCode == OpCodes.Ldc_I4_6,
            7 => instruction.OpCode == OpCodes.Ldc_I4_7,
            8 => instruction.OpCode == OpCodes.Ldc_I4_8,
            _ => instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is int constant && constant == value ||
                 instruction.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(instruction.Operand) == value
        };
    }
}
