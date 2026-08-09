using Mono.Cecil;
using Mono.Cecil.Cil;

internal enum ClientPatchStatus
{
    Applied,
    AlreadyApplied,
    UnsupportedTarget,
    AnchorNotFound,
    AmbiguousTarget,
    ValidationFailed,
    Failed
}

internal sealed class ClientPatchResult
{
    internal ClientPatchResult(string patchId, ClientPatchStatus status, string detail)
    {
        PatchId = patchId;
        Status = status;
        Detail = detail;
    }

    internal string PatchId { get; }
    internal ClientPatchStatus Status { get; }
    internal string Detail { get; }
}

internal sealed class ClientPatchDefinition
{
    internal ClientPatchDefinition(string id, Action<ModuleDefinition, string> apply, Func<ModuleDefinition, bool> isPresent, IReadOnlyList<ClientPatchOperation> operations, params string[] dependencies)
    {
        Id = id;
        Apply = apply;
        IsPresent = isPresent;
        Operations = operations;
        Dependencies = dependencies;
    }

    internal string Id { get; }
    internal Action<ModuleDefinition, string> Apply { get; }
    internal Func<ModuleDefinition, bool> IsPresent { get; }
    internal IReadOnlyList<ClientPatchOperation> Operations { get; }
    internal IReadOnlyList<string> Dependencies { get; }
}

/// <summary>
/// One concrete version-locked Terraria method transformation. This is both the human-readable
/// inventory and the data used to verify that the operation injected every required bridge call.
/// </summary>
internal sealed class ClientPatchTarget
{
    internal ClientPatchTarget(
        string id,
        string typeName,
        string memberSignature,
        string anchor,
        string injection,
        params string[] bridgeMethods)
    {
        Id = id;
        TypeName = typeName;
        MemberSignature = memberSignature;
        Anchor = anchor;
        Injection = injection;
        BridgeMethods = bridgeMethods;
        Precondition = "The exact member signature and unique anchor recorded for this target must be present in the clean, hash-verified Terraria 1.4.5.6 executable.";
        Postcondition = bridgeMethods.Length == 0
            ? "The recorded target mutation must survive the Cecil write/reopen validation."
            : "Every listed PluginUiRuntime ABI call must be present exactly once after Cecil write/reopen validation, except all-return capture sites which require one call before every return.";
    }

    internal string Id { get; }
    internal string TypeName { get; }
    internal string MemberSignature { get; }
    internal string Anchor { get; }
    internal string Injection { get; }
    internal string Precondition { get; }
    internal string Postcondition { get; }
    internal IReadOnlyList<string> BridgeMethods { get; }
}

/// <summary>Inspectable target and ABI contract for one independently applied patch set.</summary>
internal sealed class ClientPatchOperation
{
    internal ClientPatchOperation(string id, string targetType, string targetDescription, params string[] bridgeMethods)
    {
        Id = id;
        TargetType = targetType;
        TargetDescription = targetDescription;
        BridgeMethods = bridgeMethods;
        Targets = Array.Empty<ClientPatchTarget>();
    }

    internal ClientPatchOperation(
        string id,
        string targetType,
        string targetDescription,
        IReadOnlyList<ClientPatchTarget> targets,
        params string[] bridgeMethods)
    {
        Id = id;
        TargetType = targetType;
        TargetDescription = targetDescription;
        Targets = targets;
        BridgeMethods = bridgeMethods;
    }

    internal string Id { get; }
    internal string TargetType { get; }
    internal string TargetDescription { get; }
    internal IReadOnlyList<ClientPatchTarget> Targets { get; }
    internal IReadOnlyList<string> BridgeMethods { get; }
}

/// <summary>Ordered, audited transformations for exactly one supported Terraria build.</summary>
internal static class PermanentPatchCatalog
{
    internal const string Identity = "alacrity-terraria-1.4.5.6-r3";

