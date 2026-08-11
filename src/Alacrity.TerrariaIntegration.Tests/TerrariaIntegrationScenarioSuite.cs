using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Alacrity.Core;
using Alacrity.PluginSdk;
using AlacrityTerraria.GameState.Combat;
using AlacrityTerraria.GameState.Entities;
using AlacrityTerraria.Rendering.Hud;
using AlacrityTerraria.Rendering.Overlays;
using AlacrityTerraria.Rendering.Projection;
using AlacrityTerraria.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AlacrityTerraria;

/// <summary>
/// Integration scenarios that can run without launching Terraria. Graphics-device probing remains
/// opt-in because legacy XNA device teardown is not stable under all headless test environments.
/// </summary>
public static class TerrariaIntegrationScenarioSuite
{
    public static IEnumerable<object[]> GetScenarioCases(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("A scenario category is required.", nameof(category));
        }

        return GetScenarioMethods()
            .Where(method => string.Equals(GetScenarioCategory(method.Name), category, StringComparison.Ordinal))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .Select(method => new object[] { method.Name });
    }

    private static IEnumerable<MethodInfo> GetScenarioMethods()
    {
        return typeof(TerrariaIntegrationScenarioSuite)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.IsPrivate && method.Name != nameof(RunAll) && method.ReturnType == typeof(void) && method.GetParameters().Length == 0 && !method.Name.StartsWith("VerifyGraphics", StringComparison.Ordinal));
    }

    private static string GetScenarioCategory(string name)
    {
        if (name.IndexOf("Bridge", StringComparison.Ordinal) >= 0 || name.IndexOf("Staged", StringComparison.Ordinal) >= 0)
        {
            return "Bridge";
        }

        if (name.IndexOf("Projection", StringComparison.Ordinal) >= 0 || name.IndexOf("Presentation", StringComparison.Ordinal) >= 0)
        {
            return "Rendering";
        }

        return "GameState";
    }

    public static void RunScenario(string name)
    {
        MethodInfo scenario = typeof(TerrariaIntegrationScenarioSuite).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static);
        if (scenario == null || scenario.ReturnType != typeof(void) || scenario.GetParameters().Length != 0 || scenario.Name.StartsWith("VerifyGraphics", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unknown Terraria integration scenario: " + name, nameof(name));
        }

        try
        {
            scenario.Invoke(null, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    internal static void RunAll()
    {
        VerifyConcurrentSnapshotDemandAcquisition();
        VerifyEntityGenerationReuse();
        VerifyProjectionStates();
        VerifyPresentationStateTransitions();
        VerifyBridgeHandshakeParsing();
        VerifyBridgeAbiContract();
        VerifyStagedRuntimeArtifacts();
    }

    [STAThread]
    internal static void VerifyGraphicsDeviceResources()
    {
        using var window = new Form();
        window.CreateControl();
        using GraphicsDevice firstDevice = CreateDevice(window.Handle);
        using GraphicsDevice secondDevice = CreateDevice(window.Handle);
        using var firstBatch = new SpriteBatch(firstDevice);
        using var secondBatch = new SpriteBatch(secondDevice);
        using var resources = new TerrariaOverlayGraphicsResources();
        resources.Prepare(firstBatch.GraphicsDevice);
        Assert(resources.TryGetPixel(out Texture2D firstPixel), "The first SpriteBatch device must create an integration-owned pixel texture.");
        resources.Prepare(secondBatch.GraphicsDevice);
        Assert(firstPixel.IsDisposed, "Replacing the GraphicsDevice must dispose the texture owned by the prior device.");
        Assert(resources.TryGetPixel(out Texture2D secondPixel) && !ReferenceEquals(firstPixel, secondPixel), "The replacement SpriteBatch device must receive a new pixel texture.");
        VerifyAvatarBatchIsolation(firstBatch, secondBatch, secondPixel);
    }

    private static GraphicsDevice CreateDevice(IntPtr handle)
    {
        var parameters = new PresentationParameters
        {
            BackBufferWidth = 1,
            BackBufferHeight = 1,
            BackBufferFormat = SurfaceFormat.Color,
            DepthStencilFormat = DepthFormat.None,
            DeviceWindowHandle = handle,
            IsFullScreen = false,
            PresentationInterval = PresentInterval.Immediate
        };
        return new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.Reach, parameters);
    }

    private static void VerifyAvatarBatchIsolation(SpriteBatch firstBatch, SpriteBatch secondBatch, Texture2D texture)
    {
        Assert(TerrariaAvatarRenderBoundary.UsesExpectedBatch(firstBatch, firstBatch), "The native Terraria SpriteBatch must be accepted for avatar rendering.");
        Assert(!TerrariaAvatarRenderBoundary.UsesExpectedBatch(secondBatch, firstBatch), "A different SpriteBatch must be rejected before the native avatar renderer can alter it.");

        firstBatch.Begin();
        Assert(TerrariaAvatarRenderBoundary.TryDraw(firstBatch, firstBatch, () => firstBatch.Draw(texture, new Rectangle(0, 0, 1, 1), Color.White)), "The avatar boundary must preserve a usable active batch.");
        firstBatch.Draw(texture, new Rectangle(0, 0, 1, 1), Color.White);
        firstBatch.End();
    }

    private static void VerifyEntityGenerationReuse()
    {
        var tracker = new EntityGenerationTracker();
        tracker.EnsureCapacity(8, 8, 8);
        VerifyGenerationReuse(tracker, PluginEntityKind.Player, 3);
        VerifyGenerationReuse(tracker, PluginEntityKind.Npc, 4);
        VerifyGenerationReuse(tracker, PluginEntityKind.Projectile, 5);
        VerifyGenerationDemandGap(tracker, PluginEntityKind.Player, 6);
        VerifyGenerationDemandGap(tracker, PluginEntityKind.Npc, 6);
        VerifyGenerationDemandGap(tracker, PluginEntityKind.Projectile, 6);
        Assert(!tracker.GetHandle(PluginEntityKind.Projectile, 99, true).IsValid, "Out-of-range slots must never produce a handle.");
        Assert(!tracker.GetHandle(PluginEntityKind.Player, -1, true).IsValid, "Negative slots must never produce a handle.");
    }

    private static void VerifyGenerationReuse(EntityGenerationTracker tracker, PluginEntityKind kind, int slot)
    {
        PluginEntityHandle first = tracker.GetHandle(kind, slot, true);
        Assert(first.IsValid, "An active slot must receive a valid generation-aware handle.");
        Assert(tracker.GetHandle(kind, slot, true) == first, "An unchanged active slot must retain its generation.");
        tracker.GetHandle(kind, slot, false);
        PluginEntityHandle replacement = tracker.GetHandle(kind, slot, true);
        Assert(replacement != first && replacement.Slot == first.Slot && replacement.Kind == kind, "Slot reuse must produce a new handle generation for every slot-backed entity kind.");
    }

    private static void VerifyGenerationDemandGap(EntityGenerationTracker tracker, PluginEntityKind kind, int slot)
    {
        PluginEntityHandle beforeGap = tracker.GetHandle(kind, slot, true);
        tracker.InvalidateObservation(kind);
        PluginEntityHandle afterGap = tracker.GetHandle(kind, slot, true);
        Assert(afterGap != beforeGap, "An entity observed after a demand gap must not retain the former occupant's generation.");
    }

    private static void VerifyConcurrentSnapshotDemandAcquisition()
    {
        var cache = new TerrariaEntitySnapshotCache();
        var manifest = new PluginManifest(new PluginId("integration.snapshot"), "Snapshot test", new Version(1, 0), "Tests", "Snapshot demand test", new[] { "1.4.5.6" }, capabilities: PluginCapability.GameStateRead, permissions: PluginPermission.ReadGameState);
        using (var scope = new PluginResourceScope())
        {
            IPluginPlayerService players = cache.CreatePlayerService(manifest, scope);
            var started = new ManualResetEventSlim(false);
            var reads = new Task[24];
            for (int index = 0; index < reads.Length; index++)
            {
                reads[index] = Task.Run(() =>
                {
                    started.Wait();
                    var destination = new List<PluginPlayerSnapshot>();
                    players.CopyPlayers(destination);
                });
            }

            started.Set();
            Task.WaitAll(reads);
            TerrariaSnapshotDemandCounts counts = cache.GetDemandCounts();
            Assert(counts.Players == 1 && counts.Entities == 0 && counts.Buffs == 0, "Concurrent first player reads must retain exactly one player demand and no unrelated demand.");
            scope.Dispose();
            Assert(cache.GetDemandCounts().Players == 0, "Scope disposal must release the single lazily acquired player demand.");
        }

        var racingCache = new TerrariaEntitySnapshotCache();
        var racingScope = new PluginResourceScope();
        IPluginPlayerService racingPlayers = racingCache.CreatePlayerService(manifest, racingScope);
        var gate = new ManualResetEventSlim(false);
        var tasks = new Task[17];
        for (int index = 0; index < 16; index++)
        {
            tasks[index] = Task.Run(() =>
            {
                gate.Wait();
                try { racingPlayers.CopyPlayers(new List<PluginPlayerSnapshot>()); }
                catch (ObjectDisposedException) { }
            });
        }
        tasks[16] = Task.Run(() => { gate.Wait(); racingScope.Dispose(); });
        gate.Set();
        Task.WaitAll(tasks);
        Assert(racingCache.GetDemandCounts().Players == 0, "A first-read/disposal race must not leak player snapshot demand.");
    }

    private static void VerifyProjectionStates()
    {
        Assert(TerrariaWorldProjectionVerifier.TryVerify(new TerrariaWorldProjectionState(120f, -64f, 0.75f, 0.75f, 1f), out _), "Projection verification must accept live zoomed-in camera state.");
        Assert(TerrariaWorldProjectionVerifier.TryVerify(new TerrariaWorldProjectionState(-30f, 80f, 1.5f, 1.5f, -1f), out _), "Projection verification must accept live flipped-gravity state without an extra view transform.");
        Assert(!TerrariaWorldProjectionVerifier.TryVerify(new TerrariaWorldProjectionState(0f, 0f, float.NaN, 1f, 1f), out _), "Projection verification must reject invalid live presentation state.");
    }

    private static void VerifyPresentationStateTransitions()
    {
        var tracker = new ClientPresentationStateTracker();
        tracker.Update(true, false, out bool menuChanged, out bool chatChanged);
        Assert(!menuChanged && !chatChanged, "The first update must establish presentation state without fabricating lifecycle events.");
        tracker.Update(false, false, out menuChanged, out chatChanged);
        Assert(menuChanged && !chatChanged, "Leaving the menu must publish only the menu-state transition.");
        tracker.Update(false, true, out menuChanged, out chatChanged);
        Assert(!menuChanged && chatChanged, "Opening player chat must publish only the chat-input transition.");
        tracker.Update(false, true, out menuChanged, out chatChanged);
        Assert(!menuChanged && !chatChanged, "Stable presentation state must not repeatedly publish transitions.");
    }

    internal static void VerifyBridgeAbiContract()
    {
        string path = GetStagedBridgePath();
        Assert(File.Exists(path), "The staged Terraria bridge assembly must be available for ABI verification.");
        Assembly bridgeAssembly = Assembly.LoadFrom(path);
        Type bridge = bridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", true);

        VerifyStaticBridgeMethod(bridge, "GetBridgeHandshake", typeof(string));
        VerifyStaticBridgeMethod(bridge, "BootstrapPluginRuntime", typeof(void));
        VerifyStaticBridgeMethod(bridge, "ShutdownPluginRuntime", typeof(void));
        VerifyStaticBridgeMethod(bridge, "UpdatePluginKeybinds", typeof(void));
        VerifyStaticBridgeMethod(bridge, "ProcessChatInput", typeof(string), typeof(string), typeof(bool));
        VerifyStaticBridgeMethod(bridge, "DrawWorldOverlays", typeof(void), typeof(SpriteBatch));
        VerifyStaticBridgeMethod(bridge, "DrawHudWidgets", typeof(void), typeof(SpriteBatch));
        VerifyStaticBridgeMethod(bridge, "ShouldCreateDust", typeof(bool), typeof(int));
        VerifyStaticBridgeMethod(bridge, "ShouldRunGoreSystem", typeof(bool));

        MethodInfo handshake = bridge.GetMethod("GetBridgeHandshake", BindingFlags.Public | BindingFlags.Static);
        Assert((string)handshake.Invoke(null, null) == "3|2|3|1.4.5.6", "The bridge handshake must identify the matching SDK, host, ABI, and Terraria versions.");
        Assert((string)handshake.Invoke(null, null) == string.Format("{0}|{1}|{2}|1.4.5.6", AlacrityCompatibility.PluginSdk, AlacrityCompatibility.Host, AlacrityCompatibility.BridgeAbi),
            "The self-contained bridge handshake must remain synchronized with the SDK compatibility constants.");
    }

    internal static void VerifyStagedRuntimeArtifacts()
    {
        string root = GetRuntimeArtifactDirectory();
        string bridgePath = GetStagedBridgePath();
        string facadePath = Path.Combine(root, "Alacrity.PluginUiRuntime.dll");
        string facadeImportPath = Path.Combine(root, "bin", "Alacrity.PluginUiRuntime.dll");
        string bootstrapPath = Path.Combine(root, "AlacrityBootstrapRuntime.dll");

        Assert(File.Exists(bridgePath), "Runtime staging must copy the exact bridge DLL loaded by the facade.");
        Assert(File.Exists(facadePath), "Runtime staging must copy the injected PluginUiRuntime facade.");
        Assert(File.Exists(facadeImportPath), "Runtime staging must copy the facade where the version-locked patcher imports it.");
        Assert(File.Exists(bootstrapPath), "Runtime staging must copy the bootstrap runtime assembly.");
        AssertBundledPluginPackage(root, "alacrity.better-chat", "Alacrity.BetterChat.dll");
        AssertBundledPluginPackage(root, "alacrity.player-list", "Alacrity.PlayerList.dll");
        AssertBundledPluginPackage(root, "alacrity.dust-gore-toggle", "Alacrity.DustGoreToggle.dll");
        AssertBundledPluginPackage(root, "alacrity.hitboxes", "Alacrity.Hitboxes.dll");
        AssertBundledPluginPackage(root, "alacrity.visual-diagnostics", "Alacrity.VisualDiagnostics.dll");
        AssertBundledPluginPackage(root, "alacrity.off-screen-culling", "Alacrity.OffScreenCulling.dll");
        AssertBundledPluginPackage(root, "alacrity.kinesin", "Alacrity.Kinesin.dll");
        string manifest = File.ReadAllText(Path.Combine(root, "runtime-manifest.txt"));
        Assert(manifest.Contains("Alacrity.BetterChat.dll"), "The stage manifest must identify the BetterChat package assembly from this build.");
        Assert(manifest.Contains("Alacrity.PlayerList.dll"), "The stage manifest must identify the Player List package assembly from this build.");
        Assert(manifest.Contains("Alacrity.DustGoreToggle.dll"), "The stage manifest must identify the Dust/Gore package assembly from this build.");
        Assert(manifest.Contains("Alacrity.Hitboxes.dll"), "The stage manifest must identify the Hitboxes package assembly from this build.");
        Assert(manifest.Contains("Alacrity.VisualDiagnostics.dll"), "The stage manifest must identify the Visual Diagnostics package assembly from this build.");
        Assert(manifest.Contains("Alacrity.OffScreenCulling.dll"), "The stage manifest must identify the Off-screen Culling package assembly from this build.");
        Assert(AssemblyName.GetAssemblyName(bridgePath).Name == "Alacrity.PluginUiCoreBridge", "The staged bridge file must carry the assembly identity expected by the runtime facade.");

        Assembly facade = Assembly.LoadFrom(facadePath);
        foreach (AssemblyName reference in facade.GetReferencedAssemblies())
        {
            Assert(reference.Name != "Alacrity.PluginSdk", "The injected facade must not require PluginSdk just to validate a stale bridge handshake.");
        }
        Type facadeRuntime = facade.GetType("AlacrityTerraria.PluginUiRuntime", true);
        FieldInfo expectedCompatibility = facadeRuntime.GetField("ExpectedBridgeCompatibility", BindingFlags.NonPublic | BindingFlags.Static);
        object expected = expectedCompatibility.GetValue(null);
        MethodInfo formatHandshake = expected.GetType().GetMethod("ToHandshake", BindingFlags.Public | BindingFlags.Instance);
        Assert((string)formatHandshake.Invoke(expected, null) == "3|2|3|1.4.5.6", "The facade compatibility expectation must remain synchronized with the bridge and SDK constants.");

        Assembly bootstrap = Assembly.LoadFrom(bootstrapPath);
        Type runtime = bootstrap.GetType("AlacrityTerraria.AlacrityBootstrapRuntime", true);
        MethodInfo load = runtime.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo isReady = runtime.GetProperty("IsReady", BindingFlags.Public | BindingFlags.Static);
        Assert(load != null, "The staged bootstrap runtime must expose Load.");
        Assert(isReady != null, "The staged bootstrap runtime must expose IsReady.");
        load.Invoke(null, null);
        Assert((bool)isReady.GetValue(null), "The staged bootstrap runtime must load the staged Core and PluginSdk assemblies.");
    }

    private static void AssertBundledPluginPackage(string runtimeRoot, string pluginId, string assemblyName)
    {
        string packageDirectory = Path.Combine(runtimeRoot, "plugins", pluginId);
        Assert(File.Exists(Path.Combine(packageDirectory, "plugin.json")), "Runtime staging must copy the manifest for " + pluginId + ".");
        Assert(File.Exists(Path.Combine(packageDirectory, assemblyName)), "Runtime staging must copy the assembly for " + pluginId + ".");
    }

    private static string GetStagedBridgePath()
    {
        return Path.Combine(GetRuntimeArtifactDirectory(), "bin", "Alacrity.PluginUiCoreBridge.dll");
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

    internal static void VerifyBridgeHandshakeParsing()
    {
        BridgeCompatibilityDescriptor expected = new BridgeCompatibilityDescriptor(AlacrityCompatibility.PluginSdk, AlacrityCompatibility.Host, AlacrityCompatibility.BridgeAbi, "1.4.5.6");
        Assert(BridgeCompatibilityDescriptor.TryParse("3|2|3|1.4.5.6", out BridgeCompatibilityDescriptor current, out string diagnostic) && current != null && current.TryValidateAgainst(expected, out _),
            "The current bridge handshake must parse and validate through the shared compatibility descriptor.");
        Assert(!BridgeCompatibilityDescriptor.TryParse("2|2|2", out _, out diagnostic) && diagnostic.Contains("exactly four"), "A handshake with the wrong field count must diagnose its shape.");
        Assert(!BridgeCompatibilityDescriptor.TryParse("x|2|2|1.4.5.6", out _, out diagnostic) && diagnostic.Contains("PluginSdk"), "An invalid compatibility integer must identify its field.");
        Assert(!BridgeCompatibilityDescriptor.TryParse("2|2|2|1.4", out _, out diagnostic) && diagnostic.Contains("Terraria"), "A malformed Terraria version must be rejected before runtime startup.");
        BridgeCompatibilityDescriptor stale = new BridgeCompatibilityDescriptor(1, 1, 1, "1.4.4.9");
        Assert(!stale.TryValidateAgainst(expected, out diagnostic) && diagnostic.Contains("PluginSdk") && diagnostic.Contains("Core Host") && diagnostic.Contains("Bridge ABI") && diagnostic.Contains("Terraria"),
            "Compatibility diagnostics must identify every stale component instead of reporting a generic mismatch.");
    }

    private static void VerifyStaticBridgeMethod(Type bridge, string name, Type returnType, params Type[] parameters)
    {
        MethodInfo method = bridge.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
        Assert(method != null, "The bridge ABI method is missing: " + name);
        Assert(method.ReturnType == returnType, "The bridge ABI return type changed: " + name);
        Assert(method.IsStatic && method.IsPublic, "The bridge ABI method visibility changed: " + name);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
