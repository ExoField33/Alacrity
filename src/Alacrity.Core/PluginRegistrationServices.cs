using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host registration helpers for non-gameplay plugin extension points.</summary>
public sealed class PluginExtensionHost
{
    private readonly object gate = new object();
    private readonly Dictionary<Type, List<EventHandlerRegistration>> eventHandlers = new Dictionary<Type, List<EventHandlerRegistration>>();
    private readonly Dictionary<string, OwnedKeybind> keybinds = new Dictionary<string, OwnedKeybind>(StringComparer.Ordinal);
    private IReadOnlyList<PluginKeybindRegistration> keybindSnapshot = Array.Empty<PluginKeybindRegistration>();
    private bool keybindSnapshotDirty = true;
    private long keybindVersion;
    private long nextKeybindSequence;
    private readonly List<OwnedUiContribution> settingsPages = new List<OwnedUiContribution>();
    private readonly List<OwnedSettingControl> settingsControls = new List<OwnedSettingControl>();
    private readonly List<OwnedUiContribution> overlays = new List<OwnedUiContribution>();
    private readonly List<OwnedIconInteraction> iconInteractions = new List<OwnedIconInteraction>();
    private readonly Dictionary<string, DateTime> eventFailureTimes = new Dictionary<string, DateTime>(StringComparer.Ordinal);

