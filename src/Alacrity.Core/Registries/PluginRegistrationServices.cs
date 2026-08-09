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
        ScopeGuard guard = CreateScopeGuard(resources);
        ActivationCallbackGate? callbackGate = ActivationCallbackGates.TryGet(resources);
        return new PluginExtensionServices(new EventService(this, manifest.Id, resources, logger, guard, callbackGate), new UiService(this, manifest, manifest.Id, resources, guard, callbackGate), new KeybindService(this, manifest, manifest.Id, resources, guard, callbackGate));
    }

    /// <summary>Creates scope-owned extension services for a validated plugin identity.</summary>
    public PluginExtensionServices CreateServices(PluginId owner, IPluginResourceScope resources)
    {
        EnsureOwner(owner);
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        ScopeGuard guard = CreateScopeGuard(resources);
        ActivationCallbackGate? callbackGate = ActivationCallbackGates.TryGet(resources);
        return new PluginExtensionServices(new EventService(this, owner, resources, null, guard, callbackGate), new UiService(this, null, owner, resources, guard, callbackGate), new KeybindService(this, null, owner, resources, guard, callbackGate));
    }

    /// <summary>Returns settings-page contributions still owned by the specified active plugin.</summary>
    public IReadOnlyList<PluginUiContribution> GetSettingsPages(PluginId pluginId)
    {
        lock (gate)
            return settingsPages.Where(page => page.Owner == pluginId && page.IsAdmissionOpen).Select(page => page.Contribution).ToArray();
    }

    /// <summary>Returns typed setting controls still owned by the specified active plugin.</summary>
    public IReadOnlyList<PluginSettingControl> GetSettingsControls(PluginId pluginId)
    {
        lock (gate)
            return settingsControls.Where(control => control.Owner == pluginId && control.IsAdmissionOpen).Select(control => control.Control).ToArray();
    }

    /// <summary>Returns only controls belonging to one verified owner-local settings page.</summary>
    public IReadOnlyList<PluginSettingControl> GetSettingsControls(PluginId pluginId, string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId)) throw new ArgumentException("A settings page ID is required.", nameof(pageId));
        lock (gate)
            return settingsControls.Where(control => control.Owner == pluginId && control.IsAdmissionOpen && string.Equals(control.PageId, pageId, StringComparison.Ordinal)).Select(control => control.Control).ToArray();
    }

    /// <summary>Returns overlay contributions still owned by the specified active plugin.</summary>
    public IReadOnlyList<PluginUiContribution> GetOverlays(PluginId pluginId)
    {
        lock (gate)
            return overlays.Where(overlay => overlay.Owner == pluginId && overlay.IsAdmissionOpen).Select(overlay => overlay.Contribution).ToArray();
    }

    /// <summary>Resolves one scoped icon interaction without exposing host input to plugin code.</summary>
    public PluginIconInteractionState EvaluateIconInteraction(PluginId owner, string id, PluginUiRect bounds, float pointerX, float pointerY)
    {
        if (!owner.IsValid || string.IsNullOrWhiteSpace(id)) return default;
        OwnedIconInteraction? interaction;
        PluginIconInteractionDescriptor? descriptor;
        bool hovered;
        lock (gate)
        {
            OwnedIconInteraction? entry = iconInteractions.FirstOrDefault(candidate => candidate.Owner == owner && string.Equals(candidate.Descriptor.Id, id, StringComparison.Ordinal));
            if (entry == null) return default;
            interaction = entry;
            descriptor = entry.Descriptor;
            hovered = bounds.Contains(pointerX, pointerY);
        }
        bool expand = descriptor.HoverEffect == PluginIconHoverEffect.Expand || descriptor.HoverEffect == PluginIconHoverEffect.HighlightAndExpand;
        bool highlight = descriptor.HoverEffect == PluginIconHoverEffect.Highlight || descriptor.HoverEffect == PluginIconHoverEffect.HighlightAndExpand;
        PluginTooltipOptions? tooltip = null;
        if (hovered && interaction.TryEnter(out ActivationCallbackGate.Lease lease))
        {
            try
            {
                using (lease)
                {
                    tooltip = descriptor.TooltipProvider == null ? descriptor.Tooltip : descriptor.TooltipProvider() ?? descriptor.Tooltip;
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError("Alacrity tooltip provider for icon interaction '" + descriptor.Id + "' failed for plugin '" + owner.Value + "': " + exception);
            }
        }
        return new PluginIconInteractionState(true, hovered, hovered && expand ? descriptor.HoverScale : 1f, hovered && highlight ? descriptor.HoverColor : descriptor.NormalColor, tooltip);
    }

    /// <summary>Consumes one host-confirmed click and invokes only the matching scoped icon action.</summary>
    public bool TryActivateIconInteraction(PluginId owner, string id)
    {
        OwnedIconInteraction? entry;
        lock (gate) entry = iconInteractions.FirstOrDefault(candidate => candidate.Owner == owner && string.Equals(candidate.Descriptor.Id, id, StringComparison.Ordinal));
        if (entry == null) return false;
        try
        {
            if (!entry.TryEnter(out ActivationCallbackGate.Lease lease))
            {
                return false;
            }

            using (lease)
            {
                entry.Activate();
            }
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
            if (!keybind.TryEnter(out ActivationCallbackGate.Lease lease))
            {
                failure = null;
                return true;
            }

            using (lease)
            {
                keybind.PressHandler?.Invoke();
            }
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
            if (!keybind.TryEnter(out ActivationCallbackGate.Lease lease))
            {
                failure = null;
                return true;
            }

            using (lease)
            {
                keybind.StateHandler?.Invoke(isDown);
            }
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
            if (!handler.TryEnter(out ActivationCallbackGate.Lease lease))
            {
                continue;
            }

            try
            {
                using (lease)
                {
                    handler.Invoke(snapshot);
                }
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

    private IPluginRegistration Subscribe<TEvent>(PluginId owner, IPluginResourceScope resources, Action<TEvent> handler, PluginEventOptions? options, IPluginLogger? logger, ActivationCallbackGate? callbackGate)
    {
        EnsureOwner(owner);
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var registration = new EventHandlerRegistration(owner, typeof(TEvent), value => handler((TEvent)value!), options?.Once == true, RemoveEvent, logger, callbackGate);
        try
        {
            resources.Own("event:" + typeof(TEvent).FullName, PluginResourceKind.EventSubscription, registration);
        }
        catch
        {
            registration.Dispose();
            throw;
        }

        lock (gate)
        {
            if (registration.IsReleased || (callbackGate != null && callbackGate.IsClosed))
            {
                registration.Dispose();
                throw new ObjectDisposedException("IPluginResourceScope");
            }

            if (!eventHandlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = new List<EventHandlerRegistration>();
                eventHandlers.Add(typeof(TEvent), handlers);
            }

            handlers.Add(registration);
        }

        return registration;
    }

    private IPluginRegistration RegisterUi(PluginId owner, IPluginResourceScope resources, PluginUiContribution contribution, bool overlay)
    {
        EnsureOwner(owner);
        if (contribution == null) throw new ArgumentNullException(nameof(contribution));
        var entry = new OwnedUiContribution(owner, contribution, ActivationCallbackGates.TryGet(resources));
        var registration = new CallbackRegistration("ui:" + contribution.Id, () =>
        {
            lock (gate)
            {
                (overlay ? overlays : settingsPages).Remove(entry);
            }
        });

        Own(resources, registration, PluginResourceKind.UserInterface);
        lock (gate)
        {
            List<OwnedUiContribution> target = overlay ? overlays : settingsPages;
            if (target.Any(candidate => candidate.Owner == owner && string.Equals(candidate.Contribution.Id, contribution.Id, StringComparison.Ordinal)))
            {
                registration.Dispose();
                throw new InvalidOperationException("The plugin already registered " + (overlay ? "overlay" : "settings page") + " '" + contribution.Id + "'.");
            }

            if (registration.IsReleased || !entry.IsAdmissionOpen)
            {
                registration.Dispose();
                throw new ObjectDisposedException(nameof(IPluginResourceScope));
            }

            target.Add(entry);
        }
        return registration;
    }

    private IPluginRegistration RegisterSettingControl(PluginId owner, IPluginResourceScope resources, PluginSettingControl control)
    {
        EnsureOwner(owner);
        if (control == null) throw new ArgumentNullException(nameof(control));
        string pageId = control.PageId ?? ResolveLegacySettingsPage(owner);
        ActivationCallbackGate? callbackGate = ActivationCallbackGates.TryGet(resources);
        PluginSettingControl scopedControl = callbackGate == null
            ? control
            : control.WithAvailability(() => !callbackGate.IsClosed);
        var entry = new OwnedSettingControl(owner, pageId, scopedControl, callbackGate);
        var registration = new CallbackRegistration("setting:" + control.Id, () =>
        {
            lock (gate)
            {
                settingsControls.Remove(entry);
            }
        });

        Own(resources, registration, PluginResourceKind.UserInterface);
        lock (gate)
        {
            if (settingsControls.Any(candidate => candidate.Owner == owner && string.Equals(candidate.Control.Id, control.Id, StringComparison.Ordinal)))
            {
                registration.Dispose();
                throw new InvalidOperationException("The plugin already registered settings control '" + control.Id + "'.");
            }

            if (registration.IsReleased || !entry.IsAdmissionOpen)
            {
                registration.Dispose();
                throw new ObjectDisposedException(nameof(IPluginResourceScope));
            }

            settingsControls.Add(entry);
        }
        return registration;
    }

    private IPluginRegistration RegisterIconInteraction(PluginId owner, IPluginResourceScope resources, PluginIconInteractionDescriptor descriptor, Action activate)
    {
        EnsureOwner(owner);
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (activate == null) throw new ArgumentNullException(nameof(activate));
        var entry = new OwnedIconInteraction(owner, descriptor, activate, ActivationCallbackGates.TryGet(resources));
        var registration = new CallbackRegistration("icon:" + descriptor.Id, () =>
        {
            lock (gate)
            {
                iconInteractions.Remove(entry);
            }
        });

        Own(resources, registration, PluginResourceKind.UserInterface);
        lock (gate)
        {
            if (iconInteractions.Any(candidate => candidate.Owner == owner && string.Equals(candidate.Descriptor.Id, descriptor.Id, StringComparison.Ordinal)))
            {
                registration.Dispose();
                throw new InvalidOperationException("The plugin already registered icon interaction '" + descriptor.Id + "'.");
            }

            if (registration.IsReleased || !entry.IsAdmissionOpen)
            {
                registration.Dispose();
                throw new ObjectDisposedException(nameof(IPluginResourceScope));
            }

            iconInteractions.Add(entry);
        }
        return registration;
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
        Own(resources, registration, PluginResourceKind.Keybind);
        ActivationCallbackGate? callbackGate = ActivationCallbackGates.TryGet(resources);
        lock (gate)
        {
            if (keybinds.ContainsKey(registeredHostId))
            {
                registration.Dispose();
                throw new InvalidOperationException("A keybind with this ID is already registered by this plugin: " + descriptor.Id);
            }

            if (registration.IsReleased || (callbackGate != null && callbackGate.IsClosed))
            {
                registration.Dispose();
                throw new ObjectDisposedException(nameof(IPluginResourceScope));
            }

            keybinds.Add(registeredHostId, new OwnedKeybind(owner, heading, descriptor, handler, stateHandler, nextKeybindSequence++, callbackGate));
            keybindSnapshotDirty = true;
            keybindVersion++;
        }

        return registration;
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
        try { resources.Own(registration.Name, kind, registration); }
        catch { registration.Dispose(); throw; }
        return registration;
    }

    private static ScopeGuard CreateScopeGuard(IPluginResourceScope resources)
    {
        var guard = new ScopeGuard();
        try { resources.Own("extension-services", PluginResourceKind.UserInterface, guard); }
        catch { guard.Dispose(); throw; }
        return guard;
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
        private readonly PluginExtensionHost host; private readonly PluginId owner; private readonly IPluginResourceScope resources; private readonly IPluginLogger? logger; private readonly ScopeGuard guard; private readonly ActivationCallbackGate? callbackGate;
        public EventService(PluginExtensionHost host, PluginId owner, IPluginResourceScope resources, IPluginLogger? logger, ScopeGuard guard, ActivationCallbackGate? callbackGate) { this.host = host; this.owner = owner; this.resources = resources; this.logger = logger; this.guard = guard; this.callbackGate = callbackGate; }
        public IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null)
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginEventService", "The owning plugin scope has been released.");
            return host.Subscribe(owner, resources, handler, options, logger, callbackGate);
        }
    }
    private sealed class UiService : IPluginUiService
    {
        private readonly PluginExtensionHost host; private readonly PluginManifest? manifest; private readonly PluginId owner; private readonly IPluginResourceScope resources; private readonly ScopeGuard guard; private readonly ActivationCallbackGate? callbackGate;
        public UiService(PluginExtensionHost host, PluginManifest? manifest, PluginId owner, IPluginResourceScope resources, ScopeGuard guard, ActivationCallbackGate? callbackGate) { this.host = host; this.manifest = manifest; this.owner = owner; this.resources = resources; this.guard = guard; this.callbackGate = callbackGate; }
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
        [Obsolete("Use IPluginContext.Overlays for draw callbacks. This retained UI metadata API is compatibility-only.")]
        public IPluginRegistration RegisterOverlay(PluginUiContribution contribution) { EnsureUiAccess(); return host.RegisterUi(owner, resources, contribution, true); }
        private void EnsureUiAccess()
        {
            if (guard.IsReleased || (callbackGate != null && callbackGate.IsClosed)) throw new ObjectDisposedException("IPluginUiService", "The owning plugin activation has been released.");
            if (manifest == null || (manifest.Capabilities & PluginCapability.UserInterface) == 0 || (manifest.Permissions & PluginPermission.DrawUserInterface) == 0)
                throw new UnauthorizedAccessException("UI registrations require declared UserInterface capability and DrawUserInterface permission.");
        }
    }
    private sealed class KeybindService : IPluginKeybindService
    {
        private readonly PluginExtensionHost host; private readonly PluginManifest? manifest; private readonly PluginId owner; private readonly IPluginResourceScope resources; private readonly ScopeGuard guard; private readonly ActivationCallbackGate? callbackGate;
        public KeybindService(PluginExtensionHost host, PluginManifest? manifest, PluginId owner, IPluginResourceScope resources, ScopeGuard guard, ActivationCallbackGate? callbackGate) { this.host = host; this.manifest = manifest; this.owner = owner; this.resources = resources; this.guard = guard; this.callbackGate = callbackGate; }
        public IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action handler)
        {
            if (guard.IsReleased || (callbackGate != null && callbackGate.IsClosed)) throw new ObjectDisposedException("IPluginKeybindService", "The owning plugin activation has been released.");
            if (manifest == null || (manifest.Capabilities & PluginCapability.Input) == 0)
                throw new UnauthorizedAccessException("Keybind registrations require the Input capability.");
            return host.RegisterKeybind(owner, manifest.Name, resources, descriptor, handler, null);
        }

        public IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action<bool> stateHandler)
        {
            if (guard.IsReleased || (callbackGate != null && callbackGate.IsClosed)) throw new ObjectDisposedException("IPluginKeybindService", "The owning plugin activation has been released.");
            if (manifest == null || (manifest.Capabilities & PluginCapability.Input) == 0)
                throw new UnauthorizedAccessException("Keybind registrations require the Input capability.");
            return host.RegisterKeybind(owner, manifest.Name, resources, descriptor, null, stateHandler);
        }
    }
    private sealed class EventHandlerRegistration : CallbackRegistration
    {
        private readonly Action<object?> handler;
        private readonly ActivationCallbackGate? callbackGate;
        public EventHandlerRegistration(PluginId owner, Type eventType, Action<object?> handler, bool once, Action<EventHandlerRegistration> remove, IPluginLogger? logger, ActivationCallbackGate? callbackGate) : base("event:" + owner.Value + ":" + eventType.FullName, () => { }) { Owner = owner; EventType = eventType; this.handler = handler; Once = once; Remove = remove; Logger = logger; this.callbackGate = callbackGate; }
        public PluginId Owner { get; } public Type EventType { get; } public bool Once { get; } public IPluginLogger? Logger { get; } private Action<EventHandlerRegistration> Remove { get; }
        public void Invoke(object? value) => handler(value);
        public bool TryEnter(out ActivationCallbackGate.Lease lease)
        {
            if (IsReleased) { lease = default; return false; }
            if (callbackGate == null) { lease = default; return true; }
            return callbackGate.TryEnter(out lease);
        }
        public override void Dispose() { if (IsReleased) return; base.Dispose(); Remove(this); }
    }
    private class CallbackRegistration : IPluginRegistration
    {
        private readonly Action release; private bool released;
        public CallbackRegistration(string name, Action release) { Name = name; this.release = release; }
        public string Name { get; } public bool IsReleased => released;
        public virtual void Dispose() { if (released) return; released = true; release(); }
    }

    private sealed class ScopeGuard : IDisposable
    {
        private int released;
        internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;
        public void Dispose() { System.Threading.Interlocked.Exchange(ref released, 1); }
    }

    private sealed class OwnedUiContribution
    {
        private readonly ActivationCallbackGate? callbackGate;
        public OwnedUiContribution(PluginId owner, PluginUiContribution contribution, ActivationCallbackGate? callbackGate) { Owner = owner; Contribution = contribution; this.callbackGate = callbackGate; }
        public PluginId Owner { get; }
        public PluginUiContribution Contribution { get; }
        public bool IsAdmissionOpen => callbackGate == null || !callbackGate.IsClosed;
    }
    private sealed class OwnedSettingControl
    {
        private readonly ActivationCallbackGate? callbackGate;
        public OwnedSettingControl(PluginId owner, string pageId, PluginSettingControl control, ActivationCallbackGate? callbackGate) { Owner = owner; PageId = pageId; Control = control; this.callbackGate = callbackGate; }
        public PluginId Owner { get; }
        public string PageId { get; }
        public PluginSettingControl Control { get; }
        public bool IsAdmissionOpen => callbackGate == null || !callbackGate.IsClosed;
    }
    private sealed class OwnedIconInteraction
    {
        private readonly ActivationCallbackGate? callbackGate;
        public OwnedIconInteraction(PluginId owner, PluginIconInteractionDescriptor descriptor, Action activate, ActivationCallbackGate? callbackGate) { Owner = owner; Descriptor = descriptor; Activate = activate; this.callbackGate = callbackGate; }
        public PluginId Owner { get; } public PluginIconInteractionDescriptor Descriptor { get; } public Action Activate { get; }
        public bool TryEnter(out ActivationCallbackGate.Lease lease)
        {
            if (callbackGate == null) { lease = default; return true; }
            return callbackGate.TryEnter(out lease);
        }

        public bool IsAdmissionOpen => callbackGate == null || !callbackGate.IsClosed;
    }
    private sealed class OwnedKeybind
    {
        private readonly ActivationCallbackGate? callbackGate;
        public OwnedKeybind(PluginId owner, string heading, PluginKeybindDescriptor descriptor, Action? pressHandler, Action<bool>? stateHandler, long sequence, ActivationCallbackGate? callbackGate) { Owner = owner; Heading = heading; Descriptor = descriptor; PressHandler = pressHandler; StateHandler = stateHandler; Sequence = sequence; this.callbackGate = callbackGate; }
        public PluginId Owner { get; }
        public string Heading { get; }
        public PluginKeybindDescriptor Descriptor { get; }
        public Action? PressHandler { get; }
        public Action<bool>? StateHandler { get; }
        public long Sequence { get; }
        public bool TryEnter(out ActivationCallbackGate.Lease lease)
        {
            if (callbackGate == null) { lease = default; return true; }
            return callbackGate.TryEnter(out lease);
        }
    }
}
