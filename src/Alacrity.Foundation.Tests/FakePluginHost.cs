using System;
using System.Collections.Generic;
using System.IO;
using Alacrity.Core;
using Alacrity.PluginSdk;

/// <summary>
/// Test-only host assembled from the real Core registries. It intentionally uses the framework
/// neutral fallback Terraria services, proving ordinary plugin initialization has no hidden
/// TerrariaIntegration dependency.
/// </summary>
internal sealed class FakePluginHost : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "alacrity-fake-host-" + Guid.NewGuid().ToString("N"));
    private readonly PluginHostContextFactory contexts;
    private readonly List<string> diagnostics = new List<string>();

    internal FakePluginHost()
    {
        Directory.CreateDirectory(root);
        Notifications = new PluginNotificationCenter();
        Extensions = new PluginExtensionHost();
        Commands = new PluginCommandHost();
        Services = new PluginServiceHub();
        Overlays = new PluginOverlayHost();
        Hud = new PluginHudHost();
        RenderingOptimizations = new PluginRenderingOptimizationHost();
        contexts = new PluginHostContextFactory(root, Services, Extensions, Commands, overlays: Overlays, notifications: Notifications, hud: Hud, renderingOptimizations: RenderingOptimizations);
    }

    internal PluginNotificationCenter Notifications { get; }
    internal PluginExtensionHost Extensions { get; }
    internal PluginCommandHost Commands { get; }
    internal PluginServiceHub Services { get; }
    internal PluginOverlayHost Overlays { get; }
    internal PluginHudHost Hud { get; }
    internal PluginRenderingOptimizationHost RenderingOptimizations { get; }
    /// <summary>Plugin-attributed log output captured by the framework-neutral test host.</summary>
    internal IReadOnlyList<string> Diagnostics => diagnostics;
    /// <summary>Current host-rendered notifications, captured through the real notification center.</summary>
    internal IReadOnlyList<PluginNotification> ActiveNotifications => Notifications.GetActive(DateTimeOffset.UtcNow);
    /// <summary>Current real keybind-registry snapshot, suitable for controls-menu assertions.</summary>
    internal PluginKeybindRegistrySnapshot Keybinds => Extensions.GetKeybindSnapshot();

    internal IReadOnlyList<PluginUiContribution> GetSettingsPages(PluginId owner) => Extensions.GetSettingsPages(owner);
    internal IReadOnlyList<PluginSettingControl> GetSettingsControls(PluginId owner) => Extensions.GetSettingsControls(owner);
    internal PluginCommandDispatchResult DispatchCommand(string id, IReadOnlyList<string> arguments, Action<string>? reply = null) => Commands.Dispatch(id, arguments, reply);

    internal PluginHostContext Create(PluginManifest manifest) => contexts.Create(manifest, new FakePluginLogger(manifest.Id, diagnostics), new FakeMultiplayerSession());

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class FakePluginLogger : IPluginLogger
    {
        private readonly PluginId owner;
        private readonly List<string> entries;
        internal FakePluginLogger(PluginId owner, List<string> entries) { this.owner = owner; this.entries = entries; }
        public void Debug(string message) { Write("debug", message); }
        public void Info(string message) { Write("info", message); }
        public void Warn(string message) { Write("warn", message); }
        public void Error(string message, Exception? exception = null) { Write("error", message + (exception == null ? string.Empty : ": " + exception.GetType().Name)); }
        private void Write(string level, string message)
        {
            lock (entries)
                entries.Add(owner.Value + " [" + level + "] " + (message ?? string.Empty));
        }
    }

    private sealed class FakeMultiplayerSession : IMultiplayerSession
    {
        public bool IsConnected => false;
        public bool IsVanillaCompatibleMode => true;
        public bool IsAlacrityAwareServer => false;
        public ServerIdentity? Server => null;
        public ServerPluginPolicySnapshot? ActivePolicy => null;
    }
}
