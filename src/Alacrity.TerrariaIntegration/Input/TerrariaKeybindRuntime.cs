using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework.Input;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.UI.Gamepad;

namespace AlacrityTerraria.Input;

/// <summary>Owns native controls-menu binding persistence and gameplay dispatch for plugin keybinds.</summary>
internal sealed class TerrariaKeybindRuntime
{
    private readonly PluginExtensionHost extensions;
    private readonly PluginNotificationCenter notifications;
    private readonly Action<string, Exception> reportFailure;
    private readonly TerrariaPluginKeybindPersistence persistence;
    private readonly TerrariaPluginKeybindControlsAdapter controls;
    private readonly Dictionary<string, bool> downState = new Dictionary<string, bool>(StringComparer.Ordinal);
    private readonly HashSet<string> nativeIds = new HashSet<string>(StringComparer.Ordinal);
    private long registryVersion = -1;
    private PlayerInputProfile nativeProfile;
    private long nativeRegistryVersion = -1;

    internal TerrariaKeybindRuntime(string root, PluginExtensionHost extensions, PluginNotificationCenter notifications, Action<string, Exception> reportFailure)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A runtime root is required.", nameof(root));
        this.extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        persistence = new TerrariaPluginKeybindPersistence(root, reportFailure);
        controls = new TerrariaPluginKeybindControlsAdapter(extensions, EnsureBinding, ObserveBinding, reportFailure);
    }

    internal void AppendControls(UIManageControls controlsState)
    {
        if (controlsState != null) controls.Append(controlsState);
    }

    internal void Dispatch()
    {
        PluginKeybindRegistrySnapshot snapshot = extensions.GetKeybindSnapshot();
        IReadOnlyList<PluginKeybindRegistration> keybinds = snapshot.Registrations;
        if (keybinds.Count == 0) { downState.Clear(); return; }
        if (registryVersion != snapshot.Version) RemoveStaleState(keybinds, snapshot.Version);
        KeyboardState keyboard = Keyboard.GetState();
        for (int index = 0; index < keybinds.Count; index++)
        {
            PluginKeybindRegistration keybind = keybinds[index];
            bool isDown = IsDown(keybind, keyboard);
            bool wasDown = downState.TryGetValue(keybind.HostId, out bool previous) && previous;
            downState[keybind.HostId] = isDown;
            if (keybind.Descriptor.Activation == PluginKeybindActivation.Hold)
            {
                if (isDown == wasDown) continue;
                ReportInvocation(keybind, extensions.TrySetKeybindState(keybind.HostId, isDown, out Exception failure), failure);
            }
            else
            {
                if (!isDown || wasDown) continue;
                ReportInvocation(keybind, extensions.TryInvokeKeybind(keybind.HostId, out Exception failure), failure);
            }
        }
    }

    internal void EnsureNativeStateShape()
    {
        if (PlayerInput.CurrentProfile == null) return;
        try
        {
            PluginKeybindRegistrySnapshot snapshot = extensions.GetKeybindSnapshot();
            PlayerInputProfile profile = PlayerInput.CurrentProfile;
            if (ReferenceEquals(profile, nativeProfile) && snapshot.Version == nativeRegistryVersion) return;
            var activeIds = new HashSet<string>(snapshot.Registrations.Select(keybind => keybind.HostId), StringComparer.Ordinal);
            foreach (KeyConfiguration configuration in profile.InputModes.Values)
                foreach (string staleId in nativeIds.Where(id => !activeIds.Contains(id)).ToArray()) configuration.KeyStatus.Remove(staleId);
            RemoveStaleTriggerKeys(activeIds);
            for (int index = 0; index < snapshot.Registrations.Count; index++)
            {
                PluginKeybindRegistration keybind = snapshot.Registrations[index];
                EnsureBinding(keybind, InputMode.Keyboard);
                EnsureTriggerKey(PlayerInput.Triggers.Current, keybind.HostId);
                EnsureTriggerKey(PlayerInput.Triggers.Old, keybind.HostId);
                EnsureTriggerKey(PlayerInput.Triggers.JustPressed, keybind.HostId);
                EnsureTriggerKey(PlayerInput.Triggers.JustReleased, keybind.HostId);
            }
            nativeIds.Clear(); nativeIds.UnionWith(activeIds); nativeProfile = profile; nativeRegistryVersion = snapshot.Version;
        }
        catch (Exception exception) { reportFailure("Plugin keybind state synchronization", exception); }
    }

    private void EnsureBinding(PluginKeybindRegistration keybind, InputMode mode)
    {
        KeyConfiguration configuration = PlayerInput.CurrentProfile.InputModes[mode];
        if (!configuration.KeyStatus.ContainsKey(keybind.HostId)) configuration.KeyStatus.Add(keybind.HostId, persistence.GetBindings(keybind, mode, PlayerInput.CurrentProfile?.Name));
    }

    private void ObserveBinding(PluginKeybindRegistration keybind, InputMode mode, IReadOnlyList<string> bindings) => persistence.Observe(keybind, mode, PlayerInput.CurrentProfile?.Name, bindings);

    private void ReportInvocation(PluginKeybindRegistration keybind, bool invoked, Exception failure)
    {
        if (invoked || failure == null) return;
        notifications.Publish("Plugin keybind failed: " + keybind.Heading, TimeSpan.FromSeconds(4));
        reportFailure("Plugin keybind " + keybind.HostId, failure);
    }

    private bool IsDown(PluginKeybindRegistration keybind, KeyboardState keyboard)
    {
        KeyConfiguration configuration = PlayerInput.CurrentProfile == null ? null : PlayerInput.CurrentProfile.InputModes[InputMode.Keyboard];
        if (configuration == null) return false;
        EnsureBinding(keybind, InputMode.Keyboard);
        IReadOnlyList<string> bindings = configuration.KeyStatus[keybind.HostId];
        for (int index = 0; index < bindings.Count; index++)
            if (Enum.TryParse(bindings[index], true, out Keys key) && keyboard.IsKeyDown(key)) return true;
        return false;
    }

    private void RemoveStaleState(IReadOnlyList<PluginKeybindRegistration> keybinds, long version)
    {
        var active = new HashSet<string>(keybinds.Select(keybind => keybind.HostId), StringComparer.Ordinal);
        foreach (string stale in downState.Keys.Where(key => !active.Contains(key)).ToArray()) downState.Remove(stale);
        persistence.Prune(active); registryVersion = version;
    }

    private void RemoveStaleTriggerKeys(ISet<string> active)
    {
        foreach (string id in nativeIds.Where(id => !active.Contains(id)).ToArray())
        {
            PlayerInput.Triggers.Current.KeyStatus.Remove(id); PlayerInput.Triggers.Old.KeyStatus.Remove(id);
            PlayerInput.Triggers.JustPressed.KeyStatus.Remove(id); PlayerInput.Triggers.JustReleased.KeyStatus.Remove(id);
        }
    }

    private static void EnsureTriggerKey(TriggersSet triggers, string key) { if (!triggers.KeyStatus.ContainsKey(key)) triggers.KeyStatus.Add(key, false); }
}
