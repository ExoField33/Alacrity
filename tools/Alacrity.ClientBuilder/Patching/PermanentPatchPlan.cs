using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    // This is the authoritative set of facade members imported by the concrete patch plan.
    // PermanentPatchCatalog validates it bidirectionally against each patch target inventory.
    private static readonly string[] ImportedBridgeMethods =
    {
        "OpenPluginManager",
        "OpenIngamePluginSettings",
        "DrawIngamePluginSettings",
        "DrawAlacrityVersion",
        "HandleInput",
        "UpdatePluginKeybinds",
        "EnsurePluginKeybindStateShape",
        "AppendPluginKeybindControls",
        "DrawNotifications",
        "DrawHitboxes",
        "CaptureSwingHitbox",
        "ShouldRunDustSystem",
        "ShouldCreateDust",
        "ShouldUpdateDustInstance",
        "ShouldDrawDustInstance",
        "ShouldRunGoreSystem",
        "IsPaintPreparationOptimizationEnabled",
        "IsPaintExtraPreparationRelevant",
        "IsClothingEntityPresentationOptimizationEnabled",
        "IsWaterfallPresentationOptimizationEnabled",
        "IsTileDrawingPresentationOptimizationEnabled",
        "IsDrawOrchestrationOptimizationEnabled",
        "TryDrawLaserRulerPresentation",
        "ShouldDrawWorldPlayer",
        "ShouldDrawWorldItem",
        "ShouldDrawWorldParticle",
        "IsBetterChatActive",
        "ProcessPlayerChatInput",
        "ShouldHandleChatInputAction",
        "TryHandlePluginChatCommand",
        "RecordSubmittedChatInput",
        "BootstrapPluginRuntime",
        "FormatPlayerChatText",
        "HandleChatSnippetHover",
        "HandleChatSnippetClick",
        "GetChatSnippetVisibleColor",
        "CopyChatSnippetContext",
        "DecorateChatMessage",
        "ShouldDisplayNetworkChatMessage",
        "ShouldDisplayLocalChatMessage"
    };

    internal static IReadOnlyList<string> GetImportedBridgeMethods()
    {
        return ImportedBridgeMethods;
    }

    internal static void ApplyPermanentStartupAndMenu(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        var ingameOptionsType = CecilPatchPrimitives.RequireType(module, "Terraria.IngameOptions");
        PatchTerrariaVersionLabels(mainType);
        PatchPluginMenuEntry(module, mainType, ImportRuntimeMethod(module, sourceExecutablePath, "OpenPluginManager", "System.Void"));
        PatchIngamePluginSettings(
            module,
            ingameOptionsType,
            ImportRuntimeMethod(module, sourceExecutablePath, "OpenIngamePluginSettings", "System.Void"),
            ImportRuntimeMethod(module, sourceExecutablePath, "DrawIngamePluginSettings", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        PatchAlacrityVersionDraw(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "DrawAlacrityVersion", "System.Void", "Microsoft.Xna.Framework.Color", "System.Single", "System.String"),
            ReadAlacrityVersion(sourceExecutablePath));
    }

    internal static void ApplyPermanentInputAndKeybinds(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        PatchPluginInput(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleInput", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "UpdatePluginKeybinds", "System.Void"));
        PatchPluginKeybindStateShape(module, ImportRuntimeMethod(module, sourceExecutablePath, "EnsurePluginKeybindStateShape", "System.Void"));
        PatchPluginKeybindControls(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "AppendPluginKeybindControls", "System.Void", "Terraria.GameContent.UI.States.UIManageControls"));
    }

    internal static void ApplyPermanentRenderingAndCombat(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        PatchPluginRuntimeDraw(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "DrawNotifications", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        PatchHitboxWorldOverlay(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "DrawHitboxes", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        PatchSwingHitboxCapture(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "CaptureSwingHitbox", "System.Void", "Terraria.Player", "System.Boolean", "Microsoft.Xna.Framework.Rectangle"));
    }

    internal static void ApplyPermanentVisualEffects(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        PatchVisualEffects(
            module,
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldRunDustSystem", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldCreateDust", "System.Boolean", "System.Int32"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldUpdateDustInstance", "System.Boolean", "Terraria.Dust"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDrawDustInstance", "System.Boolean", "Terraria.Dust"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldRunGoreSystem", "System.Boolean"));
    }

    internal static void ApplyPermanentRenderCulling(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        var particleRendererType = CecilPatchPrimitives.RequireType(module, "Terraria.Graphics.Renderers.ParticleRenderer");
        PatchRenderCulling(
            module,
            mainType,
            particleRendererType,
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDrawWorldPlayer", "System.Boolean", "Terraria.Player"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDrawWorldItem", "System.Boolean", "System.Int32"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDrawWorldParticle", "System.Boolean", "Terraria.Graphics.Renderers.ParticleRenderer", "Terraria.Graphics.Renderers.IParticle"));
    }

    internal static void ApplyPermanentPaintedTilePreparation(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchPaintedTilePreparation(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsPaintPreparationOptimizationEnabled", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "IsPaintExtraPreparationRelevant", "System.Boolean", "System.Int32"));
    }

    internal static void ApplyPermanentClothingEntityPresentation(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchClothingEntityPresentation(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsClothingEntityPresentationOptimizationEnabled", "System.Boolean"));
    }

    internal static void ApplyPermanentWaterfallPresentation(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchWaterfallPresentation(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsWaterfallPresentationOptimizationEnabled", "System.Boolean"));
    }

    internal static void ApplyPermanentTileDrawingPresentation(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchTileDrawingPresentation(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsTileDrawingPresentationOptimizationEnabled", "System.Boolean"));
    }

    internal static void ApplyPermanentLaserRulerPresentation(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchLaserRulerPresentation(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "TryDrawLaserRulerPresentation", "System.Boolean"));
    }

    internal static void ApplyPermanentDrawOrchestration(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchDrawOrchestration(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsDrawOrchestrationOptimizationEnabled", "System.Boolean"));
    }

    internal static void ApplyPermanentChatInputAndCommands(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        var programType = CecilPatchPrimitives.RequireType(module, "Terraria.Program");
        PatchBetterChatInput(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsBetterChatActive", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ProcessPlayerChatInput", "System.String", "System.String", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldHandleChatInputAction", "System.Boolean", "System.String"));
        PatchPluginChatCommands(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "TryHandlePluginChatCommand", "System.Boolean", "System.String"),
            ImportRuntimeMethod(module, sourceExecutablePath, "RecordSubmittedChatInput", "System.Void", "System.String"));
        PatchBetterChatStartup(programType, ImportRuntimeMethod(module, sourceExecutablePath, "BootstrapPluginRuntime", "System.Void"));
        PatchBetterChatDraw(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "FormatPlayerChatText", "System.String", "System.String"));
    }

    internal static void ApplyPermanentChatDisplayAndInteraction(ModuleDefinition module, string sourceExecutablePath)
    {
        var snippets = CecilPatchPrimitives.RequireType(module, "Terraria.UI.Chat.TextSnippet");
        var chatManager = CecilPatchPrimitives.RequireType(module, "Terraria.UI.Chat.ChatManager");
        PatchBetterChatSnippet(
            snippets,
            chatManager,
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleChatSnippetHover", "System.Void", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleChatSnippetClick", "System.Boolean", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "GetChatSnippetVisibleColor", "Microsoft.Xna.Framework.Color", "System.Object", "Microsoft.Xna.Framework.Color"),
            ImportRuntimeMethod(module, sourceExecutablePath, "CopyChatSnippetContext", "System.Void", "System.Object", "System.Object"));
        PatchBetterChatParse(chatManager, ImportRuntimeMethod(module, sourceExecutablePath, "DecorateChatMessage", "System.Object", "System.Object", "Microsoft.Xna.Framework.Color", "System.String"));
        PatchBetterChatVisibility(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDisplayNetworkChatMessage", "System.Boolean", "System.Byte"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDisplayLocalChatMessage", "System.Boolean"));
    }
}
