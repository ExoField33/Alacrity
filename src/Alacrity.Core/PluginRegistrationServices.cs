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
    private readonly List<PluginUiContribution> settingsPages = new List<PluginUiContribution>();
    private readonly List<PluginUiContribution> overlays = new List<PluginUiContribution>();

    /// <summary>Creates scoped UI, event, and keybind service facades owned by one enable scope.</summary>
    public PluginExtensionServices CreateServices(IPluginResourceScope resources)
    {
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new PluginExtensionServices(new EventService(this, resources), new UiService(this, resources), new KeybindService(this, resources));
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

    private IPluginRegistration RegisterUi(IPluginResourceScope resources, PluginUiContribution contribution, bool overlay)
    {
        if (contribution == null) throw new ArgumentNullException(nameof(contribution));
        var registration = new CallbackRegistration("ui:" + contribution.Id, () => { lock (gate) (overlay ? overlays : settingsPages).Remove(contribution); });
        lock (gate) (overlay ? overlays : settingsPages).Add(contribution);
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
        private readonly PluginExtensionHost host; private readonly IPluginResourceScope resources;
        public UiService(PluginExtensionHost host, IPluginResourceScope resources) { this.host = host; this.resources = resources; }
        public IPluginRegistration RegisterSettingsPage(PluginUiContribution contribution) => host.RegisterUi(resources, contribution, false);
        public IPluginRegistration RegisterOverlay(PluginUiContribution contribution) => host.RegisterUi(resources, contribution, true);
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
}
