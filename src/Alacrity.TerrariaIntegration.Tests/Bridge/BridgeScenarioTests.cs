using System;
using System.IO;
using System.Reflection;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace AlacrityTerraria;

public sealed class BridgeScenarioTests
{
    [Fact]
    public void AbiContractRemainsStable()
    {
        string path = GetStagedBridgePath();
        Assert.True(File.Exists(path), "The staged Terraria bridge assembly must be available for ABI verification.");
        Assembly bridgeAssembly = Assembly.LoadFrom(path);
        Type bridge = bridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", true);

        VerifyStaticBridgeMethod(bridge, "GetBridgeHandshake", typeof(string));
        VerifyStaticBridgeMethod(bridge, "BootstrapPluginRuntime", typeof(void));
        VerifyStaticBridgeMethod(bridge, "ShutdownPluginRuntime", typeof(void));
        VerifyStaticBridgeMethod(bridge, "UpdatePluginKeybinds", typeof(void));
        VerifyStaticBridgeMethod(bridge, "ProcessChatInput", typeof(string), typeof(string), typeof(bool));
        VerifyStaticBridgeMethod(bridge, "PrepareStoredChatMessageText", typeof(string), typeof(string), typeof(object));
        VerifyStaticBridgeMethod(bridge, "DrawWorldOverlays", typeof(void), typeof(SpriteBatch));
        VerifyStaticBridgeMethod(bridge, "DrawHudWidgets", typeof(void), typeof(SpriteBatch));
        VerifyStaticBridgeMethod(bridge, "ShouldCreateDust", typeof(bool), typeof(int));
        VerifyStaticBridgeMethod(bridge, "ShouldRunGoreSystem", typeof(bool));
        VerifyStaticBridgeMethod(bridge, "ShouldDrawPaladinShieldIcon", typeof(bool));
        VerifyStaticBridgeMethod(bridge, "TryBeginRainPresentation", typeof(bool), typeof(bool));
        VerifyStaticBridgeMethod(
            bridge,
            "TryQueueRainPresentation",
            typeof(bool),
            typeof(Texture2D),
            typeof(Vector2),
            typeof(Rectangle?),
            typeof(Color),
            typeof(float),
            typeof(Vector2),
            typeof(float),
            typeof(SpriteEffects),
            typeof(float));
        VerifyStaticBridgeMethod(bridge, "EndRainPresentation", typeof(void));
        VerifyStaticBridgeMethod(
            bridge,
            "TryRunLightingParallel",
            typeof(bool),
            typeof(int),
            typeof(int),
            typeof(Delegate),
            typeof(object));

        MethodInfo handshake = bridge.GetMethod("GetBridgeHandshake", BindingFlags.Public | BindingFlags.Static);
        Assert.Equal("5|2|14|1.4.5.6", (string)handshake.Invoke(null, null));
        Assert.Equal(string.Format("{0}|{1}|{2}|1.4.5.6", AlacrityCompatibility.PluginSdk, AlacrityCompatibility.Host, AlacrityCompatibility.BridgeAbi), (string)handshake.Invoke(null, null));

    }

    [Fact]
    public void FacadeExposesNativeTextInputAbi()
    {
        Assembly facade = Assembly.LoadFrom(GetStagedFacadePath());
        Type bridge = facade.GetType("AlacrityTerraria.PluginUiRuntime", true);

        VerifyStaticBridgeMethod(bridge, "TryProcessNativeTextInput", typeof(bool), typeof(string), typeof(bool), typeof(string).MakeByRefType());
        VerifyStaticBridgeMethod(bridge, "FormatNativeTextInputDisplay", typeof(string), typeof(string));
        VerifyStaticBridgeMethod(bridge, "GetNativeTextInputCaret", typeof(int), typeof(string));
        VerifyStaticBridgeMethod(bridge, "ResetNativeTextInput", typeof(void));
    }