    /// <summary>Creates scope-owned extension services associated with one verified plugin manifest.</summary>
    public PluginExtensionServices CreateServices(PluginManifest manifest, IPluginResourceScope resources, IPluginLogger? logger = null)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        manifest.Validate();
        EnsureOwner(manifest.Id);
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new PluginExtensionServices(new EventService(this, manifest.Id, resources, logger), new UiService(this, manifest, manifest.Id, resources), new KeybindService(this, manifest, manifest.Id, resources));
    }

    /// <summary>Creates scope-owned extension services for a validated plugin identity.</summary>
    public PluginExtensionServices CreateServices(PluginId owner, IPluginResourceScope resources)
    {
        EnsureOwner(owner);
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new PluginExtensionServices(new EventService(this, owner, resources, null), new UiService(this, null, owner, resources), new KeybindService(this, null, owner, resources));
    }

    /// <summary>Returns settings-page contributions still owned by the specified active plugin.</summary>
    public IReadOnlyList<PluginUiContribution> GetSettingsPages(PluginId pluginId)
    {
        lock (gate)
            return settingsPages.Where(page => page.Owner == pluginId).Select(page => page.Contribution).ToArray();
    }

    /// <summary>Returns typed setting controls still owned by the specified active plugin.</summary>
    public IReadOnlyList<PluginSettingControl> GetSettingsControls(PluginId pluginId)
    {
        lock (gate)
            return settingsControls.Where(control => control.Owner == pluginId).Select(control => control.Control).ToArray();
    }

    /// <summary>Returns only controls belonging to one verified owner-local settings page.</summary>
    public IReadOnlyList<PluginSettingControl> GetSettingsControls(PluginId pluginId, string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId)) throw new ArgumentException("A settings page ID is required.", nameof(pageId));
        lock (gate)
            return settingsControls.Where(control => control.Owner == pluginId && string.Equals(control.PageId, pageId, StringComparison.Ordinal)).Select(control => control.Control).ToArray();
    }

    /// <summary>Returns overlay contributions still owned by the specified active plugin.</summary>
    public IReadOnlyList<PluginUiContribution> GetOverlays(PluginId pluginId)
    {
        lock (gate)
            return overlays.Where(overlay => overlay.Owner == pluginId).Select(overlay => overlay.Contribution).ToArray();
    }

    /// <summary>Resolves one scoped icon interaction without exposing host input to plugin code.</summary>
    public PluginIconInteractionState EvaluateIconInteraction(PluginId owner, string id, PluginUiRect bounds, float pointerX, float pointerY)
    {
        if (!owner.IsValid || string.IsNullOrWhiteSpace(id)) return default;
        lock (gate)
        {
            OwnedIconInteraction? entry = iconInteractions.FirstOrDefault(candidate => candidate.Owner == owner && string.Equals(candidate.Descriptor.Id, id, StringComparison.Ordinal));
            if (entry == null) return default;
            bool hovered = bounds.Contains(pointerX, pointerY);
            PluginIconInteractionDescriptor descriptor = entry.Descriptor;
            bool expand = descriptor.HoverEffect == PluginIconHoverEffect.Expand || descriptor.HoverEffect == PluginIconHoverEffect.HighlightAndExpand;
            bool highlight = descriptor.HoverEffect == PluginIconHoverEffect.Highlight || descriptor.HoverEffect == PluginIconHoverEffect.HighlightAndExpand;
            PluginTooltipOptions? tooltip = null;
            if (hovered)
            {
                try
                {
                    tooltip = descriptor.TooltipProvider == null ? descriptor.Tooltip : descriptor.TooltipProvider() ?? descriptor.Tooltip;
                }
                catch (Exception exception)
                {
                    Trace.TraceError("Alacrity tooltip provider for icon interaction '" + descriptor.Id + "' failed for plugin '" + owner.Value + "': " + exception);
                }
            }

            return new PluginIconInteractionState(true, hovered, hovered && expand ? descriptor.HoverScale : 1f, hovered && highlight ? descriptor.HoverColor : descriptor.NormalColor, tooltip);
        }
    }

    /// <summary>Consumes one host-confirmed click and invokes only the matching scoped icon action.</summary>
    public bool TryActivateIconInteraction(PluginId owner, string id)
    {
        OwnedIconInteraction? entry;
        lock (gate) entry = iconInteractions.FirstOrDefault(candidate => candidate.Owner == owner && string.Equals(candidate.Descriptor.Id, id, StringComparison.Ordinal));
        if (entry == null) return false;
        try
        {
            entry.Activate();
            return true;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Alacrity icon interaction '" + entry.Descriptor.Id + "' failed for plugin '" + owner.Value + "': " + exception);
            return false;
        }
    }

    /// <summary>Returns active keybind rows in deterministic plugin and registration order for the Terraria controls adapter.</summary>
    public IReadOnlyList<PluginKeybindRegistration> GetKeybinds()
    {
        return GetKeybindSnapshot().Registrations;
    }

    /// <summary>Returns registrations and their version from one lock-protected immutable snapshot.</summary>
    public PluginKeybindRegistrySnapshot GetKeybindSnapshot()
    {
        lock (gate)
        {
            if (keybindSnapshotDirty)
            {
                keybindSnapshot = Array.AsReadOnly(keybinds.Values
                    .OrderBy(keybind => keybind.Owner.Value, StringComparer.Ordinal)
                    .ThenBy(keybind => keybind.Sequence)
                    .Select(keybind => new PluginKeybindRegistration(keybind.Owner, keybind.Heading, keybind.Descriptor, keybind.Sequence))
                    .ToArray());
                keybindSnapshotDirty = false;
            }

            return new PluginKeybindRegistrySnapshot(keybindVersion, keybindSnapshot);
        }
    }

    /// <summary>Changes whenever the host-owned keybind registry gains or loses a registration.</summary>
    public long KeybindVersion
    {
        get
        {
            lock (gate)
                return keybindVersion;
        }
    }

    /// <summary>
    /// Invokes a registered keybind by its host-qualified ID. Input adapters call this only after
    /// they have observed a fresh user key press; plugin code cannot invoke another plugin's binding.
    /// </summary>
    public bool TryInvokeKeybind(string hostId, out Exception? failure)
    {
        if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("A host keybind ID is required.", nameof(hostId));

        OwnedKeybind? keybind;
        lock (gate)
            keybinds.TryGetValue(hostId, out keybind);

        if (keybind == null)
        {
            failure = null;
            return false;
        }

        try
        {
            keybind.PressHandler?.Invoke();
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }
    }

    /// <summary>Delivers a state transition to a registered held keybind.</summary>
    public bool TrySetKeybindState(string hostId, bool isDown, out Exception? failure)
    {
        if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("A host keybind ID is required.", nameof(hostId));

        OwnedKeybind? keybind;
        lock (gate)
            keybinds.TryGetValue(hostId, out keybind);
        if (keybind == null)
        {
            failure = null;
            return false;
        }

        try
        {
            keybind.StateHandler?.Invoke(isDown);
            failure = null;
            return keybind.StateHandler != null;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }
    }

    /// <summary>Publishes an immutable host event snapshot to current subscribers.</summary>
    public void Publish<TEvent>(TEvent snapshot)
    {
        EventHandlerRegistration[] handlers;
        lock (gate)
            handlers = eventHandlers.TryGetValue(typeof(TEvent), out var current) ? current.ToArray() : Array.Empty<EventHandlerRegistration>();
        foreach (var handler in handlers)
        {
            try
            {
                handler.Invoke(snapshot);
            }
            catch (Exception exception)
            {
                ReportEventFailure(handler, exception);
            }
            finally
            {
                if (handler.Once) handler.Dispose();
            }
        }
    }

    private IPluginRegistration Subscribe<TEvent>(PluginId owner, IPluginResourceScope resources, Action<TEvent> handler, PluginEventOptions? options, IPluginLogger? logger)
    {
        EnsureOwner(owner);
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var registration = new EventHandlerRegistration(owner, typeof(TEvent), value => handler((TEvent)value!), options?.Once == true, RemoveEvent, logger);
        lock (gate)
        {
            if (!eventHandlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = new List<EventHandlerRegistration>();
                eventHandlers.Add(typeof(TEvent), handlers);
            }
            handlers.Add(registration);
        }
        return Own(resources, registration, PluginResourceKind.EventSubscription);
    }

    private IPluginRegistration RegisterUi(PluginId owner, IPluginResourceScope resources, PluginUiContribution contribution, bool overlay)
    {
        EnsureOwner(owner);
        if (contribution == null) throw new ArgumentNullException(nameof(contribution));
        var entry = new OwnedUiContribution(owner, contribution);
        lock (gate)
        {
            List<OwnedUiContribution> target = overlay ? overlays : settingsPages;
            if (target.Any(candidate => candidate.Owner == owner && string.Equals(candidate.Contribution.Id, contribution.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("The plugin already registered " + (overlay ? "overlay" : "settings page") + " '" + contribution.Id + "'.");
            target.Add(entry);
        }
        var registration = new CallbackRegistration("ui:" + contribution.Id, () => { lock (gate) (overlay ? overlays : settingsPages).Remove(entry); });
        return Own(resources, registration, PluginResourceKind.UserInterface);
    }

    private IPluginRegistration RegisterSettingControl(PluginId owner, IPluginResourceScope resources, PluginSettingControl control)
    {
        EnsureOwner(owner);
        if (control == null) throw new ArgumentNullException(nameof(control));
        string pageId = control.PageId ?? ResolveLegacySettingsPage(owner);
        var entry = new OwnedSettingControl(owner, pageId, control);
        lock (gate)
        {
            if (settingsControls.Any(candidate => candidate.Owner == owner && string.Equals(candidate.Control.Id, control.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("The plugin already registered settings control '" + control.Id + "'.");
            settingsControls.Add(entry);
        }
        var registration = new CallbackRegistration("setting:" + control.Id, () => { lock (gate) settingsControls.Remove(entry); });
        return Own(resources, registration, PluginResourceKind.UserInterface);
    }

    private IPluginRegistration RegisterIconInteraction(PluginId owner, IPluginResourceScope resources, PluginIconInteractionDescriptor descriptor, Action activate)
    {
        EnsureOwner(owner);
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (activate == null) throw new ArgumentNullException(nameof(activate));
        var entry = new OwnedIconInteraction(owner, descriptor, activate);
        lock (gate)
        {
            if (iconInteractions.Any(candidate => candidate.Owner == owner && string.Equals(candidate.Descriptor.Id, descriptor.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("The plugin already registered icon interaction '" + descriptor.Id + "'.");
            iconInteractions.Add(entry);
        }
        var registration = new CallbackRegistration("icon:" + descriptor.Id, () => { lock (gate) iconInteractions.Remove(entry); });
        return Own(resources, registration, PluginResourceKind.UserInterface);
    }

    private string ResolveLegacySettingsPage(PluginId owner)
    {
        lock (gate)
        {
            OwnedUiContribution[] pages = settingsPages.Where(page => page.Owner == owner).ToArray();
            if (pages.Length > 0) return pages[0].Contribution.Id;
            return "legacy";
        }
    }

    private IPluginRegistration RegisterKeybind(PluginId owner, string heading, IPluginResourceScope resources, PluginKeybindDescriptor descriptor, Action? handler, Action<bool>? stateHandler)
    {
        EnsureOwner(owner);
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null && stateHandler == null) throw new ArgumentException("A keybind handler is required.");
        if (descriptor.Activation == PluginKeybindActivation.Hold && stateHandler == null) throw new ArgumentException("Held keybinds require a state handler.", nameof(stateHandler));
        if (descriptor.Activation == PluginKeybindActivation.Press && handler == null) throw new ArgumentException("Press keybinds require a press handler.", nameof(handler));
        lock (gate)
        {
            string hostId = GetHostKeybindId(owner, descriptor);
            if (keybinds.ContainsKey(hostId)) throw new InvalidOperationException("A keybind with this ID is already registered by this plugin: " + descriptor.Id);
            keybinds.Add(hostId, new OwnedKeybind(owner, heading, descriptor, handler, stateHandler, nextKeybindSequence++));
            keybindSnapshotDirty = true;
            keybindVersion++;
        }
        string registeredHostId = GetHostKeybindId(owner, descriptor);
        var registration = new CallbackRegistration("keybind:" + registeredHostId, () =>
        {
            lock (gate)
            {
                if (keybinds.Remove(registeredHostId))
                {
                    keybindSnapshotDirty = true;
                    keybindVersion++;
                }
            }
        });
        return Own(resources, registration, PluginResourceKind.Keybind);
    }

    private static string GetHostKeybindId(PluginId owner, PluginKeybindDescriptor descriptor)
    {
        return owner.Value + "." + descriptor.Id;
    }

    private void RemoveEvent(EventHandlerRegistration registration)
    {
        lock (gate)
            if (eventHandlers.TryGetValue(registration.EventType, out var handlers)) handlers.Remove(registration);
    }

    private void ReportEventFailure(EventHandlerRegistration registration, Exception exception)
    {
        string key = registration.Owner.Value + ":" + registration.EventType.FullName;
        lock (gate)
        {
            DateTime now = DateTime.UtcNow;
            if (eventFailureTimes.TryGetValue(key, out DateTime previous) && now - previous < TimeSpan.FromSeconds(10)) return;
            eventFailureTimes[key] = now;
        }
        if (registration.Logger != null)
            registration.Logger.Error("Event '" + registration.EventType.FullName + "' failed for plugin '" + registration.Owner.Value + "'.", exception);
        else
            Trace.TraceError("Alacrity event '" + registration.EventType.FullName + "' failed for plugin '" + registration.Owner.Value + "': " + exception);
    }

    private static IPluginRegistration Own(IPluginResourceScope resources, IPluginRegistration registration, PluginResourceKind kind)
    {
        resources.Own(registration.Name, kind, registration);
        return registration;
    }

    private static void EnsureOwner(PluginId owner)
    {
        if (!owner.IsValid)
            throw new ArgumentException("A valid owning plugin ID is required.", nameof(owner));
    }

    /// <summary>Scoped general extension services.</summary>
    public sealed class PluginExtensionServices
    {
        internal PluginExtensionServices(IPluginEventService events, IPluginUiService ui, IPluginKeybindService keybinds) { Events = events; Ui = ui; Keybinds = keybinds; }
        public IPluginEventService Events { get; }
        public IPluginUiService Ui { get; }
        public IPluginKeybindService Keybinds { get; }
    }

    private sealed class EventService : IPluginEventService
    {
        private readonly PluginExtensionHost host; private readonly PluginId owner; private readonly IPluginResourceScope resources; private readonly IPluginLogger? logger;
        public EventService(PluginExtensionHost host, PluginId owner, IPluginResourceScope resources, IPluginLogger? logger) { this.host = host; this.owner = owner; this.resources = resources; this.logger = logger; }
        public IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null) => host.Subscribe(owner, resources, handler, options, logger);
    }
    private sealed class UiService : IPluginUiService
    {
        private readonly PluginExtensionHost host; private readonly PluginManifest? manifest; private readonly PluginId owner; private readonly IPluginResourceScope resources;
        public UiService(PluginExtensionHost host, PluginManifest? manifest, PluginId owner, IPluginResourceScope resources) { this.host = host; this.manifest = manifest; this.owner = owner; this.resources = resources; }
        public IPluginRegistration RegisterSettingsPage(PluginUiContribution contribution) { EnsureUiAccess(); return host.RegisterUi(owner, resources, contribution, false); }
        public IPluginRegistration RegisterSettingsControl(PluginUiContribution contribution)
        {
            if (contribution == null) throw new ArgumentNullException(nameof(contribution));
            if (!contribution.IsInteractive) throw new ArgumentException("A settings control must provide a value reader and activation action.", nameof(contribution));
            EnsureUiAccess();
            return host.RegisterUi(owner, resources, contribution, false);
        }
        public IPluginRegistration RegisterSettingsControl(PluginSettingControl control) { EnsureUiAccess(); return host.RegisterSettingControl(owner, resources, control); }
        public IPluginRegistration RegisterIconInteraction(PluginIconInteractionDescriptor descriptor, Action activate) { EnsureUiAccess(); return host.RegisterIconInteraction(owner, resources, descriptor, activate); }
        public IPluginRegistration RegisterOverlay(PluginUiContribution contribution) { EnsureUiAccess(); return host.RegisterUi(owner, resources, contribution, true); }
        private void EnsureUiAccess()
        {
            if (manifest == null || (manifest.Capabilities & PluginCapability.UserInterface) == 0 || (manifest.Permissions & PluginPermission.DrawUserInterface) == 0)
                throw new UnauthorizedAccessException("UI registrations require declared UserInterface capability and DrawUserInterface permission.");
        }
    }
    private sealed class KeybindService : IPluginKeybindService
    {
        private readonly PluginExtensionHost host; private readonly PluginManifest? manifest; private readonly PluginId owner; private readonly IPluginResourceScope resources;
        public KeybindService(PluginExtensionHost host, PluginManifest? manifest, PluginId owner, IPluginResourceScope resources) { this.host = host; this.manifest = manifest; this.owner = owner; this.resources = resources; }
        public IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action handler)
        {
            if (manifest == null || (manifest.Capabilities & PluginCapability.Input) == 0)
                throw new UnauthorizedAccessException("Keybind registrations require the Input capability.");
            return host.RegisterKeybind(owner, manifest.Name, resources, descriptor, handler, null);
        }

        public IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action<bool> stateHandler)
        {
            if (manifest == null || (manifest.Capabilities & PluginCapability.Input) == 0)
                throw new UnauthorizedAccessException("Keybind registrations require the Input capability.");
            return host.RegisterKeybind(owner, manifest.Name, resources, descriptor, null, stateHandler);
        }
    }
    private sealed class EventHandlerRegistration : CallbackRegistration
    {
        private readonly Action<object?> handler;
        public EventHandlerRegistration(PluginId owner, Type eventType, Action<object?> handler, bool once, Action<EventHandlerRegistration> remove, IPluginLogger? logger) : base("event:" + owner.Value + ":" + eventType.FullName, () => { }) { Owner = owner; EventType = eventType; this.handler = handler; Once = once; Remove = remove; Logger = logger; }
        public PluginId Owner { get; } public Type EventType { get; } public bool Once { get; } public IPluginLogger? Logger { get; } private Action<EventHandlerRegistration> Remove { get; }
        public void Invoke(object? value) => handler(value);
        public override void Dispose() { if (IsReleased) return; base.Dispose(); Remove(this); }
    }
    private class CallbackRegistration : IPluginRegistration
    {
        private readonly Action release; private bool released;
        public CallbackRegistration(string name, Action release) { Name = name; this.release = release; }
        public string Name { get; } public bool IsReleased => released;
        public virtual void Dispose() { if (released) return; released = true; release(); }
    }

    private sealed class OwnedUiContribution
    {
        public OwnedUiContribution(PluginId owner, PluginUiContribution contribution) { Owner = owner; Contribution = contribution; }
        public PluginId Owner { get; }
        public PluginUiContribution Contribution { get; }
    }
    private sealed class OwnedSettingControl
    {
        public OwnedSettingControl(PluginId owner, string pageId, PluginSettingControl control) { Owner = owner; PageId = pageId; Control = control; }
        public PluginId Owner { get; }
        public string PageId { get; }
        public PluginSettingControl Control { get; }
    }
    private sealed class OwnedIconInteraction
    {
        public OwnedIconInteraction(PluginId owner, PluginIconInteractionDescriptor descriptor, Action activate) { Owner = owner; Descriptor = descriptor; Activate = activate; }
        public PluginId Owner { get; } public PluginIconInteractionDescriptor Descriptor { get; } public Action Activate { get; }
    }
    private sealed class OwnedKeybind
    {
        public OwnedKeybind(PluginId owner, string heading, PluginKeybindDescriptor descriptor, Action? pressHandler, Action<bool>? stateHandler, long sequence) { Owner = owner; Heading = heading; Descriptor = descriptor; PressHandler = pressHandler; StateHandler = stateHandler; Sequence = sequence; }
        public PluginId Owner { get; }
        public string Heading { get; }
        public PluginKeybindDescriptor Descriptor { get; }
        public Action? PressHandler { get; }
        public Action<bool>? StateHandler { get; }
        public long Sequence { get; }
    }
}