    private static readonly ClientPatchDefinition[] Definitions =
    {
        CreateDefinition(
            "patch.runtime.startup-and-menu",
            PermanentPatchPlan.ApplyPermanentStartupAndMenu,
            "runtime.startup-and-menu",
            "Terraria.Main / Terraria.IngameOptions",
            "Main-menu insertion, in-game settings replacement, and version labels",
            new ClientPatchTarget("menu.version-labels", "Terraria.Main", ".cctor()", "the exact v1.4.5.6 assignments to versionNumber and versionNumber2", "replace string literals with Terraria v1.4.5.6"),
            new ClientPatchTarget("menu.main-entry", "Terraria.Main", "DrawMenu(Microsoft.Xna.Framework.GameTime)", "SocialAPI.Workshop load following the verified seven-row count and locals 27/9/45", "insert a native Plugins row and call", "OpenPluginManager"),
            new ClientPatchTarget("menu.ingame-settings", "Terraria.IngameOptions", "Draw(Terraria.Main, Microsoft.Xna.Framework.Graphics.SpriteBatch)", "Lang.menu[118] Close Menu label/action and final Main.DrawThickCursor call", "replace Close Menu action and insert draw callback", "OpenIngamePluginSettings", "DrawIngamePluginSettings"),
            new ClientPatchTarget("menu.version-draw", "Terraria.Main", "DrawMenu(Microsoft.Xna.Framework.GameTime)", "Main.DrawVersionNumber(Color, Single) using verified locals 3 and 31", "insert after version draw", "DrawAlacrityVersion")),
        CreateDefinition(
            "patch.runtime.input-and-keybinds",
            PermanentPatchPlan.ApplyPermanentInputAndKeybinds,
            "runtime.input-and-keybinds",
            "Terraria.Main / Terraria.GameInput.PlayerInput / Terraria.GameContent.UI.States.UIManageControls",
            "Post-input keybind dispatch and controls-menu integration",
            new ClientPatchTarget("input.post-input", "Terraria.Main", "DoUpdate_HandleInput()", "final return after Terraria updates input state", "insert keybind update and vanilla-input guard", "UpdatePluginKeybinds", "HandleInput"),
            new ClientPatchTarget("input.key-state-shape", "Terraria.GameInput.PlayerInput", "UpdateInput()", "SettingsForUI.UpdateCounters() call", "insert before native state reset/copy", "EnsurePluginKeybindStateShape"),
            new ClientPatchTarget("input.controls-menu", "Terraria.GameContent.UI.States.UIManageControls", "OnInitialize()", "final return", "insert before return", "AppendPluginKeybindControls")),
        CreateDefinition(
            "patch.runtime.rendering-and-combat",
            PermanentPatchPlan.ApplyPermanentRenderingAndCombat,
            "runtime.rendering-and-combat",
            "Terraria.Main / Terraria.Player",
            "HUD notification, world-overlay, and melee collision capture hooks",
            new ClientPatchTarget("render.notifications", "Terraria.Main", "DrawInterface_33_MouseText()", "method entry and static Main.spriteBatch field", "insert before first instruction", "DrawNotifications"),
            new ClientPatchTarget("render.world-overlays", "Terraria.Main", "DrawInterface_1_1_DrawEmoteBubblesInWorld()", "EmoteBubble.DrawAll(SpriteBatch) continuation", "insert after native emote bubble draw", "DrawHitboxes"),
            new ClientPatchTarget("combat.melee-capture", "Terraria.Player", "ItemCheck_GetMeleeHitbox(Item, Rectangle, Boolean&, Rectangle&)", "every return in the verified four-parameter method", "insert before return and retarget branch/EH references", "CaptureSwingHitbox")),
        CreateDefinition(
            "patch.runtime.visual-effects",
            PermanentPatchPlan.ApplyPermanentVisualEffects,
            "runtime.visual-effects",
            "Terraria.Main / Terraria.Dust / Terraria.Gore",
            "Dust and gore simulation, creation, and draw policy gates",
            new ClientPatchTarget("effects.dust-draw", "Terraria.Main", "DrawDust()", "method entry and verified dust loop local", "entry gate and per-instance branch", "ShouldRunDustSystem", "ShouldDrawDustInstance"),
            new ClientPatchTarget("effects.dust-create", "Terraria.Dust", "NewDust(..., Int32 type, ...)", "method entry with type at parameter index 3", "return vanilla failure sentinel when denied", "ShouldCreateDust"),
            new ClientPatchTarget("effects.dust-update", "Terraria.Dust", "UpdateDust()", "method entry and active-field branch using the verified Dust loop local", "entry gate and per-instance loop branch", "ShouldRunDustSystem", "ShouldUpdateDustInstance"),
            new ClientPatchTarget("effects.gore-draw", "Terraria.Main", "DrawGore()", "method entry", "return gate", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-draw-behind", "Terraria.Main", "DrawGoreBehind()", "method entry", "return gate", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-draw-back", "Terraria.Main", "DrawBackGore()", "method entry", "return gate", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-create", "Terraria.Gore", "NewGore(...)", "method entry", "return sentinel", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-update", "Terraria.Gore", "Update()", "method entry", "return gate", "ShouldRunGoreSystem")),
        CreateDefinition(
            "patch.runtime.chat-input-and-commands",
            PermanentPatchPlan.ApplyPermanentChatInputAndCommands,
            "runtime.chat-input-and-commands",
            "Terraria.Main / Terraria.Program",
            "Chat editing, command consumption, startup, and input formatting",
            new ClientPatchTarget("chat.input-edit", "Terraria.Main", "GetInputText(String, Boolean)", "method entry guarded by Main.drawingPlayerChat", "early return through generic chat editor", "IsBetterChatActive", "ProcessPlayerChatInput"),
            new ClientPatchTarget("chat.command-dispatch", "Terraria.Main", "DoUpdate_HandleChat()", "Main.chatText non-empty comparison and native close-chat path", "consume handled command before network send", "TryHandlePluginChatCommand"),
            new ClientPatchTarget("chat.bootstrap", "Terraria.Program", "LaunchGame(String[], Boolean)", "method entry", "insert before first instruction", "BootstrapPluginRuntime"),
            new ClientPatchTarget("chat.input-format", "Terraria.Main", "DrawPlayerChat()", "verified chatText capture into string local 2 and cursor literal/append region", "format input and remove vanilla cursor append", "FormatPlayerChatText")),
        CreateDefinition(
            "patch.runtime.chat-display-and-interaction",
            PermanentPatchPlan.ApplyPermanentChatDisplayAndInteraction,
            "runtime.chat-display-and-interaction",
            "Terraria.UI.Chat.TextSnippet / Terraria.UI.Chat.ChatManager / Terraria.Chat.ChatHelper / Terraria.Main",
            "Chat decoration, display visibility, hover, click, color, and copy context",
            new ClientPatchTarget("chat.snippet-color", "Terraria.UI.Chat.TextSnippet", "GetVisibleColor()", "complete method body", "replace body", "GetChatSnippetVisibleColor"),
            new ClientPatchTarget("chat.snippet-hover", "Terraria.UI.Chat.TextSnippet", "OnHover()", "complete method body", "replace body", "HandleChatSnippetHover"),
            new ClientPatchTarget("chat.snippet-click", "Terraria.UI.Chat.TextSnippet", "OnClick()", "complete method body", "replace body", "HandleChatSnippetClick"),
            new ClientPatchTarget("chat.snippet-copy", "Terraria.UI.Chat.TextSnippet", "CopyMorph(String)", "final return", "insert copy-context callback before return", "CopyChatSnippetContext"),
            new ClientPatchTarget("chat.parse-decoration", "Terraria.UI.Chat.ChatManager", "ParseMessage(String, Color)", "final return", "decorate returned snippet list before return", "DecorateChatMessage"),
            new ClientPatchTarget("chat.network-visibility", "Terraria.Chat.ChatHelper", "DisplayMessage(NetworkText, Color, Byte)", "method entry", "return gate using argument 2", "ShouldDisplayNetworkChatMessage"),
            new ClientPatchTarget("chat.local-visibility-text", "Terraria.Main", "NewText(String, Byte, Byte, Byte)", "method entry", "return gate", "ShouldDisplayLocalChatMessage"),
            new ClientPatchTarget("chat.local-visibility-multiline", "Terraria.Main", "NewTextMultiline(String, Boolean, Color, Int32)", "method entry", "return gate", "ShouldDisplayLocalChatMessage"))
    };

