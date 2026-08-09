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
        new BridgeAbiContract("IsBetterChatActive", "System.Boolean"),
        new BridgeAbiContract("BootstrapPluginRuntime", "System.Void"),
        new BridgeAbiContract("ProcessPlayerChatInput", "System.String", "System.String", "System.Boolean"),
        new BridgeAbiContract("TryHandlePluginChatCommand", "System.Boolean", "System.String"),
        new BridgeAbiContract("FormatPlayerChatText", "System.String", "System.String"),
        new BridgeAbiContract("DecorateChatMessage", "System.Object", "System.Object", "Microsoft.Xna.Framework.Color", "System.String"),
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
