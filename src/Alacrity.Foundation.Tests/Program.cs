using System;
using System.Collections.Generic;
using System.IO;
using Alacrity.App;
using Alacrity.Core;
using Alacrity.PluginSdk;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ManifestRejectsInvalidServerClassification();
            PackageManifestLoadsBeforePluginExecution();
            LegacyPluginManifestMismatchIsRejected();
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
            LifecycleFailureFaultsAndCleansResources();
            LifecyclePreservesCallbackFailureAndRecordsCleanupFailure();
            LifecycleUninstallReachesTerminalStateAfterFailures();
            ResourceScopeReleasesChildrenInParentOrder();
            ResourceScopeRecordsIndividualCleanupFailures();
            ActivationTransactionRollsBackInReverseOrder();
            PatchServiceRequiresPermissionTrustAndPolicy();
            ScopedServicesRespectDependenciesAndCleanup();
            ExtensionRegistrationsAreScopeOwned();
            PluginDataAndSettingsStayIsolated();
            EnablePlannerAutoEnablesDependencies();
            DependencyWarningsClearWhenResolved();
            NotificationsExpireWithoutPersistence();
            PackageCatalogReadsManifestWithoutAssemblyLoad();
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

    private static void LegacyPluginManifestMismatchIsRejected()
    {
        var resources = new PluginResourceScope();
        var plugin = new TestPlugin(CreateManifest(), resources, new List<string>(), false);
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
        AssertThrows<InvalidOperationException>(() => new PluginLifecycleController(plugin, context));
        resources.Dispose();
    }

    private static void LifecycleCleansResourcesInReverseOrder()
    {
        var order = new List<string>();
        var resources = new PluginResourceScope();
        var plugin = new TestPlugin(CreateManifest(), resources, order, false);
        var context = new TestContext(plugin.Manifest, resources);
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
        var plugin = new TestPlugin(CreateManifest(), resources, new List<string>(), false);
        var context = new TestContext(plugin.Manifest, resources);
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
            Assert(menu.Toggle(plugin.Manifest.Id) == PluginLifecycleState.Disabled, "Plugin row toggle should disable the plugin.");
            Assert(menu.Toggle(plugin.Manifest.Id) == PluginLifecycleState.Enabled, "A disabled plugin should reinitialize before the menu enables it again.");
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
        var plugin = new TestPlugin(CreateManifest(), resources, order, true);
        var context = new TestContext(plugin.Manifest, resources);
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
        var plugin = new CleanupFailurePlugin(CreateManifest(), resources, failDisable: true, failShutdown: false);
        var context = new TestContext(plugin.Manifest, resources);
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
        var plugin = new CleanupFailurePlugin(CreateManifest(), resources, failDisable: true, failShutdown: true);
        var context = new TestContext(plugin.Manifest, resources);
        var lifecycle = new PluginLifecycleController(plugin, context);
        lifecycle.Validate();
        lifecycle.Initialize();
        lifecycle.Enable();

        AssertThrows<InvalidOperationException>(() => lifecycle.Uninstall());
        Assert(lifecycle.State == PluginLifecycleState.Uninstalled, "Uninstall must reach a terminal state after callback failures.");
        Assert(lifecycle.LastOperation.CleanupFailures.Count >= 1, "Later shutdown failures must be retained as cleanup diagnostics.");
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
        var services = host.CreateServices(scope);
        var received = 0;
        services.Events.Subscribe<string>(_ => received++);
        services.Keybinds.Register(new PluginKeybindDescriptor("toggle", "P", "Toggle"), () => { });
        services.Ui.RegisterOverlay(new PluginUiContribution("overlay", "Overlay"));
        AssertThrows<InvalidOperationException>(() => services.Keybinds.Register(new PluginKeybindDescriptor("toggle", "O", "Duplicate"), () => { }));
        host.Publish("first");
        scope.ReleaseAll();
        host.Publish("second");
        Assert(received == 1, "Event registrations must be removed with their owning scope.");
        scope.Dispose();
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

    private static void NotificationsExpireWithoutPersistence()
    {
        var notifications = new PluginNotificationCenter();
        notifications.Publish("Dependency enabled.", TimeSpan.FromSeconds(2));
        Assert(notifications.GetActive(DateTimeOffset.UtcNow).Count == 1, "A fresh notification must be available to the presentation layer.");
        Assert(notifications.GetActive(DateTimeOffset.UtcNow.AddSeconds(3)).Count == 0, "Expired notifications must be removed instead of accumulating.");
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

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
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

        public TestPlugin(PluginManifest manifest, IPluginResourceScope resources, List<string> order, bool failOnEnable)
        {
            Manifest = manifest;
            this.resources = resources;
            this.order = order;
            this.failOnEnable = failOnEnable;
        }

        public PluginManifest Manifest { get; }

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

    private sealed class TestContext : IPluginContext
    {
        public TestContext(PluginManifest manifest, IPluginResourceScope resources)
        {
            Manifest = manifest;
            Resources = resources;
            Logger = new TestLogger();
            Services = new PluginServiceHub().CreateRegistry(manifest, resources);
        }

        public PluginManifest Manifest { get; }
        public IPluginResourceScope Resources { get; }
        public IPluginLogger Logger { get; }
        public IPluginServiceRegistry Services { get; }
    }

    private sealed class CleanupFailurePlugin : IAlacrityPlugin
    {
        private readonly IPluginResourceScope resources;
        private readonly bool failDisable;
        private readonly bool failShutdown;

        public CleanupFailurePlugin(PluginManifest manifest, IPluginResourceScope resources, bool failDisable, bool failShutdown)
        {
            Manifest = manifest;
            this.resources = resources;
            this.failDisable = failDisable;
            this.failShutdown = failShutdown;
        }

        public PluginManifest Manifest { get; }

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

    private sealed class TestLogger : IPluginLogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
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
