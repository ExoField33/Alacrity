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
        return CreateService(null, resources, null);
    }

    /// <summary>Creates an activation-scoped command service with owner-attributed diagnostics.</summary>
    public IPluginCommandService CreateService(PluginManifest? manifest, IPluginResourceScope resources, IPluginLogger? logger)
    {
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        var guard = new ScopeGuard();
        try { resources.Own("commands", PluginResourceKind.Command, guard); }
        catch { guard.Dispose(); throw; }
        return new ScopedService(this, resources, guard, manifest?.Id ?? default, logger);
    }

    /// <summary>Dispatches a parsed command invocation with explicit consumed/failure semantics.</summary>
    public PluginCommandDispatchResult Dispatch(string id, IReadOnlyList<string> arguments, Action<string>? reply = null, IPluginLogger? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return PluginCommandDispatchResult.NotFound;
        CommandRegistration? registration;
        lock (gate) commands.TryGetValue(id, out registration);
        if (registration == null) return PluginCommandDispatchResult.NotFound;
        try
        {
            registration.Invoke(new PluginCommandInvocation(arguments ?? Array.Empty<string>(), reply));
            return PluginCommandDispatchResult.Handled;
        }
        catch (Exception exception)
        {
            (registration.Logger ?? diagnostics)?.Error("Plugin command '" + registration.Descriptor.Id + "' failed for plugin '" + registration.Owner.Value + "'.", exception);
            reply?.Invoke("Plugin command failed: " + registration.Descriptor.Id);
            return PluginCommandDispatchResult.HandledWithFailure;
        }
    }

    /// <summary>Compatibility wrapper; failures remain consumed and therefore return true.</summary>
    public bool TryInvoke(string id, IReadOnlyList<string> arguments, Action<string>? reply = null)
    {
        return Dispatch(id, arguments, reply) != PluginCommandDispatchResult.NotFound;
    }

    private IPluginRegistration Register(IPluginResourceScope resources, PluginId owner, IPluginLogger? logger, PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        CommandRegistration registration;
        lock (gate)
        {
            if (commands.ContainsKey(descriptor.Id)) throw new InvalidOperationException("A command with this ID is already registered: " + descriptor.Id);
            registration = new CommandRegistration(owner, logger, descriptor, handler, Remove);
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
        private readonly PluginCommandHost host; private readonly IPluginResourceScope resources; private readonly ScopeGuard guard; private readonly PluginId owner; private readonly IPluginLogger? logger;
        public ScopedService(PluginCommandHost host, IPluginResourceScope resources, ScopeGuard guard, PluginId owner, IPluginLogger? logger) { this.host = host; this.resources = resources; this.guard = guard; this.owner = owner; this.logger = logger; }
        public IPluginRegistration Register(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler)
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginCommandService", "The owning plugin scope has been released.");
            return host.Register(resources, owner, logger, descriptor, handler);
        }
    }
    private sealed class CommandRegistration : IPluginRegistration
    {
        private readonly Action<CommandRegistration> remove; private bool released;
        public CommandRegistration(PluginId owner, IPluginLogger? logger, PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler, Action<CommandRegistration> remove) { Owner = owner; Logger = logger; Descriptor = descriptor; Handler = handler; this.remove = remove; }
        public PluginId Owner { get; } public IPluginLogger? Logger { get; }
        public PluginCommandDescriptor Descriptor { get; } public Action<PluginCommandInvocation> Handler { get; }
        public string Name => "command:" + Descriptor.Id; public bool IsReleased => released;
        public void Invoke(PluginCommandInvocation invocation) => Handler(invocation);
        public void Dispose() { if (released) return; released = true; remove(this); }
    }
    private sealed class ScopeGuard : IDisposable { private int released; internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0; public void Dispose() { System.Threading.Interlocked.Exchange(ref released, 1); } }
}