    private static ClientPatchDefinition CreateDefinition(string patchId, Action<ModuleDefinition, string> apply, string operationId, string targetType, string targetDescription, params ClientPatchTarget[] targets)
    {
        var bridgeMethods = GetBridgeMethods(targets);
        var operation = new ClientPatchOperation(operationId, targetType, targetDescription, targets, bridgeMethods);
        return new ClientPatchDefinition(patchId, apply, module => HasDefinitionPostconditions(module, targets), new[] { operation });
    }

    private static string[] GetBridgeMethods(IReadOnlyList<ClientPatchTarget> targets)
    {
        var bridgeMethods = new List<string>();
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var target = targets[targetIndex];
            for (var methodIndex = 0; methodIndex < target.BridgeMethods.Count; methodIndex++)
            {
                var bridgeMethod = target.BridgeMethods[methodIndex];
                if (!bridgeMethods.Contains(bridgeMethod, StringComparer.Ordinal))
                {
                    bridgeMethods.Add(bridgeMethod);
                }
            }
        }

        return bridgeMethods.ToArray();
    }

    internal static IReadOnlyList<ClientPatchDefinition> GetDefinitions() => Definitions;

    internal static List<ClientPatchResult> ApplyAll(ModuleDefinition module, string cleanSourcePath)
    {
        return ApplyDefinitions(module, cleanSourcePath, Definitions);
    }

    internal static List<ClientPatchResult> ApplyDefinitions(ModuleDefinition module, string cleanSourcePath, IReadOnlyList<ClientPatchDefinition> definitions)
    {
        ValidateDefinitions(definitions);
        var results = new List<ClientPatchResult>(definitions.Count);
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            if (definition.IsPresent(module))
            {
                throw new ClientBuildException("Patch " + definition.Id + " is already present. Client generation requires an unmodified supported Terraria.exe source.");
            }

            try
            {
                ValidateOperationPreconditions(module, definition);
                definition.Apply(module, cleanSourcePath);
            }
            catch (ClientBuildException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ClientBuildException("Patch " + definition.Id + " failed: " + exception.Message);
            }

            if (!definition.IsPresent(module))
            {
                throw new ClientBuildException("Patch " + definition.Id + " completed without producing its verified runtime bridge call.");
            }

            for (var operationIndex = 0; operationIndex < definition.Operations.Count; operationIndex++)
            {
                var operation = definition.Operations[operationIndex];
                ValidateOperationPostcondition(module, operation);
                results.Add(new ClientPatchResult(operation.Id, ClientPatchStatus.Applied, operation.TargetType + ": " + operation.TargetDescription));
            }
        }

        return results;
    }

    internal static void ValidateCatalog()
    {
        ValidateDefinitions(Definitions);
        for (var definitionIndex = 0; definitionIndex < Definitions.Length; definitionIndex++)
        {
            var operations = Definitions[definitionIndex].Operations;
            for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                if (operations[operationIndex].Targets.Count == 0)
                {
                    throw new ClientBuildException("Permanent patch catalog operation " + operations[operationIndex].Id + " has no detailed target inventory.");
                }
            }
        }
    }

    internal static void ValidateDefinitions(IReadOnlyList<ClientPatchDefinition> definitions)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < definitions.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(definitions[index].Id) || !ids.Add(definitions[index].Id))
            {
                throw new ClientBuildException("Permanent patch catalog contains a missing or duplicate patch ID.");
            }

            indexes.Add(definitions[index].Id, index);

            for (var operationIndex = 0; operationIndex < definitions[index].Operations.Count; operationIndex++)
            {
                var operation = definitions[index].Operations[operationIndex];
                if (string.IsNullOrWhiteSpace(operation.Id) || !ids.Add(operation.Id) || operation.BridgeMethods.Count == 0)
                {
                    throw new ClientBuildException("Permanent patch catalog contains a missing or duplicate operation ID or an operation with no bridge ABI postcondition.");
                }

                var targetIds = new HashSet<string>(StringComparer.Ordinal);
                for (var targetIndex = 0; targetIndex < operation.Targets.Count; targetIndex++)
                {
                    var target = operation.Targets[targetIndex];
                    if (string.IsNullOrWhiteSpace(target.Id) ||
                        !targetIds.Add(target.Id) ||
                        string.IsNullOrWhiteSpace(target.TypeName) ||
                        string.IsNullOrWhiteSpace(target.MemberSignature) ||
                        string.IsNullOrWhiteSpace(target.Anchor) ||
                        string.IsNullOrWhiteSpace(target.Injection) ||
                        string.IsNullOrWhiteSpace(target.Precondition) ||
                        string.IsNullOrWhiteSpace(target.Postcondition))
                    {
                        throw new ClientBuildException("Permanent patch catalog contains an incomplete or duplicate detailed target for operation " + operation.Id + ".");
                    }
                }
            }
        }

        for (var index = 0; index < definitions.Count; index++)
        {
            var dependencies = definitions[index].Dependencies;
            for (var dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++)
            {
                var dependency = dependencies[dependencyIndex];
                if (!indexes.TryGetValue(dependency, out var dependencyPosition))
                {
                    throw new ClientBuildException("Patch " + definitions[index].Id + " depends on missing patch " + dependency + ".");
                }
                if (dependencyPosition >= index)
                {
                    throw new ClientBuildException("Patch " + definitions[index].Id + " has a cyclic or non-deterministic dependency on " + dependency + ". Dependencies must appear earlier in the explicit catalog.");
                }
            }
        }
    }

    internal static bool HasRuntimeBridgeCall(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            if (HasRuntimeBridgeCall(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBridgeMethodCalls(ModuleDefinition module, IReadOnlyList<string> methodNames)
    {
        for (var index = 0; index < methodNames.Count; index++)
        {
            if (!HasBridgeMethodCall(module, methodNames[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDefinitionPostconditions(ModuleDefinition module, IReadOnlyList<ClientPatchTarget> targets)
    {
        try
        {
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                ClientPatchTarget target = targets[targetIndex];
                TypeDefinition type = CecilPatchPrimitives.RequireType(module, target.TypeName);
                IReadOnlyList<MethodDefinition> methods = ResolveTargetMethods(type, target);
                for (int bridgeIndex = 0; bridgeIndex < target.BridgeMethods.Count; bridgeIndex++)
                {
                    ValidateTargetBridgePostcondition(target, methods, target.BridgeMethods[bridgeIndex]);
                }
            }

            return true;
        }
        catch (ClientBuildException)
        {
            return false;
        }
    }

    private static void ValidateOperationPostcondition(ModuleDefinition module, ClientPatchOperation operation)
    {
        for (int targetIndex = 0; targetIndex < operation.Targets.Count; targetIndex++)
        {
            ClientPatchTarget target = operation.Targets[targetIndex];
            if (target.BridgeMethods.Count == 0)
            {
                continue;
            }

            TypeDefinition type = CecilPatchPrimitives.RequireType(module, target.TypeName);
            IReadOnlyList<MethodDefinition> targetMethods = ResolveTargetMethods(type, target);
            for (int bridgeIndex = 0; bridgeIndex < target.BridgeMethods.Count; bridgeIndex++)
            {
                string bridgeMethod = target.BridgeMethods[bridgeIndex];
                ValidateTargetBridgePostcondition(target, targetMethods, bridgeMethod);
            }
        }
    }

    private static void ValidateOperationPreconditions(ModuleDefinition module, ClientPatchDefinition definition)
    {
        for (int operationIndex = 0; operationIndex < definition.Operations.Count; operationIndex++)
        {
            ClientPatchOperation operation = definition.Operations[operationIndex];
            for (int targetIndex = 0; targetIndex < operation.Targets.Count; targetIndex++)
            {
                ClientPatchTarget target = operation.Targets[targetIndex];
                TypeDefinition type = CecilPatchPrimitives.RequireType(module, target.TypeName);
                _ = ResolveTargetMethods(type, target);
            }
        }
    }

    private static IReadOnlyList<MethodDefinition> ResolveTargetMethods(TypeDefinition type, ClientPatchTarget target)
    {
        var methods = new List<MethodDefinition>();
        string[] alternatives = target.MemberSignature.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (alternatives.Length != 1)
        {
            throw new ClientBuildException("Patch target " + target.Id + " must name one exact method. Split grouped target signatures into independently verified patch sites.");
        }
        for (int index = 0; index < alternatives.Length; index++)
        {
            string candidate = alternatives[index].Trim();
            int argumentStart = candidate.IndexOf('(');
            int argumentEnd = candidate.LastIndexOf(')');
            if (argumentStart <= 0 || argumentEnd != candidate.Length - 1)
            {
                throw new ClientBuildException("Patch target " + target.Id + " has an invalid member signature: " + target.MemberSignature + ".");
            }

            string name = candidate.Substring(0, argumentStart).Trim();
            if (name.Length == 0)
            {
                throw new ClientBuildException("Patch target " + target.Id + " has an invalid member signature: " + target.MemberSignature + ".");
            }

            string parameters = candidate.Substring(argumentStart + 1, argumentEnd - argumentStart - 1).Trim();
            bool usesEllipsis = parameters.IndexOf("...", StringComparison.Ordinal) >= 0;
            string[] parameterTypes = usesEllipsis || parameters.Length == 0
                ? Array.Empty<string>()
                : parameters.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            MethodDefinition? match = null;
            for (int methodIndex = 0; methodIndex < type.Methods.Count; methodIndex++)
            {
                MethodDefinition method = type.Methods[methodIndex];
                if (!method.HasBody || !string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!usesEllipsis && !MatchesTargetParameters(method, parameterTypes))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new ClientBuildException("Patch target " + target.Id + " resolves ambiguously to multiple methods in " + type.FullName + ".");
                }

                match = method;
            }

            if (match == null)
            {
                throw new ClientBuildException("Patch target " + target.Id + " could not resolve " + type.FullName + "::" + candidate + ".");
            }

            methods.Add(match);
        }

        if (methods.Count == 0)
        {
            throw new ClientBuildException("Patch target " + target.Id + " does not identify a target member.");
        }

        return methods;
    }

    private static bool MatchesTargetParameters(MethodDefinition method, IReadOnlyList<string> expectedTypes)
    {
        if (method.Parameters.Count != expectedTypes.Count)
        {
            return false;
        }

        for (int parameterIndex = 0; parameterIndex < expectedTypes.Count; parameterIndex++)
        {
            string expected = expectedTypes[parameterIndex].Trim();
            string actual = method.Parameters[parameterIndex].ParameterType.FullName;
            if (!string.Equals(actual, expected, StringComparison.Ordinal) &&
                !actual.EndsWith("." + expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasBridgeMethodCall(IReadOnlyList<MethodDefinition> targetMethods, string bridgeMethod)
    {
        for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
        {
            MethodDefinition method = targetMethods[methodIndex];
            for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].Operand is MethodReference reference &&
                    string.Equals(reference.DeclaringType.FullName, BridgeAbiContractCatalog.FacadeTypeName, StringComparison.Ordinal) &&
                    string.Equals(reference.Name, bridgeMethod, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A successful Cecil write is not enough: each target has a deliberately small, exact ABI
    /// footprint. Counting calls detects duplicate application, and the melee hook additionally
    /// proves that every return remains covered after branch/EH retargeting.
    /// </summary>
    private static void ValidateTargetBridgePostcondition(ClientPatchTarget target, IReadOnlyList<MethodDefinition> targetMethods, string bridgeMethod)
    {
        bool allReturns = target.Anchor.IndexOf("every return", StringComparison.OrdinalIgnoreCase) >= 0 ||
            target.Injection.IndexOf("every return", StringComparison.OrdinalIgnoreCase) >= 0;
        int expected = allReturns ? CountReturns(targetMethods) : 1;
        int actual = CountBridgeMethodCalls(targetMethods, bridgeMethod);
        if (actual != expected)
        {
            throw new ClientBuildException(
                "Patch target " + target.Id + " expected " + expected + " call(s) to " + bridgeMethod +
                " but found " + actual + " in " + target.TypeName + "::" + target.MemberSignature + ".");
        }

        if (allReturns)
        {
            for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
            {
                MethodDefinition method = targetMethods[methodIndex];
                for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
                {
                    Instruction instruction = method.Body.Instructions[instructionIndex];
                    if (instruction.OpCode != OpCodes.Ret)
                    {
                        continue;
                    }

                    Instruction? previous = instruction.Previous;
                    if (!IsBridgeMethodCall(previous, bridgeMethod))
                    {
                        throw new ClientBuildException(
                            "Patch target " + target.Id + " does not invoke " + bridgeMethod +
                            " immediately before every return in " + method.FullName + ".");
                    }
                }
            }
        }
    }

    private static int CountReturns(IReadOnlyList<MethodDefinition> targetMethods)
    {
        int count = 0;
        for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
        {
            MethodDefinition method = targetMethods[methodIndex];
            for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].OpCode == OpCodes.Ret)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountBridgeMethodCalls(IReadOnlyList<MethodDefinition> targetMethods, string bridgeMethod)
    {
        int count = 0;
        for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
        {
            MethodDefinition method = targetMethods[methodIndex];
            for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (IsBridgeMethodCall(method.Body.Instructions[instructionIndex], bridgeMethod))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsBridgeMethodCall(Instruction? instruction, string bridgeMethod)
    {
        return instruction != null &&
            instruction.Operand is MethodReference reference &&
            string.Equals(reference.DeclaringType.FullName, BridgeAbiContractCatalog.FacadeTypeName, StringComparison.Ordinal) &&
            string.Equals(reference.Name, bridgeMethod, StringComparison.Ordinal);
    }

    private static bool HasBridgeMethodCall(ModuleDefinition module, string methodName)
    {
        foreach (var type in module.Types)
        {
            if (HasBridgeMethodCall(type, methodName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRuntimeBridgeCall(TypeDefinition type)
    {
        for (var methodIndex = 0; methodIndex < type.Methods.Count; methodIndex++)
        {
            var method = type.Methods[methodIndex];
            if (!method.HasBody)
            {
                continue;
            }

            for (var instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                var instruction = method.Body.Instructions[instructionIndex];
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference reference &&
                    string.Equals(reference.DeclaringType.FullName, "AlacrityTerraria.PluginUiRuntime", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        for (var nestedIndex = 0; nestedIndex < type.NestedTypes.Count; nestedIndex++)
        {
            if (HasRuntimeBridgeCall(type.NestedTypes[nestedIndex]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBridgeMethodCall(TypeDefinition type, string methodName)
    {
        for (var methodIndex = 0; methodIndex < type.Methods.Count; methodIndex++)
        {
            var method = type.Methods[methodIndex];
            if (!method.HasBody)
            {
                continue;
            }

            for (var instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].Operand is MethodReference reference &&
                    string.Equals(reference.DeclaringType.FullName, "AlacrityTerraria.PluginUiRuntime", StringComparison.Ordinal) &&
                    string.Equals(reference.Name, methodName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        for (var nestedIndex = 0; nestedIndex < type.NestedTypes.Count; nestedIndex++)
        {
            if (HasBridgeMethodCall(type.NestedTypes[nestedIndex], methodName))
            {
                return true;
            }
        }

        return false;
    }
}