    [Fact]
    public void StagedRuntimeArtifactsAreCoherent()
    {
        string root = GetRuntimeArtifactDirectory();
        string bridgePath = GetStagedBridgePath();
        string facadePath = Path.Combine(root, "Alacrity.PluginUiRuntime.dll");
        string facadeImportPath = Path.Combine(root, "bin", "Alacrity.PluginUiRuntime.dll");
        string bootstrapPath = Path.Combine(root, "AlacrityBootstrapRuntime.dll");

        Assert.True(File.Exists(bridgePath));
        Assert.True(File.Exists(facadePath));
        Assert.True(File.Exists(facadeImportPath));
        Assert.True(File.Exists(bootstrapPath));
        AssertBundledPluginPackage(root, "alacrity.better-chat", "Alacrity.BetterChat.dll");
        AssertBundledPluginPackage(root, "alacrity.player-list", "Alacrity.PlayerList.dll");
        AssertBundledPluginPackage(root, "alacrity.dust-gore-toggle", "Alacrity.DustGoreToggle.dll");
        AssertBundledPluginPackage(root, "alacrity.hitboxes", "Alacrity.Hitboxes.dll");
        AssertBundledPluginPackage(root, "alacrity.visual-diagnostics", "Alacrity.VisualDiagnostics.dll");
        AssertBundledPluginPackage(root, "alacrity.off-screen-culling", "Alacrity.OffScreenCulling.dll");
        AssertBundledPluginPackage(root, "alacrity.kinesin", "Alacrity.Kinesin.dll");
        AssertBundledPluginPackage(root, "alacrity.remove-paladin-shield-icon", "Alacrity.RemovePaladinShieldIcon.dll");
        AssertBundledPluginPackage(root, "alacrity.chat-translation", "Alacrity.ChatTranslation.dll");
        string manifest = File.ReadAllText(Path.Combine(root, "runtime-manifest.txt"));
        Assert.Contains("Alacrity.BetterChat.dll", manifest);
        Assert.Contains("Alacrity.PlayerList.dll", manifest);
        Assert.Contains("Alacrity.DustGoreToggle.dll", manifest);
        Assert.Contains("Alacrity.Hitboxes.dll", manifest);
        Assert.Contains("Alacrity.VisualDiagnostics.dll", manifest);
        Assert.Contains("plugins\\alacrity.chat-translation\\assets\\translate-icon.xnb", manifest);
        Assert.Equal("Alacrity.PluginUiCoreBridge", AssemblyName.GetAssemblyName(bridgePath).Name);

        Assembly facade = Assembly.LoadFrom(facadePath);
        foreach (AssemblyName reference in facade.GetReferencedAssemblies())
        {
            Assert.NotEqual("Alacrity.PluginSdk", reference.Name);
        }

        Type facadeRuntime = facade.GetType("AlacrityTerraria.PluginUiRuntime", true);
        Assert.NotNull(facadeRuntime);

        Assembly bootstrap = Assembly.LoadFrom(bootstrapPath);
        Type runtime = bootstrap.GetType("AlacrityTerraria.AlacrityBootstrapRuntime", true);
        MethodInfo load = runtime.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo isReady = runtime.GetProperty("IsReady", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(load);
        Assert.NotNull(isReady);
        load.Invoke(null, null);
        Assert.True((bool)isReady.GetValue(null));
    }

    [Fact]
    public void HandshakeParsingReportsCompatibilityFailures()
    {
        BridgeCompatibilityDescriptor expected = new BridgeCompatibilityDescriptor(AlacrityCompatibility.PluginSdk, AlacrityCompatibility.Host, AlacrityCompatibility.BridgeAbi, "1.4.5.6");
        Assert.True(BridgeCompatibilityDescriptor.TryParse("5|2|14|1.4.5.6", out BridgeCompatibilityDescriptor current, out string diagnostic));
        Assert.NotNull(current);
        Assert.True(current.TryValidateAgainst(expected, out _));
        Assert.False(BridgeCompatibilityDescriptor.TryParse("2|2|2", out _, out diagnostic));
        Assert.Contains("exactly four", diagnostic);
        Assert.False(BridgeCompatibilityDescriptor.TryParse("x|2|2|1.4.5.6", out _, out diagnostic));
        Assert.Contains("PluginSdk", diagnostic);
        Assert.False(BridgeCompatibilityDescriptor.TryParse("2|2|2|1.4", out _, out diagnostic));
        Assert.Contains("Terraria", diagnostic);
        BridgeCompatibilityDescriptor stale = new BridgeCompatibilityDescriptor(1, 1, 1, "1.4.4.9");
        Assert.False(stale.TryValidateAgainst(expected, out diagnostic));
        Assert.Contains("PluginSdk", diagnostic);
        Assert.Contains("Core Host", diagnostic);
        Assert.Contains("Bridge ABI", diagnostic);
        Assert.Contains("Terraria", diagnostic);
    }

    private static void VerifyStaticBridgeMethod(Type bridge, string name, Type returnType, params Type[] parameters)
    {
        MethodInfo method = bridge.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
        Assert.True(method.IsStatic && method.IsPublic);
    }

    private static void AssertBundledPluginPackage(string runtimeRoot, string pluginId, string assemblyName)
    {
        string packageDirectory = Path.Combine(runtimeRoot, "plugins", pluginId);
        Assert.True(File.Exists(Path.Combine(packageDirectory, "plugin.json")));
        Assert.True(File.Exists(Path.Combine(packageDirectory, assemblyName)));
    }

    private static string GetStagedBridgePath()
    {
        return Path.Combine(GetRuntimeArtifactDirectory(), "bin", "Alacrity.PluginUiCoreBridge.dll");
    }

    private static string GetStagedFacadePath()
    {
        return Path.Combine(GetRuntimeArtifactDirectory(), "Alacrity.PluginUiRuntime.dll");
    }

    private static string GetRuntimeArtifactDirectory()
    {
        string configuredDirectory = Environment.GetEnvironmentVariable("ALACRITY_RUNTIME_ARTIFACT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return configuredDirectory;
        }

        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string integrationProject = Path.Combine(directory.FullName, "src", "Alacrity.TerrariaIntegration", "Alacrity.TerrariaIntegration.csproj");
            if (File.Exists(integrationProject))
            {
                return Path.Combine(directory.FullName, "artifacts", "runtime");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the repository runtime artifact directory. Set ALACRITY_RUNTIME_ARTIFACT_DIRECTORY for an external test host.");
    }
}
