using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (Array.IndexOf(args, "--graphics") >= 0)
            {
                using (var window = new Form())
                {
                    window.CreateControl();
                    using (GraphicsDevice firstDevice = CreateDevice(window.Handle))
                    using (GraphicsDevice secondDevice = CreateDevice(window.Handle))
                    using (var firstBatch = new SpriteBatch(firstDevice))
                    using (var secondBatch = new SpriteBatch(secondDevice))
                    using (var resources = new TerrariaOverlayGraphicsResources())
                    {
                        resources.Prepare(firstBatch.GraphicsDevice);
                        Assert(resources.TryGetPixel(out Texture2D firstPixel), "The first SpriteBatch device must create an integration-owned pixel texture.");
                        resources.Prepare(secondBatch.GraphicsDevice);
                        Assert(firstPixel.IsDisposed, "Replacing the GraphicsDevice must dispose the texture owned by the prior device.");
                        Assert(resources.TryGetPixel(out Texture2D secondPixel) && !ReferenceEquals(firstPixel, secondPixel), "The replacement SpriteBatch device must receive a new pixel texture.");
                        VerifyAvatarBatchIsolation(firstBatch, secondBatch, secondPixel);
                    }
                }
            }
            VerifyConcurrentSnapshotDemandAcquisition();
            VerifyEntityGenerationReuse();
            VerifyProjectionStates();
            VerifyPresentationStateTransitions();
            VerifyBridgeHandshakeParsing();
            VerifyBridgeAbiContract();
            Console.WriteLine("Terraria integration tests passed." + (Array.IndexOf(args, "--graphics") >= 0 ? string.Empty : " Graphics-device validation is opt-in: rerun with --graphics in an interactive desktop session."));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
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

    private static void VerifyBridgeAbiContract()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), "src", "Alacrity.TerrariaIntegration", "bin", "Release", "net472", "Alacrity.TerrariaIntegration.dll");
        Assert(File.Exists(path), "The built Terraria integration assembly must be available for bridge ABI verification.");
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
        Assert((string)handshake.Invoke(null, null) == "2|2|2|1.4.5.6", "The bridge handshake must identify the matching SDK, host, ABI, and Terraria versions.");
    }

    private static void VerifyBridgeHandshakeParsing()
    {
        BridgeCompatibilityDescriptor expected = new BridgeCompatibilityDescriptor(AlacrityCompatibility.PluginSdk, AlacrityCompatibility.Host, AlacrityCompatibility.BridgeAbi, "1.4.5.6");
        Assert(BridgeCompatibilityDescriptor.TryParse("2|2|2|1.4.5.6", out BridgeCompatibilityDescriptor current, out string diagnostic) && current != null && current.TryValidateAgainst(expected, out _),
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
