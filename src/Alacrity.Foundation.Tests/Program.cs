using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.App;
using Alacrity.App.PluginManagement;
using Alacrity.BetterChat;
using Alacrity.DustGoreToggle;
using Alacrity.Hitboxes;
using Alacrity.PlayerList;
using Alacrity.Core;
using Alacrity.PluginSdk;
using AlacrityTerraria;
using AlacrityTerraria.Rendering.Projection;

#pragma warning disable CS0618 // Compatibility-only API coverage remains intentional in this test assembly.

public sealed class LoaderSyncTestPlugin : IAlacrityPlugin
{
    public void Initialize(IPluginContext context) { }
    public void Enable() { }
    public void Disable() { }
    public void Shutdown() { }
}

public sealed class LoaderAsyncTestPlugin : IAsyncAlacrityPlugin
{
    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task EnableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DisableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class Program
{
    private static int Main()
    {
        try
        {
            ManifestRejectsInvalidServerClassification();
            PackageManifestLoadsBeforePluginExecution();
            BundledPluginManifestsRemainValid();
            PluginAssemblyLoaderUsesHostManifestWithoutDllMetadata();
            AsyncPluginAssemblyLoaderUsesSharedRuntimeController();
            HostManifestIsAuthoritativeOverPluginImplementation();
            UnifiedContextExposesAllHostServices();
            PluginSdkHasNoEngineImplementationReferences();
            BundledPluginsInitializeInFakeHost();
            FakeHostRecordsPluginDiagnostics();
            FakeHostRecordsRealRegistrations();
            EntityHandlesPreserveGenerationIdentity();
            PluginResourceKindValuesRemainStable();
            BridgeReflectionResolverCachesSuccessfulLookups();
            BridgeReflectionResolverReportsUnavailableSignatures();
            TileStoragePreservesCompactDataAndMaterialization();
            TileReferencePreservesMapAndStandaloneIdentity();
            TileStorageCopiesAndClearsPredictably();
            TileStorageBulkOperationsPreserveSnapshots();
            TileStorageMaterializationBitmapPreservesWordBoundaries();
            TileStorageRejectsStaleWorldHandles();
            TrustMetadataRejectsMalformedHash();
            PatchAppliesAndRollsBackWithMockFiles();
            PatchRefusesUnexpectedContentAndWrongOwner();
            PatchRefusesTamperedBackupAndAlreadyPatchedWithoutBackup();
            PatchBindsOwnerAndReservesCanonicalPaths();
            PatchValidatesReplacementBeforeMutation();
            PatchVerifiesFreshBackupBeforeMutation();
            PatchRefusesTargetChangedAfterVerification();
            PatchReportsFailedRecovery();
            PatchRollbackRestoresMissingTarget();
            PatchRecoveryReconcilesOnlyVerifiedStates();
            ManagedPatchStoreRejectsPathEscape();
            FilePatchJournalReloadsTransactions();
            PluginStorageCreatesNonDestructivePackageLayout();
            PluginMenuPlacesPluginsBeforeWorkshopAndToggles();
            LifecycleCleansResourcesInReverseOrder();
            LifecycleReactivationCreatesFreshScopedContext();
            LifecycleFailureFaultsAndCleansResources();
            LifecyclePreservesCallbackFailureAndRecordsCleanupFailure();
            LifecycleUninstallReachesTerminalStateAfterFailures();
            AsyncLifecycleSupportsMixedActivationCancellationAndTimeout();
            AsyncLifecycleCancelsAfterCallbackStarts();
            AsyncUninstallPropagatesLifecycleFailures();
            AsyncShutdownIsBoundedAndRetainsFailures();
            ResourceScopeReleasesChildrenInParentOrder();
            ResourceScopeRecordsIndividualCleanupFailures();
            ActivationTransactionRollsBackInReverseOrder();
            PatchServiceRequiresPermissionTrustAndPolicy();
            ScopedServicesRespectDependenciesAndCleanup();
            ExtensionRegistrationsAreScopeOwned();
            ExtensionServicesRequireOwnersAndIsolateScopes();
            IconInteractionsAreOwnedAndResolvedByHost();
            UiRegistrationsRejectDuplicateOwnerLocalIds();
            SettingsControlsAreParentedAndLegacyControlsRemainCompatible();
            KeybindsAreOwnedQualifiedAndScopeReleased();
            KeybindDescriptorsValidateActivationAndSnapshotsRemainOwned();
            OwnerQualifiedHostServiceLookupRejectsWrongPublisher();
            ChatVisibilityFiltersAreScopeOwned();
            ChatOwnershipCompositionAndPermissionEnforcement();
            UserInteractionServicesRequirePermissionsAndValidateLinks();
            PluginSettingsAvoidNoOpPersistenceAndExposeTypedOldValue();
            TypedSettingsResetRestoresRegisteredDefaults();
            TypedSettingsNormalizeAndReleaseSubscriptions();
            BetterChatUrlDecorationHandlesBalancedAndTrailingPunctuation();
            BetterChatCachesDefaultsWithoutRewritingSettings();
            BetterChatMigratesLegacyVisibilityToToggle();
            DustGoreTogglePublishesScopedPolicyAndManagesExceptions();
            HitboxesPublishesScopedPresentationPolicy();
            PlayerListPublishesPresentationSettingsAndDefaults();
            HudWidgetsAreOwnedAndIsolated();
            OverlayDispatchIsOrderedIsolatedAndScopeOwned();
            RendererFailureSuspensionRecoversAfterCooldown();
            WorldProjectionUsesOnlyTheVerifiedCameraTranslation();
            PluginDataAndSettingsStayIsolated();
            EnablePlannerAutoEnablesDependencies();
            DependencyWarningsClearWhenResolved();
            NotificationsExpireWithoutPersistence();
            NotificationServicesRejectReleasedScopesAndRateLimit();
            NotificationPublicationCannotOutliveScopeCleanup();
            DispatcherHonorsFrameBudget();
            DispatcherRetainsPhysicalQueueSlotsAfterCancellation();
            SchedulerUsesDispatcherAndActivationCleanup();
            SchedulerElapsedWorkUsesMonotonicClockUnits();
            BackgroundWorkIsBoundedAndActivationOwned();
            TransientSchedulerAndDispatcherResourcesAreReleased();
            LifecycleDrainsActivationBackgroundWorkBeforeDisableAndReenable();
            AsyncLifecycleDrainsActivationBackgroundWorkBeforeDisable();
            ChatDecoratorOwnershipDoesNotRequireAnEditor();
            DistinctTypedSettingDefinitionsAreRejected();
            PackageCatalogReadsManifestWithoutAssemblyLoad();
            PackageCompatibilityRejectsStalePluginBeforeAssemblyLoad();
            PackageCompatibilityDiagnosesHostAndBridgeRequirements();
            IncompatibleGameVersionNeverLoadsAssembly();
            PackageRegistryRetainsHostLoadFailure();
            PresenterProjectsRuntimePackageRows();
            SettingsSchemaMigrationPersistsOnce();
            PluginUninstallPreservesOrRemovesOnlySelectedData();
            SettingsValidationFeatureScopeAndAtomicDataWrites();
            Console.WriteLine("Alacrity foundation tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ManifestRejectsInvalidServerClassification()
    {
        var manifest = new PluginManifest(
            new PluginId("example.plugin"),
            "Example",
            new Version(1, 0),
            "Tests",
            "Test plugin",
            new[] { "1.4.5.6" },
            requiresServerSupport: true);

        AssertThrows<InvalidOperationException>(() => manifest.Validate());
    }

    private static void PackageManifestLoadsBeforePluginExecution()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-manifest-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "plugin.json"), "{\n" +
                "  \"schemaVersion\": 1,\n" +
                "  \"id\": \"example.plugin\",\n" +
                "  \"name\": \"Example\",\n" +
                "  \"version\": \"1.0\",\n" +
                "  \"publisher\": \"Tests\",\n" +
                "  \"description\": \"Test plugin\",\n" +
                "  \"supportedGameVersions\": [\"1.4.5.6\"],\n" +
                "  \"capabilities\": [\"Diagnostics\"],\n" +
                "  \"permissions\": []\n" +
                "}");

            var manifest = new PluginPackageManifestReader().ReadFromPackage(root);
            Assert(manifest.Id == new PluginId("example.plugin"), "plugin.json must define the host manifest ID.");
            Assert(manifest.Capabilities == PluginCapability.Diagnostics, "plugin.json must define host-issued capabilities.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void BundledPluginManifestsRemainValid()
    {
        string alacrityRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string packagesRoot = Path.Combine(alacrityRoot, "Plugins");
        var reader = new PluginPackageManifestReader();
        foreach (string packageDirectory in Directory.GetDirectories(packagesRoot))
        {
            PluginManifest manifest = reader.ReadFromPackage(packageDirectory);
            Assert(manifest.Id.IsValid, "Bundled package manifests must retain valid plugin identities.");
        }
    }

    private static void PluginAssemblyLoaderUsesHostManifestWithoutDllMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-loader-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string package = Path.Combine(root, "plugins", "alacrity.loader-sync-test");
            Directory.CreateDirectory(package);
            string assemblyName = typeof(LoaderSyncTestPlugin).Assembly.GetName().Name + ".dll";
            File.Copy(typeof(LoaderSyncTestPlugin).Assembly.Location, Path.Combine(package, assemblyName));
            File.WriteAllText(Path.Combine(package, "plugin.json"), "{\"schemaVersion\":1,\"id\":\"alacrity.loader-sync-test\",\"name\":\"Loader Sync Test\",\"version\":\"0.1.0\",\"publisher\":\"Tests\",\"description\":\"Loader test\",\"supportedGameVersions\":[\"1.4.5.6\"],\"entryAssembly\":\"" + assemblyName + "\",\"entryType\":\"" + typeof(LoaderSyncTestPlugin).FullName + "\"}");
            var packageDescriptor = new PluginPackageCatalog(new PluginPackageManifestReader()).Discover(root)[0];
            var plugin = new PluginAssemblyLoader().Load(packageDescriptor);
            Assert(plugin.GetType().GetProperty("Manifest") == null, "Loaded plugins must not supply authoritative manifest metadata from their DLL.");
            var resources = new PluginResourceScope();
            using (var lifecycle = new PluginLifecycleController(plugin, new TestContext(packageDescriptor.Manifest, resources)))
            {
                lifecycle.Validate();
                lifecycle.Initialize();
                lifecycle.Enable();
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void HostManifestIsAuthoritativeOverPluginImplementation()
    {
        var resources = new PluginResourceScope();
        var plugin = new TestPlugin(resources, new List<string>(), false);
        var hostManifest = new PluginManifest(
            new PluginId("example.plugin"),
            "Example",
            new Version(1, 0),
            "Tests",
            "Test plugin",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Diagnostics,
            permissions: PluginPermission.Clipboard);
        var context = new TestContext(hostManifest, resources);
        using (var lifecycle = new PluginLifecycleController(plugin, context))
        {
            lifecycle.Validate();
            Assert(lifecycle.Manifest == hostManifest, "The lifecycle must expose the manifest loaded from plugin.json through the host context.");
        }
        Assert(typeof(TestPlugin).GetProperty("Manifest") == null, "Plugin implementations must not own a manifest property.");
        resources.Dispose();
    }

    private static void AsyncPluginAssemblyLoaderUsesSharedRuntimeController()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-async-loader-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string package = Path.Combine(root, "plugins", "alacrity.loader-async-test");
            Directory.CreateDirectory(package);
            string assemblyName = typeof(LoaderAsyncTestPlugin).Assembly.GetName().Name + ".dll";
            File.Copy(typeof(LoaderAsyncTestPlugin).Assembly.Location, Path.Combine(package, assemblyName));
            File.WriteAllText(Path.Combine(package, "plugin.json"), "{\"schemaVersion\":1,\"id\":\"alacrity.loader-async-test\",\"name\":\"Loader Async Test\",\"version\":\"0.1.0\",\"publisher\":\"Tests\",\"description\":\"Async loader test\",\"supportedGameVersions\":[\"1.4.5.6\"],\"pluginSdkCompatibilityVersion\":2,\"hostCompatibilityVersion\":2,\"bridgeAbiVersion\":2,\"entryAssembly\":\"" + assemblyName + "\",\"entryType\":\"" + typeof(LoaderAsyncTestPlugin).FullName + "\"}");
            var descriptor = new PluginPackageCatalog(new PluginPackageManifestReader()).Discover(root).Single();
            object entry = new PluginAssemblyLoader().LoadAny(descriptor);
            Assert(entry is IAsyncAlacrityPlugin && entry is not IAlacrityPlugin, "The loader must accept exactly the asynchronous lifecycle contract.");
            var runtime = new PluginRuntimeHost(new PluginPackageCatalog(new PluginPackageManifestReader()), new PluginAssemblyLoader(), new PluginHostContextFactory(root, new PluginServiceHub(), new PluginExtensionHost(), new PluginCommandHost()));
            var controller = runtime.LoadTrusted(descriptor, new PluginTrustVerificationResult(PluginTrustLevel.LocallyTrusted, "test"), new TestLogger(), new TestMultiplayerSession());
            Assert(controller.UsesAsyncLifecycle, "The package runtime must use the shared async-aware lifecycle controller.");
            controller.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            controller.EnableAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(controller.State == PluginLifecycleState.Enabled, "The runtime must activate manifest-declared asynchronous plugins.");
            controller.DisposeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void UnifiedContextExposesAllHostServices()
    {
        var resources = new PluginResourceScope();
        var context = new TestContext(CreateManifest(), resources);
        Assert(context.Settings != null && context.Storage != null && context.Events != null && context.Commands != null, "The final plugin context must expose settings, storage, events, and commands.");
        Assert(context.Keybinds != null && context.Ui != null && context.Services != null && context.Multiplayer != null, "The final plugin context must expose keybinds, UI, services, and multiplayer state.");
        Assert(context.Notifications != null && context.UserInteraction != null, "The final plugin context must expose scoped notifications and permission-gated user interaction services.");
        resources.Dispose();
    }

    private static void PluginResourceKindValuesRemainStable()
    {
        Assert((int)PluginResourceKind.Patch == 0 && (int)PluginResourceKind.EventSubscription == 9 && (int)PluginResourceKind.NativeHandle == 7, "Public resource-kind numeric values must remain stable for compiled plugin compatibility.");
    }

    private static void EntityHandlesPreserveGenerationIdentity()
    {
        var first = new PluginEntityHandle(PluginEntityKind.Player, 4, 1);
        var replacement = new PluginEntityHandle(PluginEntityKind.Player, 4, 2);
        var same = new PluginEntityHandle(PluginEntityKind.Player, 4, 1);
        Assert(first == same && first != replacement && first.GetHashCode() == same.GetHashCode(), "Entity handles must include their generation in equality and hashing.");
        Assert(!default(PluginEntityHandle).IsValid, "The default entity handle must never identify a live entity.");
        var snapshot = new PluginPlayerSnapshot(first, "Player", 0, true, false, false, 100, 100, 0, false);
        Assert(snapshot.Handle == first && snapshot.Id == 4, "Player snapshots must retain a generation-aware handle while preserving slot compatibility.");
    }

    private static void BundledPluginsInitializeInFakeHost()
    {
        using var host = new FakePluginHost();
        var plugins = new IAlacrityPlugin[]
        {
            new BetterChatPlugin(), new DustGoreTogglePlugin(), new HitboxesPlugin(), new PlayerListPlugin()
        };
        for (int index = 0; index < plugins.Length; index++)
        {
            PluginManifest manifest = CreateBundledTestManifest("fake.plugin." + index);
            PluginHostContext first = host.Create(manifest);
            using var controller = new PluginLifecycleController(plugins[index], first, () => host.Create(manifest));
            controller.Validate();
            controller.Initialize();
            controller.Enable();
            controller.Disable();
            controller.Initialize();
            controller.Enable();
            controller.Disable();
        }
    }

    private static void FakeHostRecordsPluginDiagnostics()
    {
        using var host = new FakePluginHost();
        PluginHostContext context = host.Create(CreateManifest());
        context.Logger.Info("recorded by the fake host");
        Assert(host.Diagnostics.Count == 1 && host.Diagnostics[0].Contains("recorded by the fake host"), "The fake host must record plugin-attributed diagnostics while exercising real Core contexts.");
    }

    private static void FakeHostRecordsRealRegistrations()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateBundledTestManifest("fake.recording");
        PluginHostContext context = host.Create(manifest);
        context.Keybinds.Register(new PluginKeybindDescriptor("recorded", "K", "Recorded"), () => { });
        context.Commands.Register(new PluginCommandDescriptor("recorded", "Recorded command"), invocation => invocation.Reply("ok"));
        context.Ui.RegisterSettingsPage(new PluginUiContribution("recorded-page", "Recorded page"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("recorded-toggle", "Recorded toggle", () => true, _ => { }).InPage("recorded-page"));
        context.Notifications.Show("recorded notification");

        Assert(host.Keybinds.Registrations.Count == 1, "The fake host must expose real host keybind registrations.");
        Assert(host.GetSettingsPages(manifest.Id).Count == 1 && host.GetSettingsControls(manifest.Id).Count == 1, "The fake host must expose retained UI registrations.");
        string reply = string.Empty;
        Assert(host.DispatchCommand("recorded", Array.Empty<string>(), value => reply = value) == PluginCommandDispatchResult.Handled && reply == "ok", "The fake host must dispatch real registered commands.");
        Assert(host.ActiveNotifications.Count == 1, "The fake host must expose notifications published through its real Core service.");
    }

    private static void PluginSdkHasNoEngineImplementationReferences()
    {
        var forbidden = new[] { "Terraria", "ReLogic", "Microsoft.Xna", "Alacrity.Core", "Alacrity.TerrariaIntegration" };
        foreach (var reference in typeof(IPluginContext).Assembly.GetReferencedAssemblies())
            Assert(!forbidden.Contains(reference.Name), "PluginSdk must remain independently implementable and must not reference " + reference.Name + ".");
    }

    private static PluginManifest CreateBundledTestManifest(string id)
    {
        return new PluginManifest(new PluginId(id), "Fake bundled plugin", new Version(1, 0), "Tests", "Fake host test", new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Input | PluginCapability.Rendering | PluginCapability.GameStateRead | PluginCapability.MultiplayerObservation,
            permissions: PluginPermission.DrawUserInterface | PluginPermission.ReadGameState | PluginPermission.ObserveMultiplayer | PluginPermission.Clipboard | PluginPermission.OpenExternalLinks);
    }

    private static void BridgeReflectionResolverCachesSuccessfulLookups()
    {
        var resolver = new BridgeReflectionResolver();
        Assert(resolver.TryResolveStaticMethod(typeof(BridgeReflectionFixture), "Draw", typeof(void), new[] { typeof(int) }, out var first, out _), "The bridge resolver must find an exact static method.");
        Assert(resolver.TryResolveStaticMethod(typeof(BridgeReflectionFixture), "Draw", typeof(void), new[] { typeof(int) }, out var second, out _), "The cached bridge lookup must remain available.");
        Assert(ReferenceEquals(first, second), "Repeated exact bridge lookups must return cached metadata.");
        Assert(resolver.TryResolveStaticField(typeof(BridgeReflectionFixture), "Counter", typeof(int), out var field, out _), "The bridge resolver must find an exact static field.");
        Assert(field != null, "Resolved field metadata must be retained.");
    }

    private static void BridgeReflectionResolverReportsUnavailableSignatures()
    {
        var resolver = new BridgeReflectionResolver();
        Assert(!resolver.TryResolveStaticMethod(typeof(BridgeReflectionFixture), "Draw", typeof(void), Type.EmptyTypes, out _, out var diagnostic), "The bridge resolver must reject an incorrect method signature.");
        Assert(diagnostic.StartsWith("Unavailable:", StringComparison.Ordinal), "Unavailable bridge members must provide a clear diagnostic.");
        Assert(!resolver.TryResolveStaticField(typeof(BridgeReflectionFixture), "Counter", typeof(string), out _, out diagnostic), "The bridge resolver must reject an incorrect field type.");
        Assert(diagnostic.StartsWith("Unavailable:", StringComparison.Ordinal), "Unexpected field types must be reported as unavailable rather than invoked.");
    }

    private static void TileStoragePreservesCompactDataAndMaterialization()
    {
        Assert(System.Runtime.InteropServices.Marshal.SizeOf<TileData>() == 14, "TileData must remain a compact fourteen-byte vanilla state representation.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.Type)).ToInt32() == 0, "Tile type must begin the compact layout.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.Wall)).ToInt32() == 2, "Tile wall must remain adjacent to type.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.TileHeader)).ToInt32() == 4, "Tile header must retain its compact offset.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.FrameX)).ToInt32() == 6, "FrameX must retain its compact offset.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.FrameY)).ToInt32() == 8, "FrameY must retain its compact offset.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.Liquid)).ToInt32() == 10, "Liquid must retain its compact offset.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.Header)).ToInt32() == 11, "Header must retain its compact offset.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.Header2)).ToInt32() == 12, "Header2 must retain its compact offset.");
        Assert(System.Runtime.InteropServices.Marshal.OffsetOf<TileData>(nameof(TileData.Header3)).ToInt32() == 13, "Header3 must retain its compact offset.");
        var map = new AlacrityTileMap(4, 3);
        Assert(map.Count == 12, "The tile map must use one flat storage element per coordinate.");
        Assert(!map.IsMaterialized(2, 1), "New map coordinates must retain vanilla null-slot semantics until materialized.");

        ref TileData tile = ref map.EnsureMaterialized(2, 1);
        tile.Type = 321;
        tile.Wall = 42;
        tile.TileHeader = 0x8A5A;
        tile.Header = 3;
        tile.Header2 = 4;
        tile.Header3 = 5;
        tile.Liquid = 255;
        tile.FrameX = -18;
        tile.FrameY = 36;

        TileSnapshot snapshot = map.GetSnapshot(2, 1);
        Assert(snapshot.IsMaterialized && snapshot.Data.Type == 321 && snapshot.Data.FrameX == -18 && snapshot.Data.Header3 == 5, "Tile snapshots must include all compact state and materialization.");
        AssertThrows<ArgumentOutOfRangeException>(() => map.GetSnapshot(-1, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => map.GetSnapshot(4, 0));
    }

    private static void TileStorageCopiesAndClearsPredictably()
    {
        var map = new AlacrityTileMap(5, 2);
        TileData value = default(TileData);
        value.Type = 7;
        value.Wall = 8;
        value.FrameX = 9;
        map.FillRegion(0, 0, 3, 1, value);
        map.CopyRegion(0, 0, 3, 1, 1, 0);
        Assert(map.GetSnapshot(1, 0).Data.Type == 7 && map.GetSnapshot(3, 0).Data.Wall == 8, "Overlapping region copies must preserve source contents.");

        value.Type = 9;
        map.FillRegion(0, 0, 2, 1, value);
        map.CopyRegion(0, 0, 2, 1, 0, 1);
        Assert(map.GetSnapshot(0, 1).Data.Type == 9 && map.GetSnapshot(1, 1).Data.Type == 9, "Vertical region copies must preserve source contents.");

        map.CopyTileData(1, 0, 4, 1);
        Assert(map.GetSnapshot(4, 1).IsMaterialized && map.GetSnapshot(4, 1).Data.FrameX == 9, "Explicit tile copies must copy contents and materialization.");
        TileData beforeClear = map.GetSnapshot(4, 1).Data;
        map.ClearTile(4, 1);
        TileData afterClear = map.GetSnapshot(4, 1).Data;
        Assert(map.GetSnapshot(4, 1).IsMaterialized && afterClear.Type == beforeClear.Type && afterClear.Wall == beforeClear.Wall && afterClear.FrameX == beforeClear.FrameX, "ClearTile must retain the vanilla fields Terraria.Tile.ClearTile preserves.");
        Assert((afterClear.TileHeader & 0x7460) == 0, "ClearTile must clear active, inactive, slope, and half-brick flags.");
        map.ClearEverything(4, 1);
        Assert(map.GetSnapshot(4, 1).IsMaterialized && map.GetSnapshot(4, 1).Data.Equals(default(TileData)), "ClearEverything must retain a materialized default tile.");
        value.TileHeader = 0xFFFF;
        value.Header = 0xFF;
        value.Header3 = 0xFF;
        map.SetSnapshot(0, 1, new TileSnapshot(value, true));
        map.ClearTileData(0, 1, TileDataMask.Slope | TileDataMask.Wiring | TileDataMask.Actuator);
        TileData selectivelyCleared = map.GetSnapshot(0, 1).Data;
        Assert((selectivelyCleared.TileHeader & 0x7BC0) == 0 && (selectivelyCleared.Header & 0x80) == 0 && selectivelyCleared.Type == 9 && selectivelyCleared.Wall == 8, "Selective map clears must preserve unrelated tile state.");
        map.SetSnapshot(1, 1, new TileSnapshot(value, true));
        map.CopyPaintAndCoating(0, 1, 1, 1);
        TileData copiedPaint = map.GetSnapshot(1, 1).Data;
        Assert(copiedPaint.color() == selectivelyCleared.color() && copiedPaint.invisibleBlock() == selectivelyCleared.invisibleBlock() && copiedPaint.fullbrightBlock() == selectivelyCleared.fullbrightBlock(), "Map paint copies must preserve the source paint and coating state.");
        map.UnmaterializeTile(4, 1);
        Assert(!map.GetSnapshot(4, 1).IsMaterialized, "UnmaterializeTile must represent a vanilla null-slot transition.");

        map.ClearRegion(0, 0, 4, 1);
        Assert(!map.IsMaterialized(0, 0) && !map.IsMaterialized(3, 0), "ClearRegion must reset both values and materialization bits.");
        AssertThrows<ArgumentOutOfRangeException>(() => map.FillRegion(4, 1, 2, 1, value));
    }

    private static void TileReferencePreservesMapAndStandaloneIdentity()
    {
        var map = new AlacrityTileMap(2, 2);
        TileReference nullReference = map.GetReference(1, 1);
        Assert(nullReference.IsNull, "An unmaterialized map coordinate must behave as a null tile reference.");
        AssertThrows<NullReferenceException>(() => { nullReference.GetData(); });

        TileReference first = map.GetOrCreateReference(1, 1);
        TileReference alias = first;
        alias.GetData().Type = 47;
        Assert(map.GetSnapshot(1, 1).Data.Type == 47, "Copied map references must preserve the same compact tile identity.");
        Assert(!first.IsNull && !alias.IsNull, "Materialized map references must be non-null.");

        TileData replacement = default(TileData);
        replacement.Type = 91;
        map.SetSnapshot(1, 1, new TileSnapshot(replacement, true));
        Assert(map.GetSnapshot(1, 1).Data.Type == 91, "Replacing a map slot must update the current map value.");
        Assert(first.GetData().Type == 47 && alias.GetData().Type == 47, "A copied map reference must retain the displaced tile identity after slot replacement.");

        TileReference source = map.GetOrCreateReference(0, 0);
        source.GetData().Type = 122;
        map.SetReference(0, 1, source);
        TileReference assigned = map.GetReference(0, 1);
        assigned.GetData().Wall = 19;
        Assert(source.GetData().Wall == 19, "Tile-array assignment must preserve the source tile identity through an alias.");
        map.SetSnapshot(0, 0, new TileSnapshot(default(TileData), true));
        Assert(assigned.GetData().Type == 122 && assigned.GetData().Wall == 19, "Replacing the source coordinate must not invalidate a tile identity assigned to another coordinate.");

        map.SetReference(1, 0, assigned);
        TileReference chained = map.GetReference(1, 0);
        chained.GetData().FrameX = 144;
        Assert(assigned.GetData().FrameX == 144, "Chained array assignments must retain one shared tile identity.");
        map.SetSnapshot(0, 1, new TileSnapshot(default(TileData), true));
        Assert(chained.GetData().Type == 122 && chained.GetData().FrameX == 144, "Replacing an intermediate alias coordinate must preserve the remaining shared identity.");

        TileReference standalone = TileReference.CreateStandalone();
        TileReference standaloneAlias = standalone;
        standaloneAlias.GetData().Wall = 18;
        Assert(standalone.GetData().Wall == 18, "Copied standalone references must retain class-like mutation identity without entering the world map.");
        Assert(standalone.GetData().Type == 0 && standalone.GetData().Wall == 18, "A standalone tile must begin with vanilla default raw state.");

        TileReference copiedStandalone = TileReference.CreateCopy(standalone);
        copiedStandalone.GetData().Wall = 31;
        Assert(standalone.GetData().Wall == 18 && copiedStandalone.GetData().Wall == 31, "Tile copy construction must copy state without aliasing the source tile identity.");
        TileReference copiedNull = TileReference.CreateCopy(default(TileReference));
        Assert(!copiedNull.IsNull && copiedNull.GetData().Equals(default(TileData)), "Copy construction from a null tile must create a default standalone tile.");

        TileReference runtimeTile = TileReferenceRuntime.Create();
        TileReferenceRuntime.SetTypeValue(runtimeTile, 700);
        TileReferenceRuntime.SetWall(runtimeTile, 91);
        TileReferenceRuntime.SetLiquid(runtimeTile, 255);
        TileReferenceRuntime.SetTileHeader(runtimeTile, 0xA5A5);
        TileReferenceRuntime.SetHeader(runtimeTile, 0x5A);
        TileReferenceRuntime.SetHeader2(runtimeTile, 0x81);
        TileReferenceRuntime.SetHeader3(runtimeTile, 0x42);
        TileReferenceRuntime.SetFrameX(runtimeTile, -144);
        TileReferenceRuntime.SetFrameY(runtimeTile, 126);
        Assert(TileReferenceRuntime.GetTypeValue(runtimeTile) == 700 && TileReferenceRuntime.GetWall(runtimeTile) == 91 && TileReferenceRuntime.GetLiquid(runtimeTile) == 255, "Runtime field lowerings must preserve primary raw tile state.");
        Assert(TileReferenceRuntime.GetTileHeader(runtimeTile) == 0xA5A5 && TileReferenceRuntime.GetHeader(runtimeTile) == 0x5A && TileReferenceRuntime.GetHeader2(runtimeTile) == 0x81 && TileReferenceRuntime.GetHeader3(runtimeTile) == 0x42, "Runtime field lowerings must preserve every header byte.");
        Assert(TileReferenceRuntime.GetFrameX(runtimeTile) == -144 && TileReferenceRuntime.GetFrameY(runtimeTile) == 126 && !TileReferenceRuntime.IsNull(runtimeTile), "Runtime field lowerings must preserve signed frames and null semantics.");
    }

    private static void TileStorageRejectsStaleWorldHandles()
    {
        var host = new AlacrityTileStorageHost();
        AssertThrows<InvalidOperationException>(() => host.GetHandle(0, 0));

        host.Initialize(2, 2);
        TileHandle oldHandle = host.GetHandle(1, 1);
        ref TileData tile = ref oldHandle.EnsureMaterialized();
        tile.Type = 4;
        Assert(oldHandle.GetSnapshot().Data.Type == 4, "A current tile handle must address its active world map.");

        host.Initialize(2, 2);
        AssertThrows<InvalidOperationException>(() => oldHandle.GetSnapshot());
        TileHandle currentHandle = host.GetHandle(1, 1);
        Assert(!currentHandle.GetSnapshot().IsMaterialized, "A world replacement must not leak old tile state into the next map.");

        host.Reset();
        AssertThrows<InvalidOperationException>(() => currentHandle.GetSnapshot());
    }

    private static void TileStorageBulkOperationsPreserveSnapshots()
    {
        AssertRegionCopyMatchesSnapshot(1, 1, 3, 2, 2, 1);
        AssertRegionCopyMatchesSnapshot(2, 1, 3, 2, 1, 1);
        AssertRegionCopyMatchesSnapshot(1, 0, 3, 2, 1, 1);
        AssertRegionCopyMatchesSnapshot(1, 1, 3, 2, 1, 0);

        var map = CreatePatternMap();
        map.ClearRegion(1, 1, 3, 2);
        for (int y = 1; y < 3; y++)
        {
            for (int x = 1; x < 4; x++)
                Assert(!map.GetSnapshot(x, y).IsMaterialized && map.GetSnapshot(x, y).Data.Equals(default(TileData)), "ClearRegion must clear every value and materialization bit in its bounds.");
        }
    }

    private static void AssertRegionCopyMatchesSnapshot(int sourceX, int sourceY, int width, int height, int destinationX, int destinationY)
    {
        var map = CreatePatternMap();
        var before = new TileSnapshot[6, 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 6; x++)
                before[x, y] = map.GetSnapshot(x, y);
        }

        map.CopyRegion(sourceX, sourceY, width, height, destinationX, destinationY);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                TileSnapshot expected = before[x, y];
                if (x >= destinationX && x < destinationX + width && y >= destinationY && y < destinationY + height)
                    expected = before[sourceX + x - destinationX, sourceY + y - destinationY];
                TileSnapshot actual = map.GetSnapshot(x, y);
                Assert(actual.IsMaterialized == expected.IsMaterialized && actual.Data.Equals(expected.Data), "CopyRegion must behave as a snapshot copy for every overlap direction.");
            }
        }
    }

    private static AlacrityTileMap CreatePatternMap()
    {
        var map = new AlacrityTileMap(6, 4);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                bool materialized = ((x + y) & 1) == 0;
                TileData data = default(TileData);
                data.Type = (ushort)(x + y * 6 + 1);
                data.Wall = (ushort)(100 + x + y * 6);
                data.TileHeader = (ushort)(x << 12);
                map.SetSnapshot(x, y, new TileSnapshot(data, materialized));
            }
        }
        return map;
    }

    private static void TileStorageMaterializationBitmapPreservesWordBoundaries()
    {
        var map = new AlacrityTileMap(65, 2);
        int[] boundaryIndices = { 31, 32, 63, 64, 95, 96, 127, 128, 129 };
        foreach (int index in boundaryIndices)
        {
            int x = index % 65;
            int y = index / 65;
            TileData data = default(TileData);
            data.Type = (ushort)(index + 1);
            map.SetSnapshot(x, y, new TileSnapshot(data, true));
        }

        foreach (int index in boundaryIndices)
        {
            int x = index % 65;
            int y = index / 65;
            Assert(map.GetSnapshot(x, y).IsMaterialized && map.GetSnapshot(x, y).Data.Type == index + 1, "Materialization bits must preserve every word-boundary coordinate.");
        }

        map.CopyRegion(30, 0, 34, 1, 31, 1);
        for (int column = 0; column < 34; column++)
        {
            TileSnapshot expected = map.GetSnapshot(30 + column, 0);
            TileSnapshot actual = map.GetSnapshot(31 + column, 1);
            Assert(actual.IsMaterialized == expected.IsMaterialized && actual.Data.Equals(expected.Data), "Cross-word region copies must preserve snapshot materialization and data.");
        }

        map.ClearRegion(31, 1, 34, 1);
        for (int x = 31; x < 65; x++)
            Assert(!map.GetSnapshot(x, 1).IsMaterialized, "Cross-word region clears must clear every affected materialization bit.");
        Assert(map.GetSnapshot(31, 0).IsMaterialized && map.GetSnapshot(32, 0).IsMaterialized, "Region clears must not disturb materialization bits outside the requested row.");
    }

    private static void LifecycleCleansResourcesInReverseOrder()
    {
        var order = new List<string>();
        var resources = new PluginResourceScope();
        var plugin = new TestPlugin(resources, order, false);
        var context = new TestContext(CreateManifest(), resources);
        using (var lifecycle = new PluginLifecycleController(plugin, context))
        {
            lifecycle.Validate();
            lifecycle.Initialize();
            lifecycle.Enable();
            lifecycle.Disable();
            Assert(lifecycle.State == PluginLifecycleState.Disabled, "Disable should return to Disabled.");
            lifecycle.Initialize();
            lifecycle.Enable();
            lifecycle.Disable();
        }

        Assert(order.Count == 4, "Both initialization cycles should release their resources.");
        Assert(order[0] == "second" && order[1] == "first" && order[2] == "second" && order[3] == "first", "Resources must be released in reverse registration order.");
    }

    private static void LifecycleReactivationCreatesFreshScopedContext()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-reactivation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = new PluginManifest(new PluginId("reactivation.plugin"), "Reactivation", new Version(1, 0), "Tests", "Fresh context test", new[] { "1.4.5.6" }, capabilities: PluginCapability.Input | PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
            var factory = new PluginHostContextFactory(root, new PluginServiceHub(), new PluginExtensionHost(), new PluginCommandHost());
            var plugin = new ContextRecordingPlugin();
            var controller = new PluginLifecycleController(plugin, factory.Create(manifest, new TestLogger(), new TestMultiplayerSession()), () => factory.Create(manifest, new TestLogger(), new TestMultiplayerSession()));
            controller.Validate(); controller.Initialize(); controller.Enable(); controller.Disable();
            IPluginContext first = plugin.LastContext!;
            controller.Initialize(); controller.Enable();
            Assert(plugin.InitializeCount == 2 && !ReferenceEquals(first, plugin.LastContext), "Re-enabling a runtime-managed plugin must use a fresh host context.");
            AssertThrows<ObjectDisposedException>(() => first.Hud.Register(new PluginHudWidgetDescriptor("stale"), (_, _) => { }));
            controller.Disable(); controller.Dispose();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void PatchAppliesAndRollsBackWithMockFiles()
    {
        var files = new MockPatchFileStore();
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        files.Put("game.bin", original);
        var definition = new PatchDefinition(
            new PatchId("test.patch"),
            new PluginId("test.plugin"),
            "game.bin",
            verifier.ComputeSha256(original),
            replacement,
            verifier.ComputeSha256(replacement));
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);
        engine.Register(definition);

        var applied = engine.Apply(definition.Id);
        Assert(applied.State == PatchTransactionState.Applied, "Patch should apply after original verification.");
        Assert(BytesEqual(files.ReadAllBytes("game.bin"), replacement), "Target should contain replacement bytes.");
        Assert(BytesEqual(files.ReadAllBytes(HostBackupPath(definition)), original), "Host backup should contain original bytes.");

        var rolledBack = engine.Rollback(definition.Id);
        Assert(rolledBack.State == PatchTransactionState.RolledBack, "Patch should roll back after applied verification.");
        Assert(BytesEqual(files.ReadAllBytes("game.bin"), original), "Rollback should restore original bytes.");
    }

    private static void PatchRefusesUnexpectedContentAndWrongOwner()
    {
        var files = new MockPatchFileStore();
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        files.Put("game.bin", System.Text.Encoding.UTF8.GetBytes("changed"));
        var definition = new PatchDefinition(
            new PatchId("test.patch"),
            new PluginId("test.plugin"),
            "game.bin",
            verifier.ComputeSha256(original),
            replacement,
            verifier.ComputeSha256(replacement));
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);
        engine.Register(definition);

        var failed = engine.Apply(definition.Id);
        Assert(failed.State == PatchTransactionState.Failed, "Unexpected target content must fail closed.");
        AssertThrows<UnauthorizedAccessException>(() => host.ForPlugin(new PluginId("other.plugin")).Apply(definition.Id));
    }

    private static void PatchRefusesTamperedBackupAndAlreadyPatchedWithoutBackup()
    {
        var files = new MockPatchFileStore();
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        files.Put("game.bin", original);
        var definition = new PatchDefinition(
            new PatchId("test.patch"),
            new PluginId("test.plugin"),
            "game.bin",
            verifier.ComputeSha256(original),
            replacement,
            verifier.ComputeSha256(replacement));
        files.Put(HostBackupPath(definition), System.Text.Encoding.UTF8.GetBytes("tampered"));
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);
        engine.Register(definition);

        var failedBackup = engine.Apply(definition.Id);
        Assert(failedBackup.State == PatchTransactionState.Failed, "A tampered backup must fail closed.");
        Assert(BytesEqual(files.ReadAllBytes(definition.TargetPath), original), "A bad backup must never overwrite the original target.");

        files.Put(definition.TargetPath, replacement);
        files.Put(HostBackupPath(definition), System.Text.Encoding.UTF8.GetBytes("tampered"));
        var failedAlreadyPatched = engine.Apply(definition.Id);
        Assert(failedAlreadyPatched.State == PatchTransactionState.Failed, "An already-patched target without a valid backup must fail closed.");
    }

    private static void PatchBindsOwnerAndReservesCanonicalPaths()
    {
        var files = new MockPatchFileStore();
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        var owner = new PluginId("test.plugin");
        var definition = CreatePatch(verifier, "test.patch", owner, "game.bin", original, replacement);
        var host = CreatePatchHost(files, verifier);
        var ownerEngine = host.ForPlugin(owner);

        AssertThrows<UnauthorizedAccessException>(() => host.ForPlugin(new PluginId("other.plugin")).Register(definition));
        ownerEngine.Register(definition);
        AssertThrows<UnauthorizedAccessException>(() => host.ForPlugin(new PluginId("other.plugin")).Apply(definition.Id));

        var targetBackupCollision = CreatePatch(verifier, "other.patch", owner, HostBackupPath(definition), original, replacement);
        AssertThrows<InvalidOperationException>(() => ownerEngine.Register(targetBackupCollision));
        var backupAliasCollision = CreatePatch(verifier, "third.patch", owner, ".\\game.bin", original, replacement);
        AssertThrows<InvalidOperationException>(() => ownerEngine.Register(backupAliasCollision));
    }

    private static void PatchValidatesReplacementBeforeMutation()
    {
        var files = new MockPatchFileStore();
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        files.Put("game.bin", original);
        var definition = new PatchDefinition(
            new PatchId("test.patch"),
            new PluginId("test.plugin"),
            "game.bin",
            verifier.ComputeSha256(original),
            replacement,
            verifier.ComputeSha256(System.Text.Encoding.UTF8.GetBytes("different")));
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);

        AssertThrows<ArgumentException>(() => engine.Register(definition));
        Assert(files.WriteCount == 0, "Invalid replacement metadata must be rejected before any write.");
        Assert(BytesEqual(files.ReadAllBytes(definition.TargetPath), original), "Registration failure must leave the target untouched.");
    }

    private static void PatchVerifiesFreshBackupBeforeMutation()
    {
        var files = new MockPatchFileStore { CorruptNextCopy = true };
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        files.Put("game.bin", original);
        var definition = CreatePatch(verifier, "test.patch", new PluginId("test.plugin"), "game.bin", original, replacement);
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);
        engine.Register(definition);

        var result = engine.Apply(definition.Id);
        Assert(result.State == PatchTransactionState.Failed, "An invalid fresh backup must stop the transaction.");
        Assert(BytesEqual(files.ReadAllBytes(definition.TargetPath), original), "The target must not be written when backup verification fails.");
    }

    private static void PatchReportsFailedRecovery()
    {
        var files = new MockPatchFileStore { CorruptNextWrite = true, FailWriteAfterCorruption = true };
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        files.Put("game.bin", original);
        var definition = CreatePatch(verifier, "test.patch", new PluginId("test.plugin"), "game.bin", original, replacement);
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);
        engine.Register(definition);

        var result = engine.Apply(definition.Id);
        Assert(result.State == PatchTransactionState.RecoveryFailed, "A failed restoration must be reported explicitly.");
        Assert(result.Error != null && result.Error.Contains("Recovery failed"), "The failure record must include the recovery failure.");
    }

    private static void PatchRefusesTargetChangedAfterVerification()
    {
        var files = new MockPatchFileStore { ChangeBeforeNextWrite = true };
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        var external = System.Text.Encoding.UTF8.GetBytes("external-change");
        files.ChangedContents = external;
        files.Put("game.bin", original);
        var definition = CreatePatch(verifier, "test.patch", new PluginId("test.plugin"), "game.bin", original, replacement);
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);
        engine.Register(definition);

        var result = engine.Apply(definition.Id);
        Assert(result.State == PatchTransactionState.Failed, "A target changed after verification must fail without mutation.");
        Assert(BytesEqual(files.ReadAllBytes(definition.TargetPath), external), "The engine must preserve a concurrent external target change.");
    }

    private static void PatchRollbackRestoresMissingTarget()
    {
        var files = new MockPatchFileStore();
        var verifier = new Sha256PatchVerifier();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        files.Put("game.bin", original);
        var definition = CreatePatch(verifier, "test.patch", new PluginId("test.plugin"), "game.bin", original, replacement);
        var host = CreatePatchHost(files, verifier);
        var engine = host.ForPlugin(definition.Owner);
        engine.Register(definition);
        Assert(engine.Apply(definition.Id).State == PatchTransactionState.Applied, "Patch setup must apply.");
        files.Remove(definition.TargetPath);

        var result = engine.Rollback(definition.Id);
        Assert(result.State == PatchTransactionState.RolledBack, "Rollback must recreate a missing target from its verified backup.");
        Assert(BytesEqual(files.ReadAllBytes(definition.TargetPath), original), "Missing target must be restored to the verified original.");
    }

    private static void PatchRecoveryReconcilesOnlyVerifiedStates()
    {
        var files = new MockPatchFileStore();
        var verifier = new Sha256PatchVerifier();
        var journal = new InMemoryPatchJournal();
        var original = System.Text.Encoding.UTF8.GetBytes("vanilla");
        var replacement = System.Text.Encoding.UTF8.GetBytes("patched");
        var definition = CreatePatch(verifier, "test.patch", new PluginId("test.plugin"), "game.bin", original, replacement);
        files.Put(definition.TargetPath, original);
        var host = new PatchHost(files, verifier, journal);
        host.ForPlugin(definition.Owner).Register(definition);

        journal.Record(new PatchTransactionRecord(definition.Id, definition.Owner, PatchTransactionState.Writing));
        var alreadyOriginal = host.RecoverIncompleteTransactions();
        Assert(alreadyOriginal.Count == 1 && alreadyOriginal[0].Record.State == PatchTransactionState.RolledBack, "Verified original content must resolve an interrupted write without mutation.");

        files.Remove(definition.TargetPath);
        files.Put(HostBackupPath(definition), original);
        journal.Record(new PatchTransactionRecord(definition.Id, definition.Owner, PatchTransactionState.RollingBack));
        var restored = host.RecoverIncompleteTransactions();
        Assert(restored.Count == 1 && restored[0].IsResolved, "A missing target must be restored only from a verified backup.");
        Assert(BytesEqual(files.ReadAllBytes(definition.TargetPath), original), "Recovery must restore the verified original bytes.");

        files.Put(definition.TargetPath, System.Text.Encoding.UTF8.GetBytes("unknown"));
        journal.Record(new PatchTransactionRecord(definition.Id, definition.Owner, PatchTransactionState.Writing));
        var unresolved = host.RecoverIncompleteTransactions();
        Assert(unresolved.Count == 1 && !unresolved[0].IsResolved, "Unknown target content must remain unresolved rather than overwritten.");
    }

    private static void ManagedPatchStoreRejectsPathEscape()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-patch-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "game.bin"), "vanilla");
            var store = new ManagedPatchFileStore(root);
            byte[] original = File.ReadAllBytes(Path.Combine(root, "game.bin"));
            byte[] replacement = System.Text.Encoding.UTF8.GetBytes("patched");
            Assert(store.TryWriteAtomically("game.bin", original, replacement), "Managed store should replace an expected snapshot.");
            AssertThrows<UnauthorizedAccessException>(() => store.GetPathIdentity("..\\outside.bin"));
            AssertThrows<UnauthorizedAccessException>(() => store.GetPathIdentity(Path.GetFullPath("outside.bin")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void FilePatchJournalReloadsTransactions()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-patch-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "transactions.journal");
            var id = new PatchId("journal.patch");
            var owner = new PluginId("test.plugin");
            new FilePatchJournal(path).Record(new PatchTransactionRecord(id, owner, PatchTransactionState.Writing, "pending"));
            PatchTransactionRecord? restored = new FilePatchJournal(path).Get(id);
            Assert(restored != null && restored.State == PatchTransactionState.Writing && restored.Error == "pending", "Persistent journals must reload the latest transaction state.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static PatchDefinition CreatePatch(
        Sha256PatchVerifier verifier,
        string id,
        PluginId owner,
        string targetPath,
        byte[] original,
        byte[] replacement)
    {
        return new PatchDefinition(
            new PatchId(id),
            owner,
            targetPath,
            verifier.ComputeSha256(original),
            replacement,
            verifier.ComputeSha256(replacement));
    }

    private static PatchHost CreatePatchHost(
        IPatchFileStore files,
        IPatchVerifier verifier)
    {
        return new PatchHost(files, verifier, new InMemoryPatchJournal());
    }

    private static string HostBackupPath(PatchDefinition definition)
    {
        return ".alacrity-backups/" + definition.Owner.Value + "/" + definition.Id.Value + ".bak";
    }

    private static void PluginMenuPlacesPluginsBeforeWorkshopAndToggles()
    {
        var resources = new PluginResourceScope();
        var manifest = CreateManifest();
        var plugin = new TestPlugin(resources, new List<string>(), false);
        var context = new TestContext(manifest, resources);
        using (var lifecycle = new PluginLifecycleController(plugin, context))
        {
            lifecycle.Validate();
            lifecycle.Initialize();
            lifecycle.Enable();
            var menu = new PluginManagementMenu(new[] { lifecycle });
            Assert(menu.MainMenuEntries[3].Id == MainMenuEntryId.Plugins, "Plugins must occupy the former Workshop slot.");
            Assert(menu.MainMenuEntries[4].Id == MainMenuEntryId.Workshop, "Workshop must shift down one slot.");
            Assert(menu.MainMenuEntries[5].Id == MainMenuEntryId.Settings, "Entries after Workshop must shift down.");
            Assert(menu.SettingsEntries[0].IsEnabled && menu.SettingsEntries[0].CanConfigure, "Enabled plugin row should expose toggle/settings state.");
            Assert(menu.Toggle(manifest.Id) == PluginLifecycleState.Disabled, "Plugin row toggle should disable the plugin.");
            Assert(menu.Toggle(manifest.Id) == PluginLifecycleState.Enabled, "A disabled plugin should reinitialize before the menu enables it again.");
        }
    }

    private static void PluginStorageCreatesNonDestructivePackageLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-storage-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new PluginStorage(root);
            var layout = storage.EnsureLayout(CreateManifest());
            Assert(File.Exists(layout.MetadataPath), "Plugin metadata must be stored in the plugin directory.");
            Assert(File.Exists(layout.ConfigPath), "Plugin configuration must be stored in the plugin directory.");
            Assert(Directory.Exists(layout.DataDirectory), "Plugin data must remain inside the plugin directory.");
            File.WriteAllText(layout.ConfigPath, "{\"kept\":true}");
            storage.EnsureLayout(CreateManifest());
            Assert(File.ReadAllText(layout.ConfigPath) == "{\"kept\":true}", "Existing plugin configuration must not be overwritten.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TrustMetadataRejectsMalformedHash()
    {
        var trust = new PluginTrustMetadata("publisher", "not-a-sha256");
        AssertThrows<InvalidOperationException>(() => trust.Validate());
    }

    private static void LifecycleFailureFaultsAndCleansResources()
    {
        var order = new List<string>();
        var resources = new PluginResourceScope();
        var plugin = new TestPlugin(resources, order, true);
        var context = new TestContext(CreateManifest(), resources);
        using (var lifecycle = new PluginLifecycleController(plugin, context))
        {
            lifecycle.Validate();
            lifecycle.Initialize();
            AssertThrows<InvalidOperationException>(() => lifecycle.Enable());
            Assert(lifecycle.State == PluginLifecycleState.Faulted, "Failed enable should fault the plugin.");
            Assert(order.Count == 2, "Failed enable must release already-owned resources.");
        }
    }

    private static void LifecyclePreservesCallbackFailureAndRecordsCleanupFailure()
    {
        var resources = new PluginResourceScope();
        var plugin = new CleanupFailurePlugin(resources, failDisable: true, failShutdown: false);
        var context = new TestContext(CreateManifest(), resources);
        var lifecycle = new PluginLifecycleController(plugin, context);
        lifecycle.Validate();
        lifecycle.Initialize();
        lifecycle.Enable();

        AssertThrows<InvalidOperationException>(() => lifecycle.Disable());
        Assert(lifecycle.State == PluginLifecycleState.Faulted, "A failed disable must fault the lifecycle.");
        Assert(lifecycle.LastOperation.CallbackFailure != null, "The callback failure must remain visible.");
        Assert(lifecycle.LastOperation.CleanupFailures.Count == 1, "Cleanup failures must be recorded separately.");
        lifecycle.Dispose();
    }

    private static void LifecycleUninstallReachesTerminalStateAfterFailures()
    {
        var resources = new PluginResourceScope();
        var plugin = new CleanupFailurePlugin(resources, failDisable: true, failShutdown: true);
        var context = new TestContext(CreateManifest(), resources);
        var lifecycle = new PluginLifecycleController(plugin, context);
        lifecycle.Validate();
        lifecycle.Initialize();
        lifecycle.Enable();

        AssertThrows<InvalidOperationException>(() => lifecycle.Uninstall());
        Assert(lifecycle.State == PluginLifecycleState.Uninstalled, "Uninstall must reach a terminal state after callback failures.");
        Assert(lifecycle.LastOperation.CleanupFailures.Count >= 1, "Later shutdown failures must be retained as cleanup diagnostics.");
    }

    private static void AsyncLifecycleSupportsMixedActivationCancellationAndTimeout()
    {
        var order = new List<string>();
        using var syncScope = new PluginResourceScope();
        using var asyncScope = new PluginResourceScope();
        var syncManifest = CreateManifest();
        var asyncManifest = new PluginManifest(new PluginId("async.plugin"), "Async", new Version(1, 0), "Tests", "Async test", new[] { "1.4.5.6" }, new[] { new PluginDependency(syncManifest.Id) });
        var sync = new PluginLifecycleController(new OrderedSyncPlugin(order, "sync"), new TestContext(syncManifest, syncScope));
        var asynchronous = new PluginLifecycleController(new OrderedAsyncPlugin(order, "async"), new TestContext(asyncManifest, asyncScope));
        sync.Validate();
        asynchronous.Validate();
        var plan = new PluginEnablePlanner().Plan(asyncManifest.Id, new[] { syncManifest, asyncManifest }, Array.Empty<PluginId>());
        var result = new PluginEnableExecutor().ExecuteAsync(plan, new Dictionary<PluginId, PluginLifecycleController> { [syncManifest.Id] = sync, [asyncManifest.Id] = asynchronous }, CancellationToken.None).GetAwaiter().GetResult();
        Assert(result.Succeeded && order.SequenceEqual(new[] { "sync:init", "sync:enable", "async:init", "async:enable" }), "Async activation must preserve dependency order while synchronous callbacks remain direct.");
        asynchronous.DisableAsync(CancellationToken.None).GetAwaiter().GetResult();
        asynchronous.DisposeAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(order.Contains("async:disable") && order.Contains("async:shutdown"), "Async disable and shutdown callbacks must complete through the shared lifecycle state machine.");

        using var timeoutScope = new PluginResourceScope();
        var timeout = new PluginLifecycleController(new NonCooperativeAsyncPlugin(timeoutScope), new TestContext(asyncManifest, timeoutScope), TimeSpan.FromMilliseconds(20));
        timeout.Validate();
        timeout.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        AssertThrows<TimeoutException>(() => timeout.EnableAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert(timeout.LastOperation.CallbackFailure?.Exception is TimeoutException && timeout.LastOperation.CleanupFailures.Count == 0, "Async timeout must retain the callback failure and release owned registrations.");

        using var cancelledScope = new PluginResourceScope();
        var cancelled = new PluginLifecycleController(new CancellableAsyncPlugin(), new TestContext(asyncManifest, cancelledScope));
        cancelled.Validate();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() => cancelled.InitializeAsync(cancellation.Token).GetAwaiter().GetResult());
        Assert(cancelled.State == PluginLifecycleState.Faulted, "Cancellation during async initialization must fault only that plugin and preserve host startup isolation.");
    }

    private static void AsyncUninstallPropagatesLifecycleFailures()
    {
        using var scope = new PluginResourceScope();
        var manifest = new PluginManifest(new PluginId("async.uninstall"), "Async uninstall", new Version(1, 0), "Tests", "Async uninstall", new[] { "1.4.5.6" });
        var controller = new PluginLifecycleController(new FailingAsyncUninstallPlugin(), new TestContext(manifest, scope));
        controller.Validate();
        controller.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        controller.EnableAsync(CancellationToken.None).GetAwaiter().GetResult();
        AssertThrows<InvalidOperationException>(() => controller.UninstallAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert(controller.State == PluginLifecycleState.Uninstalled && controller.LastOperation.CallbackFailure?.Exception is InvalidOperationException, "Async uninstall must retain lifecycle callback failures while reaching its terminal state.");
    }

    private static void AsyncLifecycleCancelsAfterCallbackStarts()
    {
        using var scope = new PluginResourceScope();
        var manifest = new PluginManifest(new PluginId("async.external-cancel"), "Async cancellation", new Version(1, 0), "Tests", "Async cancellation", new[] { "1.4.5.6" });
        var plugin = new StartedNonCooperativeAsyncPlugin();
        var controller = new PluginLifecycleController(plugin, new TestContext(manifest, scope), TimeSpan.FromSeconds(2));
        controller.Validate();
        controller.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource();
        Task operation = controller.EnableAsync(cancellation.Token);
        Assert(plugin.Started.Wait(TimeSpan.FromSeconds(1)), "The non-cooperative callback must start before external cancellation is tested.");
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() => operation.GetAwaiter().GetResult());
        plugin.Complete();
    }

    private static void AsyncShutdownIsBoundedAndRetainsFailures()
    {
        var manifest = new PluginManifest(new PluginId("async.shutdown"), "Async shutdown", new Version(1, 0), "Tests", "Async shutdown", new[] { "1.4.5.6" });

        using (var scope = new PluginResourceScope())
        {
            var plugin = new ShutdownBlockingAsyncPlugin(scope, false);
            var controller = new PluginLifecycleController(plugin, new TestContext(manifest, scope), TimeSpan.FromMilliseconds(25));
            controller.Validate();
            controller.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            controller.EnableAsync(CancellationToken.None).GetAwaiter().GetResult();

            controller.DisposeAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(plugin.DisableStarted, "Shutdown must begin an enabled async plugin's disable callback.");
            Assert(plugin.ShutdownCalled, "Shutdown must continue after a bounded disable timeout.");
            Assert(scope.IsDisposed && controller.State == PluginLifecycleState.Uninstalled, "A timed-out async shutdown must permanently release its activation scope.");
            plugin.CompleteDisable();
        }

        using (var scope = new PluginResourceScope())
        {
            var plugin = new ShutdownBlockingAsyncPlugin(scope, true);
            var controller = new PluginLifecycleController(plugin, new TestContext(manifest, scope), TimeSpan.FromMilliseconds(100));
            controller.Validate();
            controller.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            controller.EnableAsync(CancellationToken.None).GetAwaiter().GetResult();

            controller.DisposeAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(controller.LastOperation.CallbackFailure?.Exception is InvalidOperationException, "Async shutdown faults must remain attributed to the lifecycle operation.");
            Assert(scope.IsDisposed && controller.State == PluginLifecycleState.Uninstalled, "A faulting shutdown callback must not prevent final cleanup.");
        }
    }

    private static void ResourceScopeReleasesChildrenInParentOrder()
    {
        var order = new List<string>();
        var parent = new PluginResourceScope();
        parent.Own("parent-first", PluginResourceKind.Other, new TestResource("parent-first", order));
        var child = parent.CreateChildScope("feature-a");
        child.Own("child", PluginResourceKind.Other, new TestResource("child", order));
        parent.Own("parent-last", PluginResourceKind.Other, new TestResource("parent-last", order));

        parent.ReleaseAll();
        Assert(order.Count == 3, "Parent cleanup must release all child resources.");
        Assert(order[0] == "parent-last" && order[1] == "child" && order[2] == "parent-first", "Child scopes must follow parent reverse-order cleanup.");
        Assert(child.IsDisposed, "A child scope must be disposed with its parent resource.");
        parent.Dispose();
    }

    private static void ResourceScopeRecordsIndividualCleanupFailures()
    {
        var scope = new PluginResourceScope();
        scope.Own("throwing", PluginResourceKind.Other, new ThrowingResource());
        AssertThrows<InvalidOperationException>(() => scope.ReleaseAll());
        Assert(scope.LastReleaseFailures.Count == 1, "Failed cleanup must retain resource diagnostics.");
        Assert(scope.LastReleaseFailures[0].Name == "throwing", "Cleanup diagnostics must identify the resource.");
        scope.Dispose();
    }

    private static void ActivationTransactionRollsBackInReverseOrder()
    {
        var order = new List<string>();
        var resources = new PluginResourceScope();
        resources.Own("activation-resource", PluginResourceKind.Other, new TestResource("resource", order));
        var transaction = new PluginActivationTransaction(resources);
        transaction.AddStep("first", () => order.Add("first-enable"), () => order.Add("first-rollback"));
        transaction.AddStep("second", () => order.Add("second-enable"), () => order.Add("second-rollback"));
        transaction.AddStep("failure", () => throw new InvalidOperationException("Expected activation failure."), () => order.Add("failure-rollback"));

        var result = transaction.Execute();
        Assert(!result.Succeeded && result.ActivationFailure != null, "A failed activation must preserve its original failure.");
        Assert(order.Count == 5, "Failed activation must roll back completed work and release resources.");
        Assert(order[0] == "first-enable" && order[1] == "second-enable" && order[2] == "second-rollback" && order[3] == "first-rollback" && order[4] == "resource", "Activation rollback must be reverse ordered before scope cleanup.");
        resources.Dispose();
    }

    private static void PatchServiceRequiresPermissionTrustAndPolicy()
    {
        var manifest = new PluginManifest(
            new PluginId("patch.plugin"), "Patch", new Version(1, 0), "Tests", "Patch test", new[] { "1.4.5.6" },
            permissions: PluginPermission.ManagedPatch);
        var factory = new PluginPatchServiceFactory(new PatchHost(new MockPatchFileStore(), new Sha256PatchVerifier(), new InMemoryPatchJournal()), new PluginServiceAccessPolicy());

        Assert(!factory.TryCreate(manifest, PluginTrustLevel.Unverified, true, true, out var denied, out _), "Unverified packages must not receive patch services.");
        Assert(denied == null, "Denied patch service issuance must not expose a capability.");
        Assert(factory.TryCreate(manifest, PluginTrustLevel.LocallyTrusted, true, true, out var granted, out _), "Trusted, permitted packages may receive host-issued patch capabilities.");
        Assert(granted != null && granted.Owner == manifest.Id, "Issued patch capability must be bound to the verified plugin ID.");
    }

    private static void ScopedServicesRespectDependenciesAndCleanup()
    {
        var providerManifest = new PluginManifest(new PluginId("provider.plugin"), "Provider", new Version(1, 0), "Tests", "Provider", new[] { "1.4.5.6" });
        var consumerManifest = new PluginManifest(new PluginId("consumer.plugin"), "Consumer", new Version(1, 0), "Tests", "Consumer", new[] { "1.4.5.6" }, new[] { new PluginDependency(providerManifest.Id) });
        var unrelatedManifest = new PluginManifest(new PluginId("other.plugin"), "Other", new Version(1, 0), "Tests", "Other", new[] { "1.4.5.6" });
        var hub = new PluginServiceHub();
        var providerScope = new PluginResourceScope();
        var provider = hub.CreateRegistry(providerManifest, providerScope);
        provider.Publish<IExampleService>(new ExampleService());
        var consumer = hub.CreateRegistry(consumerManifest, new PluginResourceScope());
        var unrelated = hub.CreateRegistry(unrelatedManifest, new PluginResourceScope());
        Assert(consumer.TryGet<IExampleService>(out var service) && service != null, "Declared dependencies must access published service contracts.");
        Assert(!unrelated.TryGet<IExampleService>(out _), "Undeclared dependencies must not access plugin services.");
        providerScope.ReleaseAll();
        Assert(!consumer.TryGet<IExampleService>(out _), "Disabling the provider scope must remove its services automatically.");
    }

    private static void ExtensionRegistrationsAreScopeOwned()
    {
        var host = new PluginExtensionHost();
        var scope = new PluginResourceScope();
        var manifest = new PluginManifest(
            new PluginId("extensions.plugin"), "Extensions", new Version(1, 0), "Tests", "Extension registration test", new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Input,
            permissions: PluginPermission.DrawUserInterface);
        var services = host.CreateServices(manifest, scope);
        var received = 0;
        services.Events.Subscribe<string>(_ => received++);
        services.Keybinds.Register(new PluginKeybindDescriptor("toggle", "P", "Toggle"), () => { });
        services.Ui.RegisterOverlay(new PluginUiContribution("overlay", "Overlay"));
        services.Ui.RegisterSettingsPage(new PluginUiContribution("settings", "Settings"));
        var activationCount = 0;
        var interactive = new PluginUiContribution("interactive", "Interactive", () => "Enabled", () => activationCount++);
        services.Ui.RegisterSettingsControl(interactive);
        Assert(interactive.IsInteractive, "Interactive settings must retain both host-rendered delegates.");
        interactive.Activate!();
        Assert(activationCount == 1, "Interactive settings must retain their activation action.");
        AssertThrows<ArgumentException>(() => services.Ui.RegisterSettingsControl(new PluginUiContribution("invalid", "Invalid")));
        var enabled = true;
        var typed = PluginSettingControl.Toggle("enabled", "Enabled", () => enabled, value => enabled = value);
        services.Ui.RegisterSettingsControl(typed);
        Assert(host.GetSettingsControls(manifest.Id).Count == 1, "Typed setting controls must be discoverable by their verified plugin identity.");
        Assert(PluginColor.TryParseHex("#12ABef", out var color) && color.ToHex() == "#12ABEF", "Plugin colors must round-trip through canonical hexadecimal text.");
        AssertThrows<ArgumentException>(() => PluginSettingControl.Cycle("invalid-cycle", "Invalid", new[] { "Only" }, () => "Only", _ => { }));
        AssertThrows<InvalidOperationException>(() => services.Keybinds.Register(new PluginKeybindDescriptor("toggle", "O", "Duplicate"), () => { }));
        Assert(host.GetSettingsPages(manifest.Id).Count == 2, "A plugin's active settings contributions must be discoverable by its verified identity.");
        host.Publish("first");
        scope.ReleaseAll();
        host.Publish("second");
        Assert(received == 1, "Event registrations must be removed with their owning scope.");
        Assert(host.GetSettingsPages(manifest.Id).Count == 0, "Disabling a plugin must remove its settings-page registrations with the owning scope.");
        Assert(host.GetSettingsControls(manifest.Id).Count == 0, "Disabling a plugin must remove typed setting controls with the owning scope.");
        scope.Dispose();
    }

    private static void ExtensionServicesRequireOwnersAndIsolateScopes()
    {
        var host = new PluginExtensionHost();
        var invalidScope = new PluginResourceScope();
        AssertThrows<ArgumentException>(() => host.CreateServices(default(PluginId), invalidScope));
        var invalidManifest = new PluginManifest(default, "Invalid", new Version(1, 0), "Tests", "Invalid owner", new[] { "1.4.5.6" });
        AssertThrows<ArgumentException>(() => host.CreateServices(invalidManifest, invalidScope));
        invalidScope.Dispose();

        var firstManifest = new PluginManifest(new PluginId("first.extensions"), "First", new Version(1, 0), "Tests", "First extension owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Input, permissions: PluginPermission.DrawUserInterface);
        var secondManifest = new PluginManifest(new PluginId("second.extensions"), "Second", new Version(1, 0), "Tests", "Second extension owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Input, permissions: PluginPermission.DrawUserInterface);
        var firstScope = new PluginResourceScope();
        var secondScope = new PluginResourceScope();
        var first = host.CreateServices(firstManifest, firstScope);
        var second = host.CreateServices(secondManifest, secondScope);
        var firstEvents = 0;
        var secondEvents = 0;

        first.Events.Subscribe<string>(_ => firstEvents++);
        first.Keybinds.Register(new PluginKeybindDescriptor("first-keybind", "P", "First"), () => { });
        first.Ui.RegisterSettingsPage(new PluginUiContribution("first-page", "First Page"));
        first.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("first-control", "First Control", () => true, _ => { }));
        first.Ui.RegisterOverlay(new PluginUiContribution("first-overlay", "First Overlay"));
        second.Events.Subscribe<string>(_ => secondEvents++);
        second.Keybinds.Register(new PluginKeybindDescriptor("second-keybind", "O", "Second"), () => { });
        second.Ui.RegisterSettingsPage(new PluginUiContribution("second-page", "Second Page"));
        second.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("second-control", "Second Control", () => true, _ => { }));
        second.Ui.RegisterOverlay(new PluginUiContribution("second-overlay", "Second Overlay"));

        Assert(host.GetSettingsPages(firstManifest.Id).Single().Id == "first-page", "Settings pages must be attributed to their owning plugin.");
        Assert(host.GetSettingsControls(firstManifest.Id).Single().Id == "first-control", "Settings controls must be attributed to their owning plugin.");
        Assert(host.GetOverlays(firstManifest.Id).Single().Id == "first-overlay", "Overlays must be attributed to their owning plugin.");
        Assert(host.GetSettingsPages(secondManifest.Id).Single().Id == "second-page", "Other plugin contributions must remain separately owned.");
        host.Publish("before cleanup");
        Assert(firstEvents == 1 && secondEvents == 1, "Each owned event service must receive host events.");

        firstScope.ReleaseAll();
        Assert(host.GetSettingsPages(firstManifest.Id).Count == 0 && host.GetSettingsControls(firstManifest.Id).Count == 0 && host.GetOverlays(firstManifest.Id).Count == 0, "Releasing a plugin scope must remove only that plugin's UI registrations.");
        Assert(host.GetSettingsPages(secondManifest.Id).Count == 1 && host.GetSettingsControls(secondManifest.Id).Count == 1 && host.GetOverlays(secondManifest.Id).Count == 1, "Releasing one plugin scope must preserve other plugins' contributions.");
        host.Publish("after cleanup");
        Assert(firstEvents == 1 && secondEvents == 2, "Releasing one plugin scope must remove only its event registrations.");

        var replacementScope = new PluginResourceScope();
        host.CreateServices(firstManifest, replacementScope).Keybinds.Register(new PluginKeybindDescriptor("first-keybind", "P", "First"), () => { });
        replacementScope.Dispose();
        firstScope.Dispose();
        secondScope.Dispose();
    }

    private static void IconInteractionsAreOwnedAndResolvedByHost()
    {
        var host = new PluginExtensionHost();
        var firstManifest = new PluginManifest(new PluginId("first.icons"), "First", new Version(1, 0), "Tests", "First icon owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        var secondManifest = new PluginManifest(new PluginId("second.icons"), "Second", new Version(1, 0), "Tests", "Second icon owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        using var firstScope = new PluginResourceScope();
        using var secondScope = new PluginResourceScope();
        var firstCalls = 0;
        var secondCalls = 0;
        host.CreateServices(firstManifest, firstScope).Ui.RegisterIconInteraction(
            new PluginIconInteractionDescriptor("action", PluginIconHoverEffect.HighlightAndExpand, 1.2f, new PluginColor(100, 100, 100), new PluginColor(255, 255, 255), new PluginTooltipOptions("First action", PluginTooltipPlacement.Right, new PluginColor(10, 20, 30), 1.1f)),
            () => firstCalls++);
        var tooltipProviderCalls = 0;
        host.CreateServices(secondManifest, secondScope).Ui.RegisterIconInteraction(
            new PluginIconInteractionDescriptor("action", PluginIconHoverEffect.None, 1.15f, null, null, new PluginTooltipOptions("Second action"), () =>
            {
                tooltipProviderCalls++;
                return new PluginTooltipOptions("Second action resolved");
            }),
            () => secondCalls++);

        PluginIconInteractionState hovered = host.EvaluateIconInteraction(firstManifest.Id, "action", new PluginUiRect(10f, 20f, 30f, 40f), 15f, 25f);
        Assert(hovered.IsRegistered && hovered.IsHovered && hovered.Scale == 1.2f && hovered.Color.HasValue && hovered.Color.Value.Equals(new PluginColor(255, 255, 255)), "Hovered icon interactions must resolve their declared visual response.");
        Assert(hovered.Tooltip != null && hovered.Tooltip.Placement == PluginTooltipPlacement.Right && hovered.Tooltip.Color.HasValue && hovered.Tooltip.Color.Value.Equals(new PluginColor(10, 20, 30)), "Hovered icon interactions must retain their tooltip presentation metadata.");
        PluginIconInteractionState outside = host.EvaluateIconInteraction(firstManifest.Id, "action", new PluginUiRect(10f, 20f, 30f, 40f), 0f, 0f);
        Assert(outside.IsRegistered && !outside.IsHovered && outside.Scale == 1f && outside.Color.HasValue && outside.Color.Value.Equals(new PluginColor(100, 100, 100)) && outside.Tooltip == null, "Unhovered icon interactions must retain normal visual state without a tooltip.");
        PluginIconInteractionState dynamicTooltip = host.EvaluateIconInteraction(secondManifest.Id, "action", new PluginUiRect(0f, 0f, 10f, 10f), 5f, 5f);
        Assert(tooltipProviderCalls == 1 && dynamicTooltip.Tooltip != null && dynamicTooltip.Tooltip.Text == "Second action resolved", "Hovered icon interactions must support state-aware tooltip resolution without evaluating it outside hover.");
        Assert(host.TryActivateIconInteraction(firstManifest.Id, "action") && firstCalls == 1 && secondCalls == 0, "Icon actions must dispatch only to their verified owner-local registration.");
        AssertThrows<InvalidOperationException>(() => host.CreateServices(firstManifest, firstScope).Ui.RegisterIconInteraction(new PluginIconInteractionDescriptor("action"), () => { }));

        firstScope.ReleaseAll();
        Assert(!host.EvaluateIconInteraction(firstManifest.Id, "action", new PluginUiRect(0f, 0f, 1f, 1f), 0f, 0f).IsRegistered && !host.TryActivateIconInteraction(firstManifest.Id, "action"), "Releasing a plugin scope must remove only its icon interactions.");
        Assert(host.TryActivateIconInteraction(secondManifest.Id, "action") && secondCalls == 1, "Other plugins' icon interactions must survive unrelated cleanup.");
    }

    private static void HudWidgetsAreOwnedAndIsolated()
    {
        var host = new PluginHudHost();
        var first = new PluginManifest(new PluginId("first.hud"), "First HUD", new Version(1, 0), "Tests", "First HUD", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        var second = new PluginManifest(new PluginId("second.hud"), "Second HUD", new Version(1, 0), "Tests", "Second HUD", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        using var firstScope = new PluginResourceScope();
        using var secondScope = new PluginResourceScope();
        var order = new List<string>();
        host.CreateService(first, firstScope).Register(new PluginHudWidgetDescriptor("first", 10), (_, __) => order.Add("first"));
        host.CreateService(second, secondScope).Register(new PluginHudWidgetDescriptor("second", 5), (_, __) => order.Add("second"));
        host.Dispatch(new TestHudRenderer(), new PluginHudFrame(800, 600, 1f, TimeSpan.Zero, 1));
        Assert(order.SequenceEqual(new[] { "second", "first" }), "HUD widgets must dispatch in deterministic descriptor order.");
        firstScope.ReleaseAll();
        order.Clear();
        host.Dispatch(new TestHudRenderer(), new PluginHudFrame(800, 600, 1f, TimeSpan.Zero, 2));
        Assert(order.SequenceEqual(new[] { "second" }), "Releasing one plugin scope must remove only its HUD widgets.");
    }

    private static void KeybindsAreOwnedQualifiedAndScopeReleased()
    {
        var host = new PluginExtensionHost();
        var firstManifest = new PluginManifest(new PluginId("first.keybinds"), "First Controls", new Version(1, 0), "Tests", "First keybind owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.Input);
        var secondManifest = new PluginManifest(new PluginId("second.keybinds"), "Second Controls", new Version(1, 0), "Tests", "Second keybind owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.Input);
        var firstScope = new PluginResourceScope();
        var secondScope = new PluginResourceScope();
        var firstInvocations = 0;
        var secondInvocations = 0;

        host.CreateServices(firstManifest, firstScope).Keybinds.Register(new PluginKeybindDescriptor("toggle", "T", "Toggle First"), () => firstInvocations++);
        host.CreateServices(secondManifest, secondScope).Keybinds.Register(new PluginKeybindDescriptor("toggle", "Y", "Toggle Second"), () => secondInvocations++);

        var rows = host.GetKeybinds();
        Assert(rows.Count == 2, "Different plugins may reuse a local keybind ID without sharing registrations.");
        Assert(rows[0].HostId == "first.keybinds.toggle" && rows[0].Heading == "First Controls", "The native controls adapter must receive the verified plugin heading and qualified keybind ID.");
        Assert(rows[1].HostId == "second.keybinds.toggle" && rows[1].Heading == "Second Controls", "Each plugin's controls heading must remain independently owned.");
        Assert(host.TryInvokeKeybind("first.keybinds.toggle", out var failure) && failure == null && firstInvocations == 1 && secondInvocations == 0, "A host keybind dispatch must invoke only its owning plugin handler.");
        Assert(!host.TryInvokeKeybind("missing.plugin.toggle", out failure) && failure == null, "Unknown host keybind IDs must be ignored without throwing.");

        var heldStates = new List<bool>();
        host.CreateServices(firstManifest, firstScope).Keybinds.Register(new PluginKeybindDescriptor("held", "U", "Held", PluginKeybindActivation.Hold), isDown => heldStates.Add(isDown));
        Assert(host.TrySetKeybindState("first.keybinds.held", true, out failure) && failure == null, "Held keybinds must receive their press transition.");
        Assert(host.TrySetKeybindState("first.keybinds.held", false, out failure) && failure == null && heldStates.SequenceEqual(new[] { true, false }), "Held keybinds must receive a matching release transition.");

        firstScope.ReleaseAll();
        Assert(host.GetKeybinds().Count == 1 && !host.TryInvokeKeybind("first.keybinds.toggle", out failure), "Releasing a plugin scope must remove only that plugin's keybind registrations.");
        Assert(host.TryInvokeKeybind("second.keybinds.toggle", out failure) && failure == null && secondInvocations == 1, "Other plugins' keybind registrations must survive unrelated cleanup.");

        firstScope.Dispose();
        secondScope.Dispose();
    }

    private static void KeybindDescriptorsValidateActivationAndSnapshotsRemainOwned()
    {
        var descriptor = new PluginKeybindDescriptor("legacy", "T", "Legacy binding");
        Assert(descriptor.Activation == PluginKeybindActivation.Press, "The three-argument keybind constructor must preserve press activation compatibility.");
        AssertThrows<ArgumentOutOfRangeException>(() => new PluginKeybindDescriptor("invalid", "T", "Invalid", (PluginKeybindActivation)99));

        var host = new PluginExtensionHost();
        var manifest = new PluginManifest(new PluginId("ordered.keybinds"), "Ordered", new Version(1, 0), "Tests", "Ordered bindings", new[] { "1.4.5.6" }, capabilities: PluginCapability.Input);
        using var firstScope = new PluginResourceScope();
        host.CreateServices(manifest, firstScope).Keybinds.Register(new PluginKeybindDescriptor("first", "T", "First"), () => { });
        PluginKeybindRegistrySnapshot first = host.GetKeybindSnapshot();
        firstScope.ReleaseAll();
        using var secondScope = new PluginResourceScope();
        host.CreateServices(manifest, secondScope).Keybinds.Register(new PluginKeybindDescriptor("second", "Y", "Second"), () => { });
        PluginKeybindRegistrySnapshot second = host.GetKeybindSnapshot();
        Assert(second.Version > first.Version && second.Registrations.Count == 1 && second.Registrations[0].RegistrationSequence > first.Registrations[0].RegistrationSequence, "Keybind snapshots must remain atomic and registration ordering must never be reused after cleanup.");
    }

    private static void OwnerQualifiedHostServiceLookupRejectsWrongPublisher()
    {
        var hub = new PluginServiceHub();
        var owner = new PluginManifest(new PluginId("player-list.owner"), "Player List", new Version(1, 0), "Tests", "Owner", new[] { "1.4.5.6" });
        var other = new PluginManifest(new PluginId("other.owner"), "Other", new Version(1, 0), "Tests", "Other", new[] { "1.4.5.6" });
        using var scope = new PluginResourceScope();
        hub.CreateRegistry(owner, scope).Publish<IExampleService>(new ExampleService());
        Assert(hub.TryGetHostService<IExampleService>(owner.Id, out var owned) && owned != null, "Owner-qualified host lookup must expose the verified publisher's service.");
        Assert(!hub.TryGetHostService<IExampleService>(other.Id, out _), "Owner-qualified host lookup must reject a service from another plugin.");
    }

    private static void ChatVisibilityFiltersAreScopeOwned()
    {
        var host = new PluginChatHost();
        var firstManifest = new PluginManifest(new PluginId("first.chat"), "First", new Version(1, 0), "Tests", "Chat filter", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        var secondManifest = new PluginManifest(new PluginId("second.chat"), "Second", new Version(1, 0), "Tests", "Chat filter", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        var firstScope = new PluginResourceScope();
        var secondScope = new PluginResourceScope();
        host.CreateService(firstManifest, firstScope).RegisterMessageFilter(new ChatMessageFilterDescriptor("hide-players"), new TestChatFilter(ChatMessageOrigin.Player));
        host.CreateService(secondManifest, secondScope).RegisterMessageFilter(new ChatMessageFilterDescriptor("hide-local"), new TestChatFilter(ChatMessageOrigin.LocalSystem));
        Assert(!host.ShouldDisplay(ChatMessageOrigin.Player), "A scoped chat filter must receive the host-classified player origin.");
        Assert(!host.ShouldDisplay(ChatMessageOrigin.LocalSystem), "A second plugin filter must independently receive local-system messages.");
        Assert(host.ShouldDisplay(ChatMessageOrigin.Server), "Unfiltered server messages must remain visible.");
        firstScope.ReleaseAll();
        Assert(host.ShouldDisplay(ChatMessageOrigin.Player), "Removing one plugin scope must remove only its chat filter.");
        Assert(!host.ShouldDisplay(ChatMessageOrigin.LocalSystem), "Remaining plugin filters must stay registered.");
        firstScope.Dispose();
        secondScope.Dispose();
    }

    private static void ChatOwnershipCompositionAndPermissionEnforcement()
    {
        var host = new PluginChatHost();
        var firstManifest = new PluginManifest(new PluginId("first.chat-owner"), "First", new Version(1, 0), "Tests", "First chat owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Input, permissions: PluginPermission.DrawUserInterface | PluginPermission.OpenExternalLinks);
        var secondManifest = new PluginManifest(new PluginId("second.chat-owner"), "Second", new Version(1, 0), "Tests", "Second chat owner", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Input, permissions: PluginPermission.DrawUserInterface | PluginPermission.OpenExternalLinks);
        using var firstScope = new PluginResourceScope();
        using var secondScope = new PluginResourceScope();
        var interactionHost = new PluginUserInteractionHost(UnsupportedPluginUserInteractionBackend.Instance);
        IPluginUserInteractionService firstInteraction = interactionHost.CreateService(firstManifest, firstScope);
        IPluginUserInteractionService secondInteraction = interactionHost.CreateService(secondManifest, secondScope);
        var first = host.CreateService(firstManifest, firstScope, firstInteraction);
        var second = host.CreateService(secondManifest, secondScope, secondInteraction);
        first.RegisterInputEditor(new ChatInputEditorDescriptor("first-editor"), new TestInputEditor());
        second.RegisterInputEditor(new ChatInputEditorDescriptor("second-editor"), new TestInputEditor());
        Assert(host.HasInputEditor(firstManifest.Id) && host.HasInputEditor(secondManifest.Id), "Chat editor ownership must remain attributable to the registered plugin.");
        Assert(host.TryGetActiveEditorInteraction(out IPluginUserInteractionService? activeInteraction) && ReferenceEquals(firstInteraction, activeInteraction), "The active editor must expose its owning activation-scoped interaction capability.");
        first.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("first-decoration", priority: 1), new TestDecorator("first"));
        second.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("second-decoration", priority: 2), new AppendingDecorator("-second"));
        Assert(host.Decorate(new ChatMessageSnapshot("original")).Single().Text == "first-second", "Later chat decorators must receive the current decorated output in deterministic priority order.");
        Assert(host.TryGetInteraction(firstManifest.Id, out IPluginUserInteractionService? decoratorInteraction) && ReferenceEquals(firstInteraction, decoratorInteraction), "A decorator-only presentation span must retain its own interaction capability.");
        firstScope.ReleaseAll();
        Assert(!host.HasInputEditor(firstManifest.Id) && host.HasInputEditor(secondManifest.Id), "Disabling one scope must remove only its chat editor.");
        Assert(host.TryGetActiveEditorInteraction(out activeInteraction) && ReferenceEquals(secondInteraction, activeInteraction), "After owner cleanup, interaction dispatch must follow the remaining active editor rather than a stale owner cache.");
        Assert(host.Decorate(new ChatMessageSnapshot("original")).Single().Text == "original-second", "The next deterministic decorator must remain active after the first owner is removed.");

        var isolatedHost = new PluginChatHost();
        using var isolatedScope = new PluginResourceScope();
        var isolated = isolatedHost.CreateService(secondManifest, isolatedScope);
        isolated.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("throwing-decoration", priority: 1), new ThrowingDecorator());
        isolated.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("fallback-decoration", priority: 2), new TestDecorator("fallback"));
        Assert(isolatedHost.Decorate(new ChatMessageSnapshot("original")).Single().Text == "fallback", "A failed decorator must be removed and must not prevent later chat decorators from preserving vanilla chat output.");

        var denied = host.CreateService(new PluginManifest(new PluginId("denied.chat"), "Denied", new Version(1, 0), "Tests", "Denied chat", new[] { "1.4.5.6" }), new PluginResourceScope());
        AssertThrows<UnauthorizedAccessException>(() => denied.RegisterInputEditor(new ChatInputEditorDescriptor("denied-editor"), new TestInputEditor()));
        AssertThrows<UnauthorizedAccessException>(() => denied.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("denied-decoration"), new TestDecorator("denied")));

        using var extensionScope = new PluginResourceScope();
        var extension = new PluginExtensionHost().CreateServices(new PluginManifest(new PluginId("denied.extension"), "Denied extension", new Version(1, 0), "Tests", "Denied extension", new[] { "1.4.5.6" }), extensionScope);
        AssertThrows<UnauthorizedAccessException>(() => extension.Ui.RegisterSettingsPage(new PluginUiContribution("denied-page", "Denied")));
        AssertThrows<UnauthorizedAccessException>(() => extension.Keybinds.Register(new PluginKeybindDescriptor("denied-key", "P", "Denied"), () => { }));
        using var overlayScope = new PluginResourceScope();
        var overlay = new PluginOverlayHost().CreateService(new PluginManifest(new PluginId("denied.overlay"), "Denied overlay", new Version(1, 0), "Tests", "Denied overlay", new[] { "1.4.5.6" }), overlayScope);
        AssertThrows<UnauthorizedAccessException>(() => overlay.Register(new PluginOverlayDescriptor("denied-overlay"), (_, _) => { }));
    }

    private static void PluginSettingsAvoidNoOpPersistenceAndExposeTypedOldValue()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-settings-events-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new PluginSettingsStore(root, new PluginId("settings.events"));
            int changed = 0;
            PluginSettingChangedEventArgs? last = null;
            settings.Changed += (_, value) => { changed++; last = value; };
            settings.Set("enabled", true);
            settings.Set("enabled", true);
            settings.Set("enabled", false);
            Assert(changed == 2, "Writing a serialized setting value that has not changed must not persist or raise a duplicate change event.");
            Assert(last != null && last.OldValue is bool oldValue && oldValue, "Settings change events must expose the previous deserialized typed value.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TypedSettingsResetRestoresRegisteredDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-settings-reset-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new PluginSettingsStore(root, new PluginId("settings.reset"), 3, null);
            IPluginSetting<int> volume = settings.Register(new PluginSettingDefinition<int>("volume", 42, value => Math.Max(0, Math.Min(100, value))));
            IPluginSetting<bool> enabled = settings.Register(new PluginSettingDefinition<bool>("enabled", true));
            int changes = 0;
            volume.Subscribe(_ => changes++);
            enabled.Subscribe(_ => changes++);
            volume.Value = 7; enabled.Value = false;
            settings.ResetToDefaults();
            Assert(volume.Value == 42 && enabled.Value && settings.SchemaVersion == 3, "ResetToDefaults must restore active typed settings while preserving schema metadata.");
            Assert(changes == 4, "ResetToDefaults must notify only settings whose persisted values actually changed.");
            settings.ResetToDefaults();
            Assert(changes == 4, "Resetting values already at their defaults must not duplicate notifications.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void BetterChatUrlDecorationHandlesBalancedAndTrailingPunctuation()
    {
        IReadOnlyList<ChatTextSpan> balanced = BetterChatUrlParser.Decorate("Read https://example.invalid/wiki_(test). now");
        Assert(balanced.Any(span => span.LinkTarget == "https://example.invalid/wiki_(test)"), "Balanced closing parentheses must remain part of a URL.");
        IReadOnlyList<ChatTextSpan> trailing = BetterChatUrlParser.Decorate("https://example.invalid/path))).");
        Assert(trailing[0].LinkTarget == "https://example.invalid/path" && trailing.Skip(1).Any(span => span.Text == ")))."), "All unmatched trailing closing parentheses and punctuation must be separate ordinary text.");
        IReadOnlyList<ChatTextSpan> multiple = BetterChatUrlParser.Decorate("www.example.invalid/a, and https://second.invalid/b!");
        Assert(multiple.Count(span => span.LinkTarget != null) == 2, "Multiple URLs in one message must decorate independently.");
        IReadOnlyList<ChatTextSpan> invalid = BetterChatUrlParser.Decorate("https:// not-a-link");
        Assert(invalid.All(span => span.LinkTarget == null), "Malformed URLs must remain ordinary text.");
    }

    private static void BetterChatCachesDefaultsWithoutRewritingSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-better-chat-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = new PluginManifest(new PluginId("alacrity.better-chat"), "Better Chat", new Version(1, 0), "Tests", "Better chat test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Input, permissions: PluginPermission.DrawUserInterface | PluginPermission.Clipboard | PluginPermission.OpenExternalLinks);
            var factory = new PluginHostContextFactory(root, new PluginServiceHub(), new PluginExtensionHost(), new PluginCommandHost(), chat: new PluginChatHost());
            PluginHostContext context = factory.Create(manifest, new TestLogger(), new TestMultiplayerSession());
            new BetterChatPlugin().Initialize(context);
            Assert(!File.Exists(Path.Combine(root, "data", "plugins", manifest.Id.Value, "settings.json")), "BetterChat initialization must cache missing defaults without rewriting settings.json.");
            context.Resources.Dispose();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void BetterChatMigratesLegacyVisibilityToToggle()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-better-chat-visibility-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = new PluginManifest(new PluginId("alacrity.better-chat"), "Better Chat", new Version(1, 0), "Tests", "Better chat test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Input, permissions: PluginPermission.DrawUserInterface | PluginPermission.Clipboard | PluginPermission.OpenExternalLinks);
            var extensions = new PluginExtensionHost();
            var factory = new PluginHostContextFactory(root, new PluginServiceHub(), extensions, new PluginCommandHost(), chat: new PluginChatHost());
            PluginHostContext context = factory.Create(manifest, new TestLogger(), new TestMultiplayerSession());
            context.Settings.Set("visibility", "Disabled");
            new BetterChatPlugin().Initialize(context);
            PluginSettingControl control = extensions.GetSettingsControls(manifest.Id).Single(item => item.Id == "chat-visibility");
            Assert(control.Kind == PluginSettingControlKind.Toggle && control.GetToggle != null && !control.GetToggle(), "Legacy visibility must migrate to the Chat Visibility toggle.");
            Assert(context.Settings.Get<bool?>("chat-visibility", null) == false && context.Settings.Get<string?>("visibility", null) == null, "Legacy visibility must be removed after a one-time migration.");
            context.Resources.Dispose();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void PlayerListPublishesPresentationSettingsAndDefaults()
    {
        var manifest = new PluginManifest(new PluginId("alacrity.player-list"), "Player List", new Version(1, 0), "Tests", "Displays the currently online players", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Input | PluginCapability.GameStateRead | PluginCapability.MultiplayerObservation, permissions: PluginPermission.DrawUserInterface | PluginPermission.ReadGameState | PluginPermission.ObserveMultiplayer);
        string root = Path.Combine(Path.GetTempPath(), "alacrity-player-list-" + Guid.NewGuid().ToString("N"));
        try
        {
            var extensions = new PluginExtensionHost();
            var services = new PluginServiceHub();
            PluginHostContext context = new PluginHostContextFactory(root, services, extensions, new PluginCommandHost()).Create(manifest, new TestLogger(), new TestMultiplayerSession());
            var plugin = new PlayerListPlugin();
            plugin.Initialize(context);
            Assert(plugin.PlayersPerColumn == 14 && plugin.RowWidth == 260 && Math.Abs(plugin.TextScale - 1.2f) < 0.001f && plugin.ShowPlayerHeads && plugin.ShowPing, "Player List must retain its documented default presentation settings.");
            Assert(services.TryGetHostService<IPlayerListService>(manifest.Id, out var service) && ReferenceEquals(plugin, service), "Player List must publish its stable provider contract for dependent plugins.");
            plugin.ToggleVisibility();
            Assert(plugin.IsVisible, "The Display Player List binding must toggle local presentation visibility.");
            Assert(extensions.TryActivateIconInteraction(manifest.Id, "sort") && plugin.SortMode == PlayerListSortMode.Team, "The registered sort icon must dispatch to the Player List plugin through the host.");
            Assert(extensions.TryActivateIconInteraction(manifest.Id, "bot-filter") && plugin.HideBots, "The registered bot-filter icon must dispatch to the Player List plugin through the host.");
            plugin.Disable();
            Assert(!plugin.IsVisible, "Disabling Player List must immediately remove its visible presentation state.");
            context.Resources.Dispose();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void DustGoreTogglePublishesScopedPolicyAndManagesExceptions()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-dust-gore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = new PluginManifest(new PluginId("alacrity.dust-gore-toggle"), "Dust & Gore Toggle", new Version(1, 0), "Tests", "Visual effects test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Rendering | PluginCapability.GameStateRead, permissions: PluginPermission.DrawUserInterface | PluginPermission.ReadGameState);
            var services = new PluginServiceHub();
            var commands = new PluginCommandHost();
            var visualEffects = new PluginVisualEffectsHost();
            var context = new PluginHostContextFactory(root, services, new PluginExtensionHost(), commands, visualEffects: visualEffects).Create(manifest, new TestLogger(), new TestMultiplayerSession());
            var plugin = new DustGoreTogglePlugin();
            plugin.Initialize(context);

            Assert(visualEffects.GetEffectivePolicy().DustEnabled && visualEffects.GetEffectivePolicy().GoreEnabled, "Dust & Gore Toggle must register the default host-owned visual-effects policy.");

            var replies = new List<string>();
            Assert(commands.TryInvoke("de", new[] { "42" }, replies.Add), "The /de command must be registered while the plugin is active.");
            PluginVisualEffectsPolicy policy = visualEffects.GetEffectivePolicy();
            Assert(policy.DustExceptionIds.Count == 1 && policy.DustExceptionIds[0] == 42 && replies.Single().Contains("added", StringComparison.Ordinal), "The /de command must add a bounded Dust ID exception.");

            context.Resources.Dispose();
            Assert(visualEffects.GetEffectivePolicy().DustEnabled && visualEffects.GetEffectivePolicy().DustExceptionIds.Count == 0, "Disposing Dust & Gore Toggle's scope must restore the vanilla visual-effects policy.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void HitboxesPublishesScopedPresentationPolicy()
    {
        var manifest = new PluginManifest(new PluginId("alacrity.hitboxes"), "Hitboxes", new Version(1, 0), "Tests", "Hitbox diagnostics test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Rendering | PluginCapability.GameStateRead | PluginCapability.MultiplayerObservation, permissions: PluginPermission.DrawUserInterface | PluginPermission.ReadGameState | PluginPermission.ObserveMultiplayer);
        using var scope = new PluginResourceScope();
        var context = new TestContext(manifest, scope);
        var plugin = new HitboxesPlugin();
        plugin.Initialize(context);

        var overlays = context.Overlays as TestOverlays;
        Assert(overlays != null && overlays.Registrations == 1, "Hitboxes must own one generic world-overlay registration.");

        scope.Dispose();
        Assert(overlays != null, "Hitbox overlay registration must be retained through scope cleanup.");
    }

    private static void UserInteractionServicesRequirePermissionsAndValidateLinks()
    {
        var backend = new RecordingUserInteractionBackend();
        var host = new PluginUserInteractionHost(backend);
        var denied = host.CreateService(CreateManifest());
        Assert(!denied.TryWriteClipboard("blocked") && !denied.TryReadClipboard(out _) && !denied.TryOpenExternalLink(new Uri("https://example.invalid")), "Host interaction services must deny every operation not declared in the verified manifest.");
        Assert(backend.Calls == 0, "Denied interaction requests must not reach the platform backend.");

        var permitted = host.CreateService(new PluginManifest(new PluginId("interaction.plugin"), "Interaction", new Version(1, 0), "Tests", "Interaction test", new[] { "1.4.5.6" }, permissions: PluginPermission.Clipboard | PluginPermission.OpenExternalLinks));
        Assert(permitted.TryWriteClipboard("copied") && permitted.TryReadClipboard(out var copied) && copied == "copied", "Declared clipboard permission must enable the host-owned clipboard backend.");
        Assert(permitted.TryOpenExternalLink(new Uri("https://example.invalid/path")), "Declared external-link permission must allow validated HTTPS links.");
        Assert(!permitted.TryOpenExternalLink(new Uri("file:///C:/not-allowed")), "The host must reject non-HTTP(S) links before invoking the backend.");
        Assert(backend.LastOpened == "https://example.invalid/path", "Only the validated URI should reach the platform backend.");
    }

    private static void PluginDataAndSettingsStayIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "alacrity-data-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plugin = new PluginId("storage.plugin");
            var settings = new PluginSettingsStore(root, plugin);
            settings.Set("enabled", true);
            Assert(new PluginSettingsStore(root, plugin).Get("enabled", false), "Settings must persist beneath the separate plugin data root.");
            var data = new PluginDataStore(root, plugin);
            using (var stream = new StreamWriter(data.Create("state/value.txt"))) stream.Write("kept");
            Assert(data.Exists("state/value.txt"), "Plugin data must be readable from its confined root.");
            AssertThrows<UnauthorizedAccessException>(() => data.Create("..\\other.txt"));
            Assert(File.Exists(Path.Combine(root, "data", "plugins", plugin.Value, "settings.json")), "Settings must not be written to the package directory.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void EnablePlannerAutoEnablesDependencies()
    {
        var dependency = new PluginManifest(new PluginId("dependency.plugin"), "Dependency", new Version(1, 0), "Tests", "Dependency", new[] { "1.4.5.6" });
        var requested = new PluginManifest(new PluginId("requested.plugin"), "Requested", new Version(1, 0), "Tests", "Requested", new[] { "1.4.5.6" }, new[] { new PluginDependency(dependency.Id) });
        var plan = new PluginEnablePlanner().Plan(requested.Id, new[] { requested, dependency }, Array.Empty<PluginId>());
        Assert(plan.OrderedPlugins.Count == 2 && plan.OrderedPlugins[0] == dependency.Id && plan.OrderedPlugins[1] == requested.Id, "Dependencies must be enabled before the requested plugin.");
        Assert(plan.Notifications.Count == 1 && plan.Notifications[0].Dependency == dependency.Id, "Auto-enabled dependencies must produce one transient notification.");
    }

    private static void DependencyWarningsClearWhenResolved()
    {
        var dependency = new PluginManifest(new PluginId("dependency.plugin"), "Dependency", new Version(1, 0), "Tests", "Dependency", new[] { "1.4.5.6" });
        var requested = new PluginManifest(new PluginId("requested.plugin"), "Requested", new Version(1, 0), "Tests", "Requested", new[] { "1.4.5.6" }, new[] { new PluginDependency(dependency.Id) });
        var diagnostics = new PluginDependencyDiagnostics();
        diagnostics.Refresh(new[] { requested, dependency }, new[] { requested.Id });
        Assert(diagnostics.ActiveWarnings.Count == 1, "An enabled plugin with a disabled dependency must show one warning.");
        diagnostics.Refresh(new[] { requested, dependency }, new[] { requested.Id, dependency.Id });
        Assert(diagnostics.ActiveWarnings.Count == 0, "Warnings must disappear as soon as the dependency is enabled.");
        diagnostics.Refresh(new[] { requested, dependency }, Array.Empty<PluginId>());
        Assert(diagnostics.ActiveWarnings.Count == 0, "Warnings must disappear when the dependent plugin is disabled.");
    }

    private static void UiRegistrationsRejectDuplicateOwnerLocalIds()
    {
        var manifest = new PluginManifest(new PluginId("ui.duplicate"), "UI Duplicate", new Version(1, 0), "Tests", "UI duplicate test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        using var scope = new PluginResourceScope();
        var extensions = new PluginExtensionHost();
        PluginExtensionHost.PluginExtensionServices services = extensions.CreateServices(manifest, scope);
        services.Ui.RegisterSettingsPage(new PluginUiContribution("settings", "Settings"));
        AssertThrows<InvalidOperationException>(() => services.Ui.RegisterSettingsPage(new PluginUiContribution("settings", "Duplicate Settings")));
        services.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("visible", "Visible", () => true, _ => { }));
        AssertThrows<InvalidOperationException>(() => services.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("visible", "Duplicate Visible", () => true, _ => { })));
        Assert(extensions.GetSettingsPages(manifest.Id).Count == 1 && extensions.GetSettingsControls(manifest.Id).Count == 1, "Duplicate UI registration rejection must preserve the original registrations.");
    }

    private static void SettingsControlsAreParentedAndLegacyControlsRemainCompatible()
    {
        var manifest = new PluginManifest(new PluginId("ui.pages"), "UI Pages", new Version(1, 0), "Tests", "UI page hierarchy test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        using var scope = new PluginResourceScope();
        var extensions = new PluginExtensionHost();
        PluginExtensionHost.PluginExtensionServices services = extensions.CreateServices(manifest, scope);
        services.Ui.RegisterSettingsPage(new PluginUiContribution("first", "First"));
        services.Ui.RegisterSettingsPage(new PluginUiContribution("second", "Second"));
        services.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("first-control", "First Control", () => true, _ => { }).InPage("first"));
        Assert(extensions.GetSettingsControls(manifest.Id, "first").Count == 1, "A typed setting control must remain associated with its declared page.");
        Assert(extensions.GetSettingsControls(manifest.Id, "second").Count == 0, "Controls must not leak into another page owned by the same plugin.");
        services.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("unparented", "Unparented", () => true, _ => { }));
        Assert(extensions.GetSettingsControls(manifest.Id, "first").Count == 2, "Legacy unqualified controls must stay available through a deterministic first-page compatibility attachment.");

        using var legacyScope = new PluginResourceScope();
        var legacyServices = extensions.CreateServices(new PluginManifest(new PluginId("ui.legacy"), "Legacy UI", new Version(1, 0), "Tests", "Legacy UI test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface), legacyScope);
        legacyServices.Ui.RegisterSettingsPage(new PluginUiContribution("only-page", "Only Page"));
        legacyServices.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("legacy-control", "Legacy Control", () => true, _ => { }));
        Assert(extensions.GetSettingsControls(new PluginId("ui.legacy"), "only-page").Count == 1, "Single-page legacy controls must be attached to their only page without breaking existing plugins.");
    }

    private static void TypedSettingsNormalizeAndReleaseSubscriptions()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-typed-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            PluginManifest manifest = CreateManifest();
            PluginHostContext context = new PluginHostContextFactory(root, new PluginServiceHub(), new PluginExtensionHost(), new PluginCommandHost())
                .Create(manifest, new TestLogger(), new TestMultiplayerSession());
            IPluginSetting<int> setting = context.Settings.Register(new PluginSettingDefinition<int>("bounded", 4, value => Math.Max(0, Math.Min(10, value))));
            int notifications = 0;
            setting.Subscribe(value => notifications += value);

            setting.Value = 18;
            Assert(setting.Value == 10 && notifications == 10, "Typed settings must normalize before persistence and notify subscribed plugin code.");

            context.Resources.Dispose();
            AssertThrows<ObjectDisposedException>(() => setting.Value = 3);
            Assert(notifications == 10, "Releasing a plugin scope must remove typed-setting subscriptions and reject stale setting handles.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void NotificationsExpireWithoutPersistence()
    {
        var notifications = new PluginNotificationCenter();
        notifications.Publish("Dependency enabled.", TimeSpan.FromSeconds(2));
        Assert(notifications.GetActive(DateTimeOffset.UtcNow).Count == 1, "A fresh notification must be available to the presentation layer.");
        Assert(notifications.GetActive(DateTimeOffset.UtcNow.AddSeconds(3)).Count == 0, "Expired notifications must be removed instead of accumulating.");

        PluginManifest manifest = CreateManifest();
        using (var notificationScope = new PluginResourceScope())
        {
            notifications.CreateService(manifest, notificationScope).Show("Ready", new PluginNotificationOptions(PluginNotificationTarget.PluginManager, new PluginColor(10, 20, 30), TimeSpan.FromSeconds(30)));
            PluginNotification owned = notifications.GetActive(DateTimeOffset.UtcNow).Single();
            Assert(owned.Owner == manifest.Id && owned.Message == "Ready" && owned.Options.Target == PluginNotificationTarget.PluginManager && owned.Options.Color.HasValue && owned.Options.Color.Value.Equals(new PluginColor(10, 20, 30)) && owned.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(16), "Plugin notifications must be owner-attributed, targetable, colored, and lifetime-bounded.");
        }
        Assert(notifications.GetActive(DateTimeOffset.UtcNow).Count == 0, "Releasing a plugin scope must remove that plugin's pending notifications.");
    }

    private static void NotificationServicesRejectReleasedScopesAndRateLimit()
    {
        var center = new PluginNotificationCenter();
        PluginManifest manifest = CreateManifest();
        var scope = new PluginResourceScope();
        IPluginNotificationService service = center.CreateService(manifest, scope);
        for (int index = 0; index < 20; index++) service.Show("message-" + index);
        Assert(center.GetActive(DateTimeOffset.UtcNow).Count <= 3, "Per-plugin notification limits and publication throttling must bound active notifications.");
        scope.ReleaseAll();
        AssertThrows<ObjectDisposedException>(() => service.Show("stale"));
        scope.Dispose();
    }

    private static void DispatcherHonorsFrameBudget()
    {
        var dispatcher = new PluginDispatcherHost(1, TimeSpan.FromSeconds(1));
        var scope = new PluginResourceScope();
        PluginManifest manifest = CreateManifest();
        IPluginDispatcher service = dispatcher.CreateService(manifest, scope);
        int calls = 0;
        service.Post(() => calls++); service.Post(() => calls++);
        dispatcher.Drain();
        Assert(calls == 1, "Dispatcher must retain excess work for the next update after its callback budget is consumed.");
        dispatcher.Drain();
        Assert(calls == 2, "Dispatcher must drain retained work on a later update.");
        scope.Dispose();
    }

    private static void DispatcherRetainsPhysicalQueueSlotsAfterCancellation()
    {
        var dispatcher = new PluginDispatcherHost(8, TimeSpan.FromSeconds(1), maximumQueuedWork: 1, maximumQueuedWorkPerPlugin: 1);
        using var scope = new PluginResourceScope();
        IPluginDispatcher service = dispatcher.CreateService(CreateManifest(), scope);
        IPluginRegistration cancelled = service.Post(() => { });
        cancelled.Dispose();
        AssertThrows<InvalidOperationException>(() => service.Post(() => { }));
        dispatcher.Drain();
        service.Post(() => { }).Dispose();
    }

    private static void SchedulerUsesDispatcherAndActivationCleanup()
    {
        var dispatcher = new PluginDispatcherHost(16, TimeSpan.FromSeconds(1));
        var scheduler = new PluginSchedulerHost();
        using var scope = new PluginResourceScope();
        PluginManifest manifest = CreateManifest();
        IPluginDispatcher dispatch = dispatcher.CreateService(manifest, scope);
        IPluginScheduler service = scheduler.CreateService(manifest, scope, dispatch, new TestLogger());
        int delayed = 0;
        int repeating = 0;
        service.AfterUpdates("delayed", 2, () => delayed++);
        service.EveryUpdates("repeat", 1, () => repeating++);

        scheduler.Tick(1); dispatcher.Drain();
        Assert(delayed == 0 && repeating == 1, "Scheduler must dispatch due update work through the bounded dispatcher.");
        scheduler.Tick(2); dispatcher.Drain();
        Assert(delayed == 1 && repeating == 2, "Delayed and repeated scheduler work must run at their documented update counts.");

        scope.Dispose();
        scheduler.Tick(3); dispatcher.Drain();
        Assert(repeating == 2, "Activation cleanup must cancel repeating scheduler work.");
        AssertThrows<ObjectDisposedException>(() => service.NextUpdate("stale", () => { }));
    }

    private static void SchedulerElapsedWorkUsesMonotonicClockUnits()
    {
        var clock = new ManualMonotonicClock(3);
        var dispatcher = new PluginDispatcherHost(16, TimeSpan.FromSeconds(1));
        var scheduler = new PluginSchedulerHost(clock);
        using var scope = new PluginResourceScope();
        PluginManifest manifest = CreateManifest();
        IPluginScheduler service = scheduler.CreateService(manifest, scope, dispatcher.CreateService(manifest, scope), new TestLogger());
        int oneShot = 0;
        int repeating = 0;
        service.After("one-second", TimeSpan.FromSeconds(1), () => oneShot++);
        service.Every("half-second", TimeSpan.FromMilliseconds(500), () => repeating++);

        scheduler.Tick(0); dispatcher.Drain();
        Assert(oneShot == 0 && repeating == 0, "Elapsed work must not run before its monotonic deadline.");
        clock.Advance(1);
        scheduler.Tick(1); dispatcher.Drain();
        Assert(oneShot == 0 && repeating == 0, "Fractional TimeSpan conversion must round up at a non-10MHz clock frequency.");
        clock.Advance(1);
        scheduler.Tick(2); dispatcher.Drain();
        Assert(oneShot == 0 && repeating == 1, "Half-second repeat work must use the scheduler clock frequency, not TimeSpan ticks.");
        clock.Advance(1);
        scheduler.Tick(3); dispatcher.Drain();
        Assert(oneShot == 1 && repeating == 1, "One-shot elapsed work must run at exactly one synthetic clock second.");
        clock.Advance(1);
        scheduler.Tick(4); dispatcher.Drain();
        Assert(repeating == 2, "Repeating elapsed work must schedule its next deadline in the same monotonic unit.");
        Assert(MonotonicClockMath.ToClockTicks(TimeSpan.MaxValue, long.MaxValue) == long.MaxValue,
            "Large elapsed delays must saturate instead of overflowing their clock deadline.");
    }

    private static void BackgroundWorkIsBoundedAndActivationOwned()
    {
        var scheduler = new PluginSchedulerHost(maximumScheduledWorkPerPlugin: 4, maximumBackgroundWorkPerPlugin: 1);
        using var scope = new PluginResourceScope();
        PluginManifest manifest = CreateManifest();
        IPluginScheduler service = scheduler.CreateService(manifest, scope, new PluginDispatcherHost(8, TimeSpan.FromSeconds(1)).CreateService(manifest, scope), new TestLogger());
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        IPluginRegistration active = service.RunBackground("held", _ => Task.Run(() => { started.Set(); release.Wait(); }));
        Assert(started.Wait(TimeSpan.FromSeconds(1)), "Background work must retain and start its owned task.");
        Assert(scheduler.GetBackgroundWorkCount(manifest.Id) == 1, "The background quota must count an in-flight callback exactly once.");
        AssertThrows<InvalidOperationException>(() => service.RunBackground("over-limit", _ => Task.CompletedTask));
        active.Dispose();
        Assert(scheduler.GetBackgroundWorkCount(manifest.Id) == 1, "Disposing a still-running callback must not free its physical quota slot early.");
        AssertThrows<InvalidOperationException>(() => service.RunBackground("disposed-but-running", _ => Task.CompletedTask));
        release.Set();
        Assert(scheduler.CancelAndDrainBackgroundWorkAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(),
            "Scheduler shutdown must observe completion after a non-cooperative background callback is allowed to finish.");
        Assert(scheduler.GetBackgroundWorkCount(manifest.Id) == 0, "Background capacity must return exactly once after completion.");

        var completedScheduler = new PluginSchedulerHost(maximumBackgroundWorkPerPlugin: 1);
        using var completedScope = new PluginResourceScope();
        IPluginScheduler completed = completedScheduler.CreateService(manifest, completedScope, new PluginDispatcherHost(8, TimeSpan.FromSeconds(1)).CreateService(manifest, completedScope), new TestLogger());
        var normalCompletion = new ManualResetEventSlim();
        completed.RunBackground("complete", _ => { normalCompletion.Set(); return Task.CompletedTask; });
        Assert(normalCompletion.Wait(TimeSpan.FromSeconds(1)), "Normal background work must complete successfully.");
        Assert(SpinWait.SpinUntil(() => completedScheduler.GetBackgroundWorkCount(manifest.Id) == 0, TimeSpan.FromSeconds(1)), "Normal completion must release the active background quota.");
        completed.RunBackground("reused-capacity", _ => Task.CompletedTask).Dispose();

        var cancelledScheduler = new PluginSchedulerHost(maximumBackgroundWorkPerPlugin: 1);
        using var cancelledScope = new PluginResourceScope();
        IPluginScheduler cancelled = cancelledScheduler.CreateService(manifest, cancelledScope, new PluginDispatcherHost(8, TimeSpan.FromSeconds(1)).CreateService(manifest, cancelledScope), new TestLogger());
        var cancellationStarted = new ManualResetEventSlim();
        var cancellationObserved = new ManualResetEventSlim();
        cancelled.RunBackground("cancellable", token => Task.Run(() => { cancellationStarted.Set(); try { token.WaitHandle.WaitOne(); } finally { cancellationObserved.Set(); } }));
        Assert(cancellationStarted.Wait(TimeSpan.FromSeconds(1)), "The cancellation test callback must begin before activation teardown.");
        cancelledScope.Dispose();
        Assert(cancellationObserved.Wait(TimeSpan.FromSeconds(1)), "Activation teardown must cancel owned background work.");
        Assert(cancelledScheduler.CancelAndDrainBackgroundWorkAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(),
            "Cancelled background work must be drained without leaving unobserved tasks.");
        AssertThrows<ObjectDisposedException>(() => cancelled.RunBackground("stale", _ => Task.CompletedTask));

        var failingScheduler = new PluginSchedulerHost(maximumBackgroundWorkPerPlugin: 1);
        using var failingScope = new PluginResourceScope();
        var logger = new TestLogger();
        IPluginScheduler failing = failingScheduler.CreateService(manifest, failingScope, new PluginDispatcherHost(8, TimeSpan.FromSeconds(1)).CreateService(manifest, failingScope), logger);
        var faultStarted = new ManualResetEventSlim();
        failing.RunBackground("fault", _ => { faultStarted.Set(); return Task.FromException(new InvalidOperationException("expected background failure")); });
        Assert(faultStarted.Wait(TimeSpan.FromSeconds(1)), "The failing background callback must begin before drain observation is tested.");
        Assert(failingScheduler.CancelAndDrainBackgroundWorkAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(), "Faulted background work must still be observed and drained.");
        Assert(logger.ContainsError("fault") && logger.ContainsError(manifest.Id.Value), "Unexpected background failures must remain attributed to the owning plugin logger.");
    }

    private static void TransientSchedulerAndDispatcherResourcesAreReleased()
    {
        var dispatcher = new PluginDispatcherHost(128, TimeSpan.FromSeconds(1), 128, 128);
        var scheduler = new PluginSchedulerHost(128, 8);
        using var scope = new PluginResourceScope();
        PluginManifest manifest = CreateManifest();
        IPluginDispatcher dispatch = dispatcher.CreateService(manifest, scope, new TestLogger());
        IPluginScheduler service = scheduler.CreateService(manifest, scope, dispatch, new TestLogger());
        int baseline = scope.ResourceCount;
        int callbacks = 0;
        for (int index = 0; index < 64; index++) service.NextUpdate("transient-" + index, () => callbacks++);
        scheduler.Tick(1);
        dispatcher.Drain();
        Assert(callbacks == 64, "Every owned one-shot scheduled callback must execute exactly once.");
        Assert(scope.ResourceCount == baseline, "Completed scheduler and dispatcher registrations must release their scope ownership instead of accumulating for the activation lifetime.");
    }

    private static void LifecycleDrainsActivationBackgroundWorkBeforeDisableAndReenable()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateBundledTestManifest("activation.background.sync");
        var plugin = new ActivationBackgroundPlugin();
        using var controller = new PluginLifecycleController(plugin, host.Create(manifest), () => host.Create(manifest), TimeSpan.FromSeconds(1));
        controller.Validate();
        controller.Initialize();
        controller.Enable();
        Assert(plugin.Started.Wait(TimeSpan.FromSeconds(1)), "The activation-owned background callback must start before disable is tested.");
        controller.Disable();
        Assert(plugin.DisableObservedDrain, "Synchronous disable must not invoke the plugin callback while its activation background work remains active.");

        controller.Initialize();
        controller.Enable();
        Assert(plugin.StartCount == 2, "Re-enable must create a fresh activation that admits new background work independently.");
        controller.Disable();
        Assert(plugin.DisableCount == 2, "Each activation disable must drain only that activation's background work.");
    }

    private static void AsyncLifecycleDrainsActivationBackgroundWorkBeforeDisable()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateBundledTestManifest("activation.background.async");
        var plugin = new AsyncActivationBackgroundPlugin();
        using var controller = new PluginLifecycleController(plugin, host.Create(manifest), () => host.Create(manifest), TimeSpan.FromSeconds(1));
        controller.Validate();
        controller.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        controller.EnableAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(plugin.Started.Wait(TimeSpan.FromSeconds(1)), "The async activation-owned background callback must start before disable is tested.");
        controller.DisableAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(plugin.DisableObservedDrain, "Asynchronous disable must drain the activation background work before its lifecycle callback.");
    }

    private static void ChatDecoratorOwnershipDoesNotRequireAnEditor()
    {
        var manifest = new PluginManifest(new PluginId("decorator.only"), "Decorator only", new Version(1, 0), "Tests", "Decorator only", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        using var scope = new PluginResourceScope();
        var interactions = new PluginUserInteractionHost(UnsupportedPluginUserInteractionBackend.Instance);
        IPluginUserInteractionService interaction = interactions.CreateService(manifest, scope);
        var host = new PluginChatHost();
        host.CreateService(manifest, scope, interaction).RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("decorator"), new TestDecorator("decorated"));
        Assert(host.HasMessageDecorators && host.TryGetInteraction(manifest.Id, out IPluginUserInteractionService? resolved) && ReferenceEquals(interaction, resolved), "Decorator-only chat extensions must remain interactive without an input editor.");
    }

    private static void DistinctTypedSettingDefinitionsAreRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-settings-definition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new PluginSettingsStore(root, new PluginId("settings.definition"));
            settings.Register(new PluginSettingDefinition<int>("value", 1, value => Math.Max(0, value)));
            AssertThrows<InvalidOperationException>(() => settings.Register(new PluginSettingDefinition<int>("value", 1, value => Math.Min(10, value))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void PackageCatalogReadsManifestWithoutAssemblyLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "alacrity-catalog-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = Path.Combine(root, "plugins", "catalog.plugin");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "plugin.json"), "{\"schemaVersion\":1,\"id\":\"catalog.plugin\",\"name\":\"Catalog\",\"version\":\"1.0\",\"publisher\":\"Tests\",\"description\":\"Catalog\",\"supportedGameVersions\":[\"1.4.5.6\"]}");
            var catalog = new PluginPackageCatalog(new PluginPackageManifestReader()).Discover(root);
            Assert(catalog.Count == 1 && catalog[0].Manifest.Id == new PluginId("catalog.plugin"), "Catalog discovery must create verified metadata before any assembly is loaded.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void PackageCompatibilityRejectsStalePluginBeforeAssemblyLoad()
    {
        PluginManifest manifest = new PluginManifest(
            new PluginId("stale.compatibility"), "Stale", new Version(1, 0), "Tests", "Stale package",
            new[] { "1.4.5.6" }, compatibility: PluginCompatibilityRequirements.Legacy);
        PluginCompatibilityException error = AssertThrows<PluginCompatibilityException>(() => PluginCompatibilityValidator.EnsureSupported(manifest));
        Assert(error.Component == "PluginSdk" && error.Expected == 1 && error.Actual == AlacrityCompatibility.PluginSdk,
            "Compatibility validation must identify the mismatched component before any assembly load is attempted.");

        PluginCompatibilityValidator.EnsureSupported(CreateManifest());
    }

    private static void PackageCompatibilityDiagnosesHostAndBridgeRequirements()
    {
        PluginManifest hostStale = new PluginManifest(new PluginId("stale.host"), "Stale host", new Version(1, 0), "Tests", "Host compatibility test", new[] { "1.4.5.6" },
            compatibility: new PluginCompatibilityRequirements(AlacrityCompatibility.PluginSdk, 1, AlacrityCompatibility.BridgeAbi));
        PluginCompatibilityException host = AssertThrows<PluginCompatibilityException>(() => PluginCompatibilityValidator.EnsureSupported(hostStale));
        Assert(host.Component == "Core host" && host.Expected == 1 && host.Actual == AlacrityCompatibility.Host,
            "Host compatibility validation must identify a stale Core participant before assembly load.");

        PluginManifest bridgeStale = new PluginManifest(new PluginId("stale.bridge"), "Stale bridge", new Version(1, 0), "Tests", "Bridge compatibility test", new[] { "1.4.5.6" },
            compatibility: new PluginCompatibilityRequirements(AlacrityCompatibility.PluginSdk, AlacrityCompatibility.Host, 1));
        PluginCompatibilityException bridge = AssertThrows<PluginCompatibilityException>(() => PluginCompatibilityValidator.EnsureSupported(bridgeStale));
        Assert(bridge.Component == "Terraria bridge ABI" && bridge.Expected == 1 && bridge.Actual == AlacrityCompatibility.BridgeAbi,
            "Bridge compatibility validation must identify a stale ABI participant before assembly load.");
    }

    private static void IncompatibleGameVersionNeverLoadsAssembly()
    {
        string root = Path.Combine(Path.GetTempPath(), "alacrity-version-admission-" + Guid.NewGuid().ToString("N"));
        try
        {
            string package = Path.Combine(root, "plugins", "version.test");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "plugin.json"), "{\"schemaVersion\":1,\"id\":\"version.test\",\"name\":\"Version Test\",\"version\":\"1.0.0\",\"publisher\":\"Tests\",\"description\":\"Compatibility test\",\"supportedGameVersions\":[\"9.9.9\"],\"pluginSdkCompatibilityVersion\":2,\"hostCompatibilityVersion\":2,\"bridgeAbiVersion\":2,\"entryAssembly\":\"missing.dll\",\"entryType\":\"Missing.Plugin\"}");
            PluginPackageDescriptor descriptor = new PluginPackageCatalog(new PluginPackageManifestReader()).Discover(root).Single();
            var runtime = new PluginRuntimeHost(
                new PluginPackageCatalog(new PluginPackageManifestReader()),
                new PluginAssemblyLoader(),
                new PluginHostContextFactory(root, new PluginServiceHub(), new PluginExtensionHost(), new PluginCommandHost()),
                "1.4.5.6");
            bool rejected = false;
            try
            {
                _ = runtime.LoadTrusted(descriptor, new PluginTrustVerificationResult(PluginTrustLevel.LocallyTrusted, "test"), new TestLogger(), new TestMultiplayerSession());
            }
            catch (PluginGameVersionCompatibilityException)
            {
                rejected = true;
            }
            Assert(rejected, "An incompatible plugin must be rejected before its missing entry assembly is considered.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void OverlayDispatchIsOrderedIsolatedAndScopeOwned()
    {
        var host = new PluginOverlayHost(TimeSpan.Zero);
        var firstManifest = new PluginManifest(new PluginId("first.overlay"), "First", new Version(1, 0), "Tests", "First overlay", new[] { "1.4.5.6" }, capabilities: PluginCapability.Rendering, permissions: PluginPermission.DrawUserInterface);
        var secondManifest = new PluginManifest(new PluginId("second.plugin"), "Second", new Version(1, 0), "Tests", "Second test", new[] { "1.4.5.6" }, capabilities: PluginCapability.Rendering, permissions: PluginPermission.DrawUserInterface);
        using var firstScope = new PluginResourceScope();
        using var secondScope = new PluginResourceScope();
        var order = new List<string>();
        host.CreateService(firstManifest, firstScope).Register(new PluginOverlayDescriptor("foreground", PluginOverlayLayer.Foreground), (_, _) => order.Add("first"));
        host.CreateService(secondManifest, secondScope).Register(new PluginOverlayDescriptor("background", PluginOverlayLayer.Background), (_, _) => order.Add("second"));
        host.CreateService(secondManifest, secondScope).Register(new PluginOverlayDescriptor("failure", PluginOverlayLayer.WorldMarkers), (_, _) => throw new InvalidOperationException("expected overlay failure"));
        host.CreateService(firstManifest, firstScope).Register(new PluginOverlayDescriptor("hud", PluginOverlayLayer.Foreground, 0, PluginOverlaySpace.Hud), (_, _) => order.Add("hud"));
        host.CreateService(firstManifest, firstScope).Register(new PluginOverlayDescriptor("menu", PluginOverlayLayer.Foreground, 0, PluginOverlaySpace.Menu), (_, _) => order.Add("menu"));
        host.Dispatch(new TestOverlayCanvas(), new PluginOverlayFrame(1920, 1080, 1f, false, TimeSpan.Zero), new TestLogger());
        Assert(order.SequenceEqual(new[] { "second", "first" }), "Overlays must dispatch in deterministic layer order and isolate a failing callback.");
        order.Clear();
        host.Dispatch(new TestOverlayCanvas(), new PluginOverlayFrame(1920, 1080, 1f, false, TimeSpan.Zero), PluginOverlaySpace.Hud);
        Assert(order.SequenceEqual(new[] { "hud" }), "World and HUD overlays must dispatch only through their declared coordinate-space phase.");
        order.Clear();
        host.Dispatch(new TestOverlayCanvas(), new PluginOverlayFrame(1920, 1080, 1f, true, TimeSpan.Zero), PluginOverlaySpace.Menu);
        Assert(order.SequenceEqual(new[] { "menu" }), "Menu overlays must remain isolated from world and HUD registrations.");
        firstScope.ReleaseAll();
        order.Clear();
        host.Dispatch(new TestOverlayCanvas(), new PluginOverlayFrame(1920, 1080, 1f, false, TimeSpan.Zero));
        Assert(order.SequenceEqual(new[] { "second" }) && host.CountFor(firstManifest.Id) == 0 && host.CountFor(secondManifest.Id) == 2, "Disabling one scope must remove only that plugin's overlays.");
    }

    private static void RendererFailureSuspensionRecoversAfterCooldown()
    {
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var manifest = new PluginManifest(new PluginId("recovery.renderer"), "Recovery", new Version(1, 0), "Tests", "Renderer recovery", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface | PluginCapability.Rendering, permissions: PluginPermission.DrawUserInterface);
        using var hudScope = new PluginResourceScope();
        var hud = new PluginHudHost(TimeSpan.FromSeconds(5), () => now);
        int hudCalls = 0;
        hud.CreateService(manifest, hudScope).Register(new PluginHudWidgetDescriptor("retry", 0), (_, __) => { hudCalls++; if (hudCalls <= 3) throw new InvalidOperationException("expected"); });
        for (int index = 0; index < 4; index++) hud.Dispatch(new TestHudRenderer(), new PluginHudFrame(1, 1, 1f, TimeSpan.Zero, 0));
        Assert(hudCalls == 3, "A failed HUD widget must suspend after its rolling threshold.");
        now = now.AddSeconds(6);
        hud.Dispatch(new TestHudRenderer(), new PluginHudFrame(1, 1, 1f, TimeSpan.Zero, 0));
        Assert(hudCalls == 4, "A suspended HUD widget must receive one retry after the cooldown.");

        using var overlayScope = new PluginResourceScope();
        var overlays = new PluginOverlayHost(TimeSpan.FromSeconds(5), () => now);
        int overlayCalls = 0;
        overlays.CreateService(manifest, overlayScope).Register(new PluginOverlayDescriptor("retry", PluginOverlayLayer.Foreground), (_, __) => { overlayCalls++; if (overlayCalls <= 3) throw new InvalidOperationException("expected"); });
        for (int index = 0; index < 4; index++) overlays.Dispatch(new TestOverlayCanvas(), new PluginOverlayFrame(1, 1, 1f, false, TimeSpan.Zero));
        Assert(overlayCalls == 3, "A failed overlay must suspend after its rolling threshold.");
        now = now.AddSeconds(6);
        overlays.Dispatch(new TestOverlayCanvas(), new PluginOverlayFrame(1, 1, 1f, false, TimeSpan.Zero));
        Assert(overlayCalls == 4, "A suspended overlay must receive one retry after the cooldown.");

        using var retryScope = new PluginResourceScope();
        var retryHost = new PluginHudHost(TimeSpan.FromSeconds(5), () => now);
        int retryCalls = 0;
        retryHost.CreateService(manifest, retryScope).Register(new PluginHudWidgetDescriptor("failing-retry", 0), (_, __) => { retryCalls++; throw new InvalidOperationException("expected"); });
        for (int index = 0; index < 4; index++) retryHost.Dispatch(new TestHudRenderer(), new PluginHudFrame(1, 1, 1f, TimeSpan.Zero, 0));
        now = now.AddSeconds(6);
        retryHost.Dispatch(new TestHudRenderer(), new PluginHudFrame(1, 1, 1f, TimeSpan.Zero, 0));
        retryHost.Dispatch(new TestHudRenderer(), new PluginHudFrame(1, 1, 1f, TimeSpan.Zero, 0));
        Assert(retryCalls == 4, "A failed retry trial must immediately return to cooldown rather than receive normal failure attempts.");
    }

    private static void WorldProjectionUsesOnlyTheVerifiedCameraTranslation()
    {
        TerrariaWorldProjectionMath.Project(320f, 240f, 100f, 75f, out float ordinaryX, out float ordinaryY);
        Assert(ordinaryX == 220f && ordinaryY == 165f, "World projection must subtract the active camera origin exactly once.");

        TerrariaWorldProjectionMath.Project(-20f, 400f, 75f, -25f, out float negativeX, out float negativeY);
        Assert(negativeX == -95f && negativeY == 425f, "World projection must preserve negative world and camera coordinates without clamping.");

        TerrariaWorldProjectionMath.Project(640f, 360f, 160f, 90f, out float zoomedX, out float zoomedY);
        Assert(zoomedX == 480f && zoomedY == 270f, "The screen-space hook must not apply zoom or a view matrix a second time.");
        Assert(TerrariaWorldProjectionVerifier.TryVerify(new TerrariaWorldProjectionState(160f, 90f, 0.5f, 0.5f, 1f), out _), "Projection verification must accept zoomed-in live state.");
        Assert(TerrariaWorldProjectionVerifier.TryVerify(new TerrariaWorldProjectionState(-80f, 32f, 2f, 2f, -1f), out _), "Projection verification must accept flipped-gravity live state without adding another transform.");
        Assert(!TerrariaWorldProjectionVerifier.TryVerify(new TerrariaWorldProjectionState(0f, 0f, 0f, 1f, 1f), out _), "Projection verification must reject invalid live zoom values.");
    }

    private static void NotificationPublicationCannotOutliveScopeCleanup()
    {
        var center = new PluginNotificationCenter();
        var manifest = new PluginManifest(new PluginId("notification.race"), "Notification race", new Version(1, 0), "Tests", "Race test", new[] { "1.4.5.6" }, capabilities: PluginCapability.UserInterface, permissions: PluginPermission.DrawUserInterface);
        using var scope = new PluginResourceScope();
        IPluginNotificationService service = center.CreateService(manifest, scope);
        var started = new ManualResetEventSlim(false);
        var writers = new Task[8];
        for (int index = 0; index < writers.Length; index++)
        {
            writers[index] = Task.Run(() =>
            {
                started.Wait();
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    try { service.Show("racing notification"); }
                    catch (ObjectDisposedException) { return; }
                }
            });
        }
        started.Set();
        scope.Dispose();
        Task.WaitAll(writers);
        Assert(center.GetActive(DateTimeOffset.UtcNow).All(notification => notification.Owner != manifest.Id), "Scope cleanup must remove notifications and prevent a stale publisher from reviving them.");
        AssertThrows<ObjectDisposedException>(() => service.Show("after cleanup"));
    }

    private static void PackageRegistryRetainsHostLoadFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "alacrity-registry-fault-" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = Path.Combine(root, "plugins", "faulted.plugin");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "plugin.json"), "{\"schemaVersion\":1,\"id\":\"faulted.plugin\",\"name\":\"Faulted\",\"version\":\"1.0\",\"publisher\":\"Tests\",\"description\":\"Faulted package\",\"supportedGameVersions\":[\"1.4.5.6\"]}");
            var descriptor = new PluginPackageCatalog(new PluginPackageManifestReader()).Discover(root)[0];
            var registry = new PluginPackageLifecycleRegistry();
            registry.Discover(descriptor);
            registry.MarkFaulted(descriptor.Manifest.Id, "The manifest-declared entry assembly is missing.");
            var record = registry.Records[0];
            Assert(record.State == PluginPackageLifecycleState.Faulted, "Host package-load failures must be retained as faulted state.");
            Assert(record.Detail == "The manifest-declared entry assembly is missing.", "Faulted package diagnostics must remain available to the application layer.");
            registry.MarkRestartRequired(descriptor.Manifest.Id, "Reload requires restart.");
            Assert(registry.Records[0].State == PluginPackageLifecycleState.RestartRequired, "Reload requests must surface the safe restart-required state.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void PresenterProjectsRuntimePackageRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "alacrity-presenter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = Path.Combine(root, "plugins", "presenter.plugin");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "plugin.json"), "{\"schemaVersion\":1,\"id\":\"presenter.plugin\",\"name\":\"Presenter\",\"version\":\"1.0\",\"publisher\":\"Tests\",\"description\":\"Presenter package\",\"supportedGameVersions\":[\"1.4.5.6\"]}");
            var runtime = new PluginManagerRuntime(
                new PluginRuntimeHost(new PluginPackageCatalog(new PluginPackageManifestReader()), new PluginAssemblyLoader(), new PluginHostContextFactory(root, new PluginServiceHub(), new PluginExtensionHost(), new PluginCommandHost())),
                new PluginPackageLifecycleRegistry(),
                new PluginActivationCoordinator(new PatchHost(new MockPatchFileStore(), new Sha256PatchVerifier(), new InMemoryPatchJournal()), new PluginEnablePlanner(), new PluginEnableExecutor(), new PluginActivationGate(new PluginDependencyDiagnostics())));
            runtime.Discover(root);
            var rows = new PluginManagerPresenter().Present(runtime, Array.Empty<PluginDependencyWarning>());
            Assert(rows.Count == 1 && rows[0].Id == new PluginId("presenter.plugin"), "The presenter must project manifest-first runtime package rows.");
            Assert(!rows[0].CanToggle, "A manifest-only package must not be presented as toggleable.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void SettingsSchemaMigrationPersistsOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "alacrity-settings-schema-" + Guid.NewGuid().ToString("N"));
        try
        {
            var id = new PluginId("schema.plugin");
            var invoked = 0;
            var first = new PluginSettingsStore(root, id, 2, (settings, previous) => { invoked++; settings.Set("migrated", previous + 1); });
            Assert(invoked == 1 && first.SchemaVersion == 2 && first.Get("migrated", 0) == 1, "A schema upgrade must run and persist its migration.");
            var second = new PluginSettingsStore(root, id, 2, (settings, previous) => invoked++);
            Assert(invoked == 1 && second.Get("migrated", 0) == 1, "The same schema migration must not run again after reopen.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void PluginUninstallPreservesOrRemovesOnlySelectedData()
    {
        var root = Path.Combine(Path.GetTempPath(), "alacrity-uninstall-" + Guid.NewGuid().ToString("N"));
        try
        {
            var id = new PluginId("uninstall.plugin");
            var installations = new PluginInstallationStore(root);
            Directory.CreateDirectory(installations.GetPackageDirectory(id));
            var data = new PluginDataStore(root, id);
            using (var stream = new StreamWriter(data.Create("saved.txt"))) stream.Write("keep");
            var service = new PluginUninstallService(root, installations);
            service.Execute(service.Plan(id, false));
            Assert(!Directory.Exists(installations.GetPackageDirectory(id)) && data.Exists("saved.txt"), "Package-only uninstall must preserve isolated user data.");
            service.Execute(service.Plan(id, true));
            Assert(!Directory.Exists(data.RootPath), "Complete uninstall must remove only the selected plugin data root.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void SettingsValidationFeatureScopeAndAtomicDataWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "alacrity-settings-features-" + Guid.NewGuid().ToString("N"));
        try
        {
            var id = new PluginId("features.plugin");
            var settings = new PluginSettingsStore(root, id);
            settings.Register("quality", 2, value => value >= 1 && value <= 3);
            Assert(settings.Get("quality", 0) == 2, "Registered settings must apply defaults.");
            AssertThrows<ArgumentException>(() => settings.Set("quality", 4));
            var feature = settings.CreateFeatureSettings(new PluginFeatureId("overlay"));
            feature.Set("enabled", true);
            Assert(settings.Get("feature.overlay.enabled", false), "Feature settings must use a separate key namespace.");
            var data = new PluginDataStore(root, id);
            data.WriteAtomically("state.bin", new byte[] { 1, 2, 3 });
            using (var stream = data.OpenRead("state.bin")) Assert(stream.Length == 3, "Atomic data writes must replace the complete target file.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("example.plugin"),
            "Example",
            new Version(1, 0),
            "Tests",
            "Test plugin",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Diagnostics,
            permissions: PluginPermission.None);
    }

    private static TException AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }

    private sealed class TestPlugin : IAlacrityPlugin
    {
        private readonly IPluginResourceScope resources;
        private readonly List<string> order;
        private readonly bool failOnEnable;

        public TestPlugin(IPluginResourceScope resources, List<string> order, bool failOnEnable)
        {
            this.resources = resources;
            this.order = order;
            this.failOnEnable = failOnEnable;
        }

        public void Initialize(IPluginContext context)
        {
            resources.Own("first", PluginResourceKind.Other, new TestResource("first", order));
            resources.Own("second", PluginResourceKind.Other, new TestResource("second", order));
        }

        public void Enable()
        {
            if (failOnEnable)
                throw new InvalidOperationException("Expected test failure.");
        }

        public void Disable() { }
        public void Shutdown() { }
    }

    private static class BridgeReflectionFixture
    {
        public static int Counter;
        public static void Draw(int value) { Counter = value; }
    }

    private sealed class ActivationBackgroundPlugin : IAlacrityPlugin
    {
        private readonly ManualResetEventSlim started = new ManualResetEventSlim();
        private readonly ManualResetEventSlim finished = new ManualResetEventSlim();

        public ManualResetEventSlim Started => started;
        public int StartCount { get; private set; }
        public int DisableCount { get; private set; }
        public bool DisableObservedDrain { get; private set; }

        public void Initialize(IPluginContext context)
        {
            StartCount++;
            context.Scheduler.RunBackground("activation-background", async token =>
            {
                started.Set();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); }
                finally { finished.Set(); }
            });
        }

        public void Enable() { }

        public void Disable()
        {
            DisableCount++;
            DisableObservedDrain = finished.IsSet;
            started.Reset();
            finished.Reset();
        }

        public void Shutdown() { }
    }

    private sealed class AsyncActivationBackgroundPlugin : IAsyncAlacrityPlugin
    {
        private readonly ManualResetEventSlim finished = new ManualResetEventSlim();

        public ManualResetEventSlim Started { get; } = new ManualResetEventSlim();
        public bool DisableObservedDrain { get; private set; }

        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            context.Scheduler.RunBackground("async-activation-background", async token =>
            {
                Started.Set();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); }
                finally { finished.Set(); }
            });
            return Task.CompletedTask;
        }

        public Task EnableAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisableAsync(CancellationToken cancellationToken)
        {
            DisableObservedDrain = finished.IsSet;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestContext : IPluginContext
    {
        public TestContext(PluginManifest manifest, IPluginResourceScope resources)
        {
            Manifest = manifest;
            Resources = resources;
            Logger = new TestLogger();
            Dispatcher = new PluginDispatcherHost().CreateService(manifest, resources);
            Scheduler = new PluginSchedulerHost().CreateService(manifest, resources, Dispatcher, Logger);
            Notifications = new PluginNotificationCenter().CreateService(manifest);
            Services = new PluginServiceHub().CreateRegistry(manifest, resources);
            Settings = new TestSettings();
            Storage = new TestStorage();
            Events = new TestEvents();
            Commands = new TestCommands();
            Keybinds = new TestKeybinds();
            Ui = new TestUi();
            Overlays = new TestOverlays();
            Hud = new TestHud();
            UserInteraction = new PluginUserInteractionHost(UnsupportedPluginUserInteractionBackend.Instance).CreateService(manifest);
            Terraria = new TestTerrariaServices();
            Multiplayer = new TestMultiplayerSession();
        }

        public PluginManifest Manifest { get; }
        public IPluginResourceScope Resources { get; }
        public IPluginLogger Logger { get; }
        public IPluginDispatcher Dispatcher { get; }
        public IPluginScheduler Scheduler { get; }
        public IPluginNotificationService Notifications { get; }
        public IPluginServiceRegistry Services { get; }
        public IPluginSettings Settings { get; }
        public IPluginStorage Storage { get; }
        public IPluginEventService Events { get; }
        public IPluginCommandService Commands { get; }
        public IPluginKeybindService Keybinds { get; }
        public IPluginUiService Ui { get; }
        public IPluginOverlayService Overlays { get; }
        public IPluginHudService Hud { get; }
        public IPluginUserInteractionService UserInteraction { get; }
        public ITerrariaServices Terraria { get; }
        public IMultiplayerSession Multiplayer { get; }
    }

    private sealed class CleanupFailurePlugin : IAlacrityPlugin
    {
        private readonly IPluginResourceScope resources;
        private readonly bool failDisable;
        private readonly bool failShutdown;

        public CleanupFailurePlugin(IPluginResourceScope resources, bool failDisable, bool failShutdown)
        {
            this.resources = resources;
            this.failDisable = failDisable;
            this.failShutdown = failShutdown;
        }

        public void Initialize(IPluginContext context)
        {
            resources.Own("cleanup-failure", PluginResourceKind.Other, new ThrowingResource());
        }

        public void Enable() { }

        public void Disable()
        {
            if (failDisable)
                throw new InvalidOperationException("Expected disable failure.");
        }

        public void Shutdown()
        {
            if (failShutdown)
                throw new InvalidOperationException("Expected shutdown failure.");
        }
    }

    private sealed class OrderedSyncPlugin : IAlacrityPlugin
    {
        private readonly List<string> order; private readonly string name;
        public OrderedSyncPlugin(List<string> order, string name) { this.order = order; this.name = name; }
        public void Initialize(IPluginContext context) { order.Add(name + ":init"); }
        public void Enable() { order.Add(name + ":enable"); }
        public void Disable() { order.Add(name + ":disable"); }
        public void Shutdown() { order.Add(name + ":shutdown"); }
    }

    private sealed class OrderedAsyncPlugin : IAsyncAlacrityPlugin
    {
        private readonly List<string> order; private readonly string name;
        public OrderedAsyncPlugin(List<string> order, string name) { this.order = order; this.name = name; }
        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) { order.Add(name + ":init"); return Task.CompletedTask; }
        public Task EnableAsync(CancellationToken cancellationToken) { order.Add(name + ":enable"); return Task.CompletedTask; }
        public Task DisableAsync(CancellationToken cancellationToken) { order.Add(name + ":disable"); return Task.CompletedTask; }
        public Task ShutdownAsync(CancellationToken cancellationToken) { order.Add(name + ":shutdown"); return Task.CompletedTask; }
    }

    private sealed class NonCooperativeAsyncPlugin : IAsyncAlacrityPlugin
    {
        private readonly IPluginResourceScope scope;
        public NonCooperativeAsyncPlugin(IPluginResourceScope scope) { this.scope = scope; }
        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) { scope.Own("async-timeout", PluginResourceKind.BackgroundTask, new TestRegistration("async-timeout")); return Task.CompletedTask; }
        public Task EnableAsync(CancellationToken cancellationToken) => new TaskCompletionSource<object?>().Task;
        public Task DisableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StartedNonCooperativeAsyncPlugin : IAsyncAlacrityPlugin
    {
        private readonly TaskCompletionSource<object?> completion = new TaskCompletionSource<object?>();
        public ManualResetEventSlim Started { get; } = new ManualResetEventSlim(false);

        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EnableAsync(CancellationToken cancellationToken)
        {
            Started.Set();
            return completion.Task;
        }

        public Task DisableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Complete() => completion.TrySetResult(null);
    }

    private sealed class ShutdownBlockingAsyncPlugin : IAsyncAlacrityPlugin
    {
        private readonly IPluginResourceScope scope;
        private readonly bool failShutdown;
        private readonly TaskCompletionSource<object?> disableCompletion = new TaskCompletionSource<object?>();

        public ShutdownBlockingAsyncPlugin(IPluginResourceScope scope, bool failShutdown)
        {
            this.scope = scope;
            this.failShutdown = failShutdown;
        }

        public bool DisableStarted { get; private set; }
        public bool ShutdownCalled { get; private set; }

        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            scope.Own("async-shutdown", PluginResourceKind.BackgroundTask, new TestRegistration("async-shutdown"));
            return Task.CompletedTask;
        }

        public Task EnableAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisableAsync(CancellationToken cancellationToken)
        {
            DisableStarted = true;
            return failShutdown ? Task.CompletedTask : disableCompletion.Task;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalled = true;
            return failShutdown ? Task.FromException(new InvalidOperationException("Expected async shutdown failure.")) : Task.CompletedTask;
        }

        public void CompleteDisable() => disableCompletion.TrySetResult(null);
    }

    private sealed class CancellableAsyncPlugin : IAsyncAlacrityPlugin
    {
        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => Task.FromCanceled(cancellationToken);
        public Task EnableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ContextRecordingPlugin : IAlacrityPlugin
    {
        public int InitializeCount { get; private set; }
        public IPluginContext? LastContext { get; private set; }
        public void Initialize(IPluginContext context)
        {
            LastContext = context;
            InitializeCount++;
            context.Keybinds.Register(new PluginKeybindDescriptor("reactivate", "T", "Reactivate"), () => { });
        }
        public void Enable() { }
        public void Disable() { }
        public void Shutdown() { }
    }

    private sealed class FailingAsyncUninstallPlugin : IAsyncAlacrityPlugin
    {
        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisableAsync(CancellationToken cancellationToken) => Task.FromException(new InvalidOperationException("Expected async uninstall failure."));
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestSettings : IPluginSettings
    {
        private readonly Dictionary<string, object?> values = new Dictionary<string, object?>();
        public event EventHandler<PluginSettingChangedEventArgs>? Changed;
        public IPluginSetting<T> Register<T>(PluginSettingDefinition<T> definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return new TestSetting<T>(this, definition);
        }
        public T Get<T>(string key, T defaultValue) => values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
        public void Set<T>(string key, T value) { values.TryGetValue(key, out var oldValue); values[key] = value; Changed?.Invoke(this, new PluginSettingChangedEventArgs(key, oldValue, value)); }
        public bool Remove(string key) { if (!values.TryGetValue(key, out var value)) return false; values.Remove(key); Changed?.Invoke(this, new PluginSettingChangedEventArgs(key, value, null)); return true; }
        public void ResetToDefaults() => values.Clear();

        private sealed class TestSetting<T> : IPluginSetting<T>
        {
            private readonly TestSettings settings;
            private readonly PluginSettingDefinition<T> definition;
            public TestSetting(TestSettings settings, PluginSettingDefinition<T> definition) { this.settings = settings; this.definition = definition; }
            public string Key => definition.Key;
            public T DefaultValue => definition.DefaultValue;
            public T Value { get => settings.Get(definition.Key, definition.DefaultValue); set => settings.Set(definition.Key, definition.Normalize == null ? value : definition.Normalize(value)); }
            public void Reset() => Value = definition.DefaultValue;
            public IPluginRegistration Subscribe(Action<T> handler) => new TestRegistration("setting-subscription");
        }
    }

    private sealed class TestStorage : IPluginStorage
    {
        public Stream OpenRead(string relativePath) => throw new NotSupportedException();
        public Stream Create(string relativePath) => throw new NotSupportedException();
        public bool Exists(string relativePath) => false;
        public void Delete(string relativePath) { }
        public IReadOnlyList<string> Enumerate(string relativeDirectory) => Array.Empty<string>();
    }

    private sealed class TestEvents : IPluginEventService
    {
        public IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null) => new TestRegistration("event");
    }

    private sealed class TestCommands : IPluginCommandService
    {
        public IPluginRegistration Register(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler) => new TestRegistration("command");
    }

    private sealed class TestKeybinds : IPluginKeybindService
    {
        public IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action handler) => new TestRegistration("keybind");
        public IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action<bool> stateHandler) => new TestRegistration("keybind-state");
    }

    private sealed class TestUi : IPluginUiService
    {
        public IPluginRegistration RegisterSettingsPage(PluginUiContribution contribution) => new TestRegistration("page");
        public IPluginRegistration RegisterSettingsControl(PluginUiContribution contribution) => new TestRegistration("control");
        public IPluginRegistration RegisterSettingsControl(PluginSettingControl control) => new TestRegistration("control");
        public IPluginRegistration RegisterIconInteraction(PluginIconInteractionDescriptor descriptor, Action activate) => new TestRegistration("icon");
        public IPluginRegistration RegisterOverlay(PluginUiContribution contribution) => new TestRegistration("overlay");
    }

    private sealed class TestOverlays : IPluginOverlayService
    {
        public int Registrations { get; private set; }
        public IPluginRegistration Register(PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw)
        {
            Registrations++;
            return new TestRegistration("overlay:" + descriptor.Id);
        }
    }

    private sealed class TestHud : IPluginHudService
    {
        public IPluginRegistration Register(PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw) => new TestRegistration("hud:" + descriptor.Id);
    }

    private sealed class TestHudRenderer : IPluginHudRenderer
    {
        private static readonly IPluginHudCanvas Canvas = new TestHudCanvas();
        public void Render(PluginId owner, PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw, PluginHudFrame frame) => draw(Canvas, frame);
    }

    private sealed class TestHudCanvas : IPluginHudCanvas
    {
        public void DrawPanel(PluginUiRect bounds, PluginOverlayColor color) { }
        public void DrawText(string text, float x, float y, PluginOverlayColor color, float scale = 1f, float originX = 0f, float originY = 0f) { }
        public void DrawAsset(string approvedAssetId, PluginUiRect bounds, PluginOverlayColor? tint = null) { }
        public void DrawPlayerAvatar(int playerId, float x, float y, float scale = 1f) { }
        public void DrawNpcHead(int npcType, float x, float y, float scale = 1f, PluginOverlayColor? tint = null) { }
        public void DrawInteractiveAsset(string interactionId, string approvedAssetId, PluginUiRect bounds) { }
        public void DrawInteractiveNpcHead(string interactionId, int npcType, PluginUiRect bounds) { }
        public bool CapturePointer(PluginUiRect bounds) => false;
    }

    private sealed class TestTerrariaServices : ITerrariaServices
    {
        public IPluginChatService Chat { get; } = new TestChatService();
        public IPluginEntitySnapshotService Entities { get; } = new TestEntitySnapshots();
        public IPluginPlayerService Players { get; } = new TestPlayers();
        public IPluginVisualEffectsService VisualEffects { get; } = new TestVisualEffects();
        public IPluginSessionPresentationService Session { get; } = new TestSessionPresentation();
    }

    private sealed class TestSessionPresentation : IPluginSessionPresentationService
    {
        public PluginSessionPresentationSnapshot GetCurrent() => new PluginSessionPresentationSnapshot("Tests", 255, null);
    }

    private sealed class TestChatService : IPluginChatService
    {
        public IPluginRegistration RegisterInputEditor(ChatInputEditorDescriptor descriptor, IChatInputEditor editor) => new TestRegistration("chat-editor");
        public IPluginRegistration RegisterMessageDecorator(ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator) => new TestRegistration("chat-decorator");
        public IPluginRegistration RegisterMessageFilter(ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter) => new TestRegistration("chat-filter");
        public IPluginRegistration RegisterLinkHandler(ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler) => new TestRegistration("chat-link");
    }

    private sealed class TestEntitySnapshots : IPluginEntitySnapshotService
    {
        public int ActiveEntityCount => 0;
        public void CopyActiveEntities(ICollection<PluginEntitySnapshot> destination) { }
        public void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination) { }
        public bool TryGetBySlot(PluginEntityKind kind, int slot, out PluginEntitySnapshot entity) { entity = default; return false; }
        public bool TryGetByHandle(PluginEntityHandle handle, out PluginEntitySnapshot entity) { entity = default; return false; }
    }

    private sealed class TestVisualEffects : IPluginVisualEffectsService
    {
        public IPluginRegistration RegisterPolicy(PluginVisualEffectsPolicy policy) => new TestRegistration("visual-effects");
    }

    private sealed class TestPlayers : IPluginPlayerService
    {
        public int ActivePlayerCount => 0;
        public bool TryGet(int playerId, out PluginPlayerSnapshot player) { player = default; return false; }
        public bool TryGet(PluginEntityHandle handle, out PluginPlayerSnapshot player) { player = default; return false; }
        public string? GetName(int playerId) => null;
        public void CopyPlayers(ICollection<PluginPlayerSnapshot> destination) { }
        public void CopyBuffs(int playerId, ICollection<PluginBuffSnapshot> destination) { }
    }

    private sealed class TestChatFilter : IChatMessageFilter
    {
        private readonly ChatMessageOrigin hidden;
        public TestChatFilter(ChatMessageOrigin hidden) { this.hidden = hidden; }
        public bool ShouldDisplay(ChatMessageOrigin origin) => origin != hidden;
    }

    private sealed class TestInputEditor : IChatInputEditor
    {
        public ChatInputEditResult Edit(ChatInputSnapshot snapshot, ChatInputAction action) => ChatInputEditResult.Unhandled(snapshot);
    }

    private sealed class TestDecorator : IChatMessageDecorator
    {
        private readonly string text;
        public TestDecorator(string text) { this.text = text; }
        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message) => new[] { new ChatTextSpan(text) };
    }

    private sealed class AppendingDecorator : IChatMessageDecorator
    {
        private readonly string suffix;
        public AppendingDecorator(string suffix) { this.suffix = suffix; }
        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message) => new[] { new ChatTextSpan(message.Text + suffix) };
    }

    private sealed class ThrowingDecorator : IChatMessageDecorator
    {
        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message) => throw new InvalidOperationException("Expected decorator failure.");
    }

    private sealed class RecordingUserInteractionBackend : IPluginUserInteractionBackend
    {
        private string clipboard = string.Empty;
        public int Calls { get; private set; }
        public string? LastOpened { get; private set; }
        public bool TryReadClipboard(out string text) { Calls++; text = clipboard; return true; }
        public bool TryWriteClipboard(string text) { Calls++; clipboard = text ?? string.Empty; return true; }
        public bool TryOpenExternalLink(Uri uri) { Calls++; LastOpened = uri.AbsoluteUri; return true; }
    }

    private sealed class TestOverlayCanvas : IPluginOverlayCanvas
    {
        public void DrawText(string text, float x, float y, PluginOverlayColor color, float scale = 1f) { }
        public void FillRectangle(float x, float y, float width, float height, PluginOverlayColor color) { }
        public void DrawRectangle(float x, float y, float width, float height, PluginOverlayColor color, float thickness = 1f) { }
        public void DrawLine(float startX, float startY, float endX, float endY, PluginOverlayColor color, float thickness = 1f) { }
        public void DrawAsset(string approvedAssetId, float x, float y, float scale = 1f, PluginOverlayColor? tint = null) { }
        public void DrawWorldMarker(float worldX, float worldY, string text, PluginOverlayColor color) { }
        public void DrawWorldRectangle(float worldX, float worldY, float width, float height, PluginOverlayColor color, float thickness = 1f) { }
    }

    private sealed class TestRegistration : IPluginRegistration
    {
        public TestRegistration(string name) { Name = name; }
        public string Name { get; }
        public bool IsReleased { get; private set; }
        public void Dispose() => IsReleased = true;
    }

    private sealed class TestMultiplayerSession : IMultiplayerSession
    {
        public bool IsConnected => false;
        public bool IsVanillaCompatibleMode => true;
        public bool IsAlacrityAwareServer => false;
        public ServerIdentity? Server => null;
        public ServerPluginPolicySnapshot? ActivePolicy => null;
    }

    private sealed class TestLogger : IPluginLogger
    {
        private readonly object gate = new object();
        private readonly List<string> errors = new List<string>();
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { lock (gate) errors.Add(message); }
        public bool ContainsError(string text) { lock (gate) return errors.Any(error => error.Contains(text)); }
    }

    private sealed class ManualMonotonicClock : IMonotonicClock
    {
        private long timestamp;

        public ManualMonotonicClock(long frequency) => Frequency = frequency;

        public long Frequency { get; }

        public long GetTimestamp() => Interlocked.Read(ref timestamp);

        public void Advance(long amount) => Interlocked.Add(ref timestamp, amount);
    }

    private interface IExampleService { }
    private sealed class ExampleService : IExampleService { }

    private sealed class TestResource : IDisposable
    {
        private readonly string name;
        private readonly List<string> order;

        public TestResource(string name, List<string> order)
        {
            this.name = name;
            this.order = order;
        }

        public void Dispose() => order.Add(name);
    }

    private sealed class ThrowingResource : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("Expected cleanup failure.");
    }

    private sealed class MockPatchFileStore : IPatchFileStore
    {
        private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private bool failNextWrite;

        public bool CorruptNextCopy { get; set; }
        public bool CorruptNextWrite { get; set; }
        public bool FailWriteAfterCorruption { get; set; }
        public bool ChangeBeforeNextWrite { get; set; }
        public byte[] ChangedContents { get; set; } = System.Text.Encoding.UTF8.GetBytes("externally-changed");
        public int WriteCount { get; private set; }

        public string GetPathIdentity(string path)
        {
            return System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }

        public bool Exists(string path) => files.ContainsKey(path);

        public byte[] ReadAllBytes(string path)
        {
            if (!files.TryGetValue(path, out var contents))
                throw new InvalidOperationException("Missing mock file: " + path);
            return (byte[])contents.Clone();
        }

        public bool TryWriteAtomically(string path, byte[]? expectedContents, byte[] contents)
        {
            if (ChangeBeforeNextWrite)
            {
                ChangeBeforeNextWrite = false;
                files[path] = (byte[])ChangedContents.Clone();
            }

            if (expectedContents == null)
            {
                if (files.ContainsKey(path))
                    return false;
            }
            else if (!files.TryGetValue(path, out var current) || !BytesEqual(current, expectedContents))
            {
                return false;
            }

            WriteCount++;
            if (failNextWrite)
            {
                failNextWrite = false;
                throw new InvalidOperationException("Expected mock write failure.");
            }
            if (CorruptNextWrite)
            {
                CorruptNextWrite = false;
                files[path] = System.Text.Encoding.UTF8.GetBytes("corrupt");
                failNextWrite = FailWriteAfterCorruption;
                return true;
            }
            files[path] = (byte[])contents.Clone();
            return true;
        }

        public void Copy(string sourcePath, string destinationPath, bool overwrite)
        {
            if (!files.ContainsKey(sourcePath))
                throw new InvalidOperationException("Missing mock source: " + sourcePath);
            if (!overwrite && files.ContainsKey(destinationPath))
                throw new InvalidOperationException("Mock destination already exists: " + destinationPath);
            files[destinationPath] = CorruptNextCopy
                ? System.Text.Encoding.UTF8.GetBytes("corrupt")
                : ReadAllBytes(sourcePath);
            CorruptNextCopy = false;
        }

        public void Put(string path, byte[] contents) => files[path] = (byte[])contents.Clone();

        public void Remove(string path) => files.Remove(path);
    }
}
