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
        "ShouldDrawPaladinShieldIcon",
        "TryDrawLaserRulerPresentation",
        "TryBeginRainPresentation",
        "TryQueueRainPresentation",
        "EndRainPresentation",
        "TryRunLightingParallel",
        "TryDrawStaticTileChunk",
        "InvalidateStaticTileChunks",
        "ShouldDrawWorldPlayer",
        "ShouldDrawWorldItem",
        "ShouldDrawWorldParticle",
        "TryProcessNativeTextInput",
        "FormatNativeTextInputDisplay",
        "GetNativeTextInputCaret",
        "DrawNativePlayerChatSelection",
        "DrawNativeTextBoxSelection",
        "ResetNativeTextInput",
        "ShouldHandleChatInputAction",
        "TryHandlePluginChatCommand",
        "RecordSubmittedChatInput",
        "TryDeferOutgoingChatMessage",
        "HasReadyOutgoingChatMessage",
        "DrawChatActionStrip",
        "BootstrapPluginRuntime",
        "FormatPlayerChatText",
        "HandleChatSnippetHover",
        "HandleChatSnippetClick",
        "GetChatSnippetVisibleColor",
        "CopyChatSnippetContext",
        "DecorateStoredChatMessage",
        "PrepareStoredChatMessageText",
        "BeginStoredChatMessageDecorationForContainer",
        "EndStoredChatMessageDecoration",
        "RefreshStoredChatMessagePresentations",
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
        var playerType = CecilPatchPrimitives.RequireType(module, "Terraria.Player");
        PatchPluginInput(
            mainType,
            playerType,
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

    internal static void ApplyPermanentPresentationSuppression(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchPaladinShieldIcon(
            CecilPatchPrimitives.RequireType(module, "Terraria.Main"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDrawPaladinShieldIcon", "System.Boolean"));
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

    internal static void ApplyPermanentRainPresentation(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchRainPresentation(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "TryBeginRainPresentation", "System.Boolean", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "TryQueueRainPresentation", "System.Boolean", "Microsoft.Xna.Framework.Graphics.Texture2D", "Microsoft.Xna.Framework.Vector2", "System.Nullable`1<Microsoft.Xna.Framework.Rectangle>", "Microsoft.Xna.Framework.Color", "System.Single", "Microsoft.Xna.Framework.Vector2", "System.Single", "Microsoft.Xna.Framework.Graphics.SpriteEffects", "System.Single"),
            ImportRuntimeMethod(module, sourceExecutablePath, "EndRainPresentation", "System.Void"));
    }

    internal static void ApplyPermanentLightingParallelism(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchLightingParallelism(
            module,
            ImportRuntimeMethod(
                module,
                sourceExecutablePath,
                "TryRunLightingParallel",
                "System.Boolean",
                "System.Int32",
                "System.Int32",
                "System.Delegate",
                "System.Object"));
    }

    internal static void ApplyPermanentStaticTileChunkPresentation(ModuleDefinition module, string sourceExecutablePath)
    {
        PatchStaticTileChunkPresentation(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "TryDrawStaticTileChunk", "System.Boolean", "Terraria.GameContent.Drawing.TileDrawing", "System.Boolean", "Microsoft.Xna.Framework.Vector2", "Microsoft.Xna.Framework.Vector2", "System.Int32", "System.Int32"),
            ImportRuntimeMethod(module, sourceExecutablePath, "InvalidateStaticTileChunks", "System.Void", "System.Int32", "System.Int32"));
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
        PatchNativeTextInput(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "TryProcessNativeTextInput", "System.Boolean", "System.String", "System.Boolean", "System.String&"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ResetNativeTextInput", "System.Void"));
        PatchNativeMenuTextPresentation(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "FormatNativeTextInputDisplay", "System.String", "System.String"));
        PatchNativeTextInputCaret(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "GetNativeTextInputCaret", "System.Int32", "System.String"),
            ImportRuntimeMethod(module, sourceExecutablePath, "DrawNativeTextBoxSelection", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch", "System.String", "Microsoft.Xna.Framework.Vector2", "System.Object", "System.Single"));
        PatchChatInputActionOwnership(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldHandleChatInputAction", "System.Boolean", "System.String"));
        PatchPluginChatCommands(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "TryHandlePluginChatCommand", "System.Boolean", "System.String"),
            ImportRuntimeMethod(module, sourceExecutablePath, "RecordSubmittedChatInput", "System.Void", "System.String"),
            ImportRuntimeMethod(module, sourceExecutablePath, "TryDeferOutgoingChatMessage", "System.Boolean", "System.String"),
            ImportRuntimeMethod(module, sourceExecutablePath, "HasReadyOutgoingChatMessage", "System.Boolean"));
        PatchBetterChatStartup(programType, ImportRuntimeMethod(module, sourceExecutablePath, "BootstrapPluginRuntime", "System.Void"));
        PatchBetterChatDraw(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "FormatPlayerChatText", "System.String", "System.String"),
            ImportRuntimeMethod(module, sourceExecutablePath, "DrawNativePlayerChatSelection", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch", "System.String"),
            ImportRuntimeMethod(module, sourceExecutablePath, "DrawChatActionStrip", "System.Void"));
    }

    internal static void ApplyPermanentChatDisplayAndInteraction(ModuleDefinition module, string sourceExecutablePath)
    {
        var snippets = CecilPatchPrimitives.RequireType(module, "Terraria.UI.Chat.TextSnippet");
        var chatManager = CecilPatchPrimitives.RequireType(module, "Terraria.UI.Chat.ChatManager");
        var chatContainer = CecilPatchPrimitives.RequireType(module, "Terraria.UI.Chat.ChatMessageContainer");
        PatchBetterChatSnippet(
            snippets,
            chatManager,
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleChatSnippetHover", "System.Void", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleChatSnippetClick", "System.Boolean", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "GetChatSnippetVisibleColor", "Microsoft.Xna.Framework.Color", "System.Object", "Microsoft.Xna.Framework.Color"),
            ImportRuntimeMethod(module, sourceExecutablePath, "CopyChatSnippetContext", "System.Void", "System.Object", "System.Object"));
        PatchStoredChatMessageDecoration(
            chatContainer,
            ImportRuntimeMethod(module, sourceExecutablePath, "BeginStoredChatMessageDecorationForContainer", "System.Void", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "PrepareStoredChatMessageText", "System.String", "System.String", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "EndStoredChatMessageDecoration", "System.Void"));
        PatchStoredChatMessagePresentationRefresh(
            CecilPatchPrimitives.RequireType(module, "Terraria.GameContent.UI.Chat.RemadeChatMonitor"),
            ImportRuntimeMethod(module, sourceExecutablePath, "RefreshStoredChatMessagePresentations", "System.Void"));
        PatchBetterChatParse(chatManager, ImportRuntimeMethod(module, sourceExecutablePath, "DecorateStoredChatMessage", "System.Object", "System.Object", "Microsoft.Xna.Framework.Color", "System.String"));
        PatchBetterChatVisibility(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDisplayNetworkChatMessage", "System.Boolean", "System.Byte"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDisplayLocalChatMessage", "System.Boolean"));
    }
}
