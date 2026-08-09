using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch-domain implementation is separate from the command-line entry point.
internal static partial class PermanentPatchPlan
{
    internal static void ApplyPermanentAlacrityPatches(ModuleDefinition module, string sourceExecutablePath)
    {
        ApplyPermanentStartupAndMenu(module, sourceExecutablePath);
        ApplyPermanentInputAndKeybinds(module, sourceExecutablePath);
        ApplyPermanentRenderingAndCombat(module, sourceExecutablePath);
        ApplyPermanentRenderCulling(module, sourceExecutablePath);
        ApplyPermanentVisualEffects(module, sourceExecutablePath);
        ApplyPermanentChatInputAndCommands(module, sourceExecutablePath);
        ApplyPermanentChatDisplayAndInteraction(module, sourceExecutablePath);
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

    internal static void ApplyPermanentChatInputAndCommands(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        var programType = CecilPatchPrimitives.RequireType(module, "Terraria.Program");
        PatchBetterChatInput(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsBetterChatActive", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ProcessPlayerChatInput", "System.String", "System.String", "System.Boolean"));
        PatchPluginChatCommands(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "TryHandlePluginChatCommand", "System.Boolean", "System.String"));
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
