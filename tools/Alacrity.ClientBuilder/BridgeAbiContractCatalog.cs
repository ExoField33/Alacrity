/// <summary>Single authoritative contract list for facade methods referenced by permanent Terraria patches.</summary>
internal sealed class BridgeAbiContract
{
    internal BridgeAbiContract(string name, string returnType, params string[] parameterTypes)
    {
        Name = name;
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
    }

    internal string Name { get; }
    internal string ReturnType { get; }
    internal IReadOnlyList<string> ParameterTypes { get; }
}

internal static class BridgeAbiContractCatalog
{
    internal const string FacadeAssemblyName = "Alacrity.PluginUiRuntime";
    internal const string FacadeTypeName = "AlacrityTerraria.PluginUiRuntime";

    private static readonly BridgeAbiContract[] Contracts =
    {
        new BridgeAbiContract("GetBridgeHandshake", "System.String"),
        new BridgeAbiContract("OpenPluginManager", "System.Void"),
        new BridgeAbiContract("OpenIngamePluginSettings", "System.Void"),
        new BridgeAbiContract("DrawIngamePluginSettings", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"),
        new BridgeAbiContract("DrawAlacrityVersion", "System.Void", "Microsoft.Xna.Framework.Color", "System.Single", "System.String"),
        new BridgeAbiContract("HandleInput", "System.Boolean"),
        new BridgeAbiContract("UpdatePluginKeybinds", "System.Void"),
        new BridgeAbiContract("EnsurePluginKeybindStateShape", "System.Void"),
        new BridgeAbiContract("AppendPluginKeybindControls", "System.Void", "Terraria.GameContent.UI.States.UIManageControls"),
        new BridgeAbiContract("DrawNotifications", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"),
        new BridgeAbiContract("DrawHitboxes", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"),
        new BridgeAbiContract("CaptureSwingHitbox", "System.Void", "Terraria.Player", "System.Boolean", "Microsoft.Xna.Framework.Rectangle"),
        new BridgeAbiContract("ShouldRunDustSystem", "System.Boolean"),
        new BridgeAbiContract("ShouldCreateDust", "System.Boolean", "System.Int32"),
        new BridgeAbiContract("ShouldUpdateDustInstance", "System.Boolean", "Terraria.Dust"),
        new BridgeAbiContract("ShouldDrawDustInstance", "System.Boolean", "Terraria.Dust"),
        new BridgeAbiContract("ShouldDrawWorldPlayer", "System.Boolean", "Terraria.Player"),
        new BridgeAbiContract("ShouldDrawWorldItem", "System.Boolean", "System.Int32"),
        new BridgeAbiContract("ShouldDrawWorldParticle", "System.Boolean", "Terraria.Graphics.Renderers.ParticleRenderer", "Terraria.Graphics.Renderers.IParticle"),
        new BridgeAbiContract("ShouldRunGoreSystem", "System.Boolean"),
        new BridgeAbiContract("IsPaintPreparationOptimizationEnabled", "System.Boolean"),
        new BridgeAbiContract("IsPaintExtraPreparationRelevant", "System.Boolean", "System.Int32"),
        new BridgeAbiContract("IsClothingEntityPresentationOptimizationEnabled", "System.Boolean"),
        new BridgeAbiContract("IsWaterfallPresentationOptimizationEnabled", "System.Boolean"),
        new BridgeAbiContract("IsTileDrawingPresentationOptimizationEnabled", "System.Boolean"),
        new BridgeAbiContract("IsDrawOrchestrationOptimizationEnabled", "System.Boolean"),
        new BridgeAbiContract("ShouldDrawPaladinShieldIcon", "System.Boolean"),
        new BridgeAbiContract("TryDrawLaserRulerPresentation", "System.Boolean"),
        new BridgeAbiContract("TryBeginRainPresentation", "System.Boolean", "System.Boolean"),
        new BridgeAbiContract("TryQueueRainPresentation", "System.Boolean", "Microsoft.Xna.Framework.Graphics.Texture2D", "Microsoft.Xna.Framework.Vector2", "System.Nullable`1<Microsoft.Xna.Framework.Rectangle>", "Microsoft.Xna.Framework.Color", "System.Single", "Microsoft.Xna.Framework.Vector2", "System.Single", "Microsoft.Xna.Framework.Graphics.SpriteEffects", "System.Single"),
        new BridgeAbiContract("EndRainPresentation", "System.Void"),
        new BridgeAbiContract("TryRunLightingParallel", "System.Boolean", "System.Int32", "System.Int32", "System.Delegate", "System.Object"),
        new BridgeAbiContract("TryDrawStaticTileChunk", "System.Boolean", "Terraria.GameContent.Drawing.TileDrawing", "System.Boolean", "Microsoft.Xna.Framework.Vector2", "Microsoft.Xna.Framework.Vector2", "System.Int32", "System.Int32"),
        new BridgeAbiContract("InvalidateStaticTileChunks", "System.Void", "System.Int32", "System.Int32"),
        new BridgeAbiContract("TryProcessNativeTextInput", "System.Boolean", "System.String", "System.Boolean", "System.String&"),
        new BridgeAbiContract("FormatNativeTextInputDisplay", "System.String", "System.String"),
        new BridgeAbiContract("GetNativeTextInputCaret", "System.Int32", "System.String"),
        new BridgeAbiContract("DrawNativePlayerChatSelection", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch", "System.String"),
        new BridgeAbiContract("DrawNativeTextBoxSelection", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch", "System.String", "Microsoft.Xna.Framework.Vector2", "System.Object", "System.Single"),
        new BridgeAbiContract("ResetNativeTextInput", "System.Void"),
        new BridgeAbiContract("IsBetterChatActive", "System.Boolean"),
        new BridgeAbiContract("BootstrapPluginRuntime", "System.Void"),
        new BridgeAbiContract("ShouldHandleChatInputAction", "System.Boolean", "System.String"),
        new BridgeAbiContract("ProcessPlayerChatInput", "System.String", "System.String", "System.Boolean"),
        new BridgeAbiContract("RecordSubmittedChatInput", "System.Void", "System.String"),
        new BridgeAbiContract("TryDeferOutgoingChatMessage", "System.Boolean", "System.String"),
        new BridgeAbiContract("TakeReadyOutgoingChatMessage", "System.String"),
        new BridgeAbiContract("HasReadyOutgoingChatMessage", "System.Boolean"),
        new BridgeAbiContract("DrawChatActionStrip", "System.Void"),
        new BridgeAbiContract("TryHandlePluginChatCommand", "System.Boolean", "System.String"),
        new BridgeAbiContract("FormatPlayerChatText", "System.String", "System.String"),
        new BridgeAbiContract("DecorateStoredChatMessage", "System.Object", "System.Object", "Microsoft.Xna.Framework.Color", "System.String"),
        new BridgeAbiContract("PrepareStoredChatMessageText", "System.String", "System.String", "System.Object"),
        new BridgeAbiContract("BeginStoredChatMessageDecoration", "System.Void"),
        new BridgeAbiContract("BeginStoredChatMessageDecorationForContainer", "System.Void", "System.Object"),
        new BridgeAbiContract("EndStoredChatMessageDecoration", "System.Void"),
        new BridgeAbiContract("RefreshStoredChatMessagePresentations", "System.Void"),
        new BridgeAbiContract("ShouldDisplayNetworkChatMessage", "System.Boolean", "System.Byte"),
        new BridgeAbiContract("ShouldDisplayLocalChatMessage", "System.Boolean"),
        new BridgeAbiContract("HandleChatSnippetHover", "System.Void", "System.Object"),
        new BridgeAbiContract("HandleChatSnippetClick", "System.Boolean", "System.Object"),
        new BridgeAbiContract("GetChatSnippetVisibleColor", "Microsoft.Xna.Framework.Color", "System.Object", "Microsoft.Xna.Framework.Color"),
        new BridgeAbiContract("CopyChatSnippetContext", "System.Void", "System.Object", "System.Object")
    };

    internal static IReadOnlyList<BridgeAbiContract> GetContracts() => Contracts;

    internal static BridgeAbiContract Require(string name)
    {
        for (var index = 0; index < Contracts.Length; index++)
        {
            if (string.Equals(Contracts[index].Name, name, StringComparison.Ordinal))
            {
                return Contracts[index];
            }
        }

        throw new ClientBuildException("Permanent patch catalog references a bridge method absent from the ABI contract catalog: " + name + ".");
    }
}
