using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host registration helpers for non-gameplay plugin extension points.</summary>
public sealed class PluginExtensionHost
{
    private readonly object gate = new object();
    private readonly Dictionary<Type, List<EventHandlerRegistration>> eventHandlers = new Dictionary<Type, List<EventHandlerRegistration>>();
    private readonly Dictionary<string, PluginKeybindDescriptor> keybinds = new Dictionary<string, PluginKeybindDescriptor>(StringComparer.Ordinal);
    private readonly List<OwnedUiContribution> settingsPages = new List<OwnedUiContribution>();
    private readonly List<OwnedSettingControl> settingsControls = new List<OwnedSettingControl>();
    private readonly List<OwnedUiContribution> overlays = new List<OwnedUiContribution>();

    /// <summary>Creates scoped UI, event, and keybind service facades owned by one enable scope.</summary>
    public PluginExtensionServices CreateServices(IPluginResourceScope resources)
    {
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return CreateServices(default(PluginId), resources);
    }

    /// <summary>Creates scope-owned extension services associated with one verified plugin manifest.</summary>
    public PluginExtensionServices CreateServices(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return CreateServices(manifest.Id, resources);
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

    /// <summary>Publishes an immutable host event snapshot to current subscribers.</summary>
    public void Publish<TEvent>(TEvent snapshot)
    {
        EventHandlerRegistration[] handlers;
        lock (gate)
            handlers = eventHandlers.TryGetValue(typeof(TEvent), out var current) ? current.ToArray() : Array.Empty<EventHandlerRegistration>();
        foreach (var handler in handlers)
        {
            handler.Invoke(snapshot);
            if (handler.Once) handler.Dispose();
        }
    }

    private IPluginRegistration Subscribe<TEvent>(IPluginResourceScope resources, Action<TEvent> handler, PluginEventOptions? options)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var registration = new EventHandlerRegistration(typeof(TEvent), value => handler((TEvent)value!), options?.Once == true, RemoveEvent);
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

    private PluginExtensionServices CreateServices(PluginId owner, IPluginResourceScope resources)
    {
        return new PluginExtensionServices(new EventService(this, resources), new UiService(this, owner, resources), new KeybindService(this, resources));
    }

    private IPluginRegistration RegisterUi(PluginId owner, IPluginResourceScope resources, PluginUiContribution contribution, bool overlay)
    {
        if (contribution == null) throw new ArgumentNullException(nameof(contribution));
        var entry = new OwnedUiContribution(owner, contribution);
        var registration = new CallbackRegistration("ui:" + contribution.Id, () => { lock (gate) (overlay ? overlays : settingsPages).Remove(entry); });
        lock (gate) (overlay ? overlays : settingsPages).Add(entry);
        return Own(resources, registration, PluginResourceKind.UserInterface);
    }

    private IPluginRegistration RegisterSettingControl(PluginId owner, IPluginResourceScope resources, PluginSettingControl control)
    {
        if (control == null) throw new ArgumentNullException(nameof(control));
        var entry = new OwnedSettingControl(owner, control);
        var registration = new CallbackRegistration("setting:" + control.Id, () => { lock (gate) settingsControls.Remove(entry); });
        lock (gate) settingsControls.Add(entry);
        return Own(resources, registration, PluginResourceKind.UserInterface);
    }

    private IPluginRegistration RegisterKeybind(IPluginResourceScope resources, PluginKeybindDescriptor descriptor, Action handler)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        lock (gate)
        {
            if (keybinds.ContainsKey(descriptor.Id)) throw new InvalidOperationException("A keybind with this ID is already registered: " + descriptor.Id);
            keybinds.Add(descriptor.Id, descriptor);
        }
        var registration = new CallbackRegistration("keybind:" + descriptor.Id, () => { lock (gate) keybinds.Remove(descriptor.Id); });
        return Own(resources, registration, PluginResourceKind.Keybind);
    }

    private void RemoveEvent(EventHandlerRegistration registration)
    {
        lock (gate)
            if (eventHandlers.TryGetValue(registration.EventType, out var handlers)) handlers.Remove(registration);
    }

    private static IPluginRegistration Own(IPluginResourceScope resources, IPluginRegistration registration, PluginResourceKind kind)
    {
        resources.Own(registration.Name, kind, registration);
        return registration;
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
        private readonly PluginExtensionHost host; private readonly IPluginResourceScope resources;
        public EventService(PluginExtensionHost host, IPluginResourceScope resources) { this.host = host; this.resources = resources; }
        public IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null) => host.Subscribe(resources, handler, options);
    }
    private sealed class UiService : IPluginUiService
    {
        private readonly PluginExtensionHost host; private readonly PluginId owner; private readonly IPluginResourceScope resources;
        public UiService(PluginExtensionHost host, PluginId owner, IPluginResourceScope resources) { this.host = host; this.owner = owner; this.resources = resources; }
        public IPluginRegistration RegisterSettingsPage(PluginUiContribution contribution) => host.RegisterUi(owner, resources, contribution, false);
        public IPluginRegistration RegisterSettingsControl(PluginUiContribution contribution)
        {
            if (contribution == null) throw new ArgumentNullException(nameof(contribution));
            if (!contribution.IsInteractive) throw new ArgumentException("A settings control must provide a value reader and activation action.", nameof(contribution));
            return host.RegisterUi(owner, resources, contribution, false);
        }
        public IPluginRegistration RegisterSettingsControl(PluginSettingControl control) => host.RegisterSettingControl(owner, resources, control);
        public IPluginRegistration RegisterOverlay(PluginUiContribution contribution) => host.RegisterUi(owner, resources, contribution, true);
    }
    private sealed class KeybindService : IPluginKeybindService
    {
        private readonly PluginExtensionHost host; private readonly IPluginResourceScope resources;
        public KeybindService(PluginExtensionHost host, IPluginResourceScope resources) { this.host = host; this.resources = resources; }
        public IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action handler) => host.RegisterKeybind(resources, descriptor, handler);
    }
    private sealed class EventHandlerRegistration : CallbackRegistration
    {
        private readonly Action<object?> handler;
        public EventHandlerRegistration(Type eventType, Action<object?> handler, bool once, Action<EventHandlerRegistration> remove) : base("event:" + eventType.FullName, () => { }) { EventType = eventType; this.handler = handler; Once = once; Remove = remove; }
        public Type EventType { get; } public bool Once { get; } private Action<EventHandlerRegistration> Remove { get; }
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
        public OwnedSettingControl(PluginId owner, PluginSettingControl control) { Owner = owner; Control = control; }
        public PluginId Owner { get; }
        public PluginSettingControl Control { get; }
    }
}
