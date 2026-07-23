using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host command registry with scope-owned registrations and explicit conflict rejection.</summary>
public sealed class PluginCommandHost
{
    private readonly object gate = new object();
    private readonly Dictionary<string, CommandRegistration> commands = new Dictionary<string, CommandRegistration>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a command registration service for one plugin resource scope.</summary>
    public IPluginCommandService CreateService(IPluginResourceScope resources)
    {
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new ScopedService(this, resources);
    }

    /// <summary>Dispatches a parsed command invocation; returns false when no command is registered.</summary>
    public bool TryInvoke(string id, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        CommandRegistration? registration;
        lock (gate) commands.TryGetValue(id, out registration);
        if (registration == null) return false;
        registration.Invoke(new PluginCommandInvocation(arguments ?? Array.Empty<string>()));
        return true;
    }

    private IPluginRegistration Register(IPluginResourceScope resources, PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        CommandRegistration registration;
        lock (gate)
        {
            if (commands.ContainsKey(descriptor.Id)) throw new InvalidOperationException("A command with this ID is already registered: " + descriptor.Id);
            registration = new CommandRegistration(descriptor, handler, Remove);
            commands.Add(descriptor.Id, registration);
        }
        try { resources.Own(registration.Name, PluginResourceKind.Command, registration); return registration; }
        catch { registration.Dispose(); throw; }
    }

    private void Remove(CommandRegistration registration)
    {
        lock (gate)
            if (commands.TryGetValue(registration.Descriptor.Id, out var current) && ReferenceEquals(current, registration)) commands.Remove(registration.Descriptor.Id);
    }

    private sealed class ScopedService : IPluginCommandService
    {
        private readonly PluginCommandHost host; private readonly IPluginResourceScope resources;
        public ScopedService(PluginCommandHost host, IPluginResourceScope resources) { this.host = host; this.resources = resources; }
        public IPluginRegistration Register(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler) => host.Register(resources, descriptor, handler);
    }
    private sealed class CommandRegistration : IPluginRegistration
    {
        private readonly Action<CommandRegistration> remove; private bool released;
        public CommandRegistration(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler, Action<CommandRegistration> remove) { Descriptor = descriptor; Handler = handler; this.remove = remove; }
        public PluginCommandDescriptor Descriptor { get; } public Action<PluginCommandInvocation> Handler { get; }
        public string Name => "command:" + Descriptor.Id; public bool IsReleased => released;
        public void Invoke(PluginCommandInvocation invocation) => Handler(invocation);
        public void Dispose() { if (released) return; released = true; remove(this); }
    }
}
