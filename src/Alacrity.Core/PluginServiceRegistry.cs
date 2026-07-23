using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host registry for public cross-plugin service contracts.</summary>
public sealed class PluginServiceHub
{
    private readonly object gate = new object();
    private readonly Dictionary<Type, PublishedService> services = new Dictionary<Type, PublishedService>();

    /// <summary>Creates a dependency-restricted registry for one plugin enable scope.</summary>
    public IPluginServiceRegistry CreateRegistry(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new ScopedRegistry(this, manifest, resources);
    }

    private IPluginRegistration Publish<TService>(PluginManifest publisher, IPluginResourceScope resources, TService service) where TService : class
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        var type = typeof(TService);
        PublishedService published;
        lock (gate)
        {
            if (services.ContainsKey(type))
                throw new InvalidOperationException("A service is already published for " + type.FullName + ".");
            published = new PublishedService(type, publisher.Id, service, Remove);
            services.Add(type, published);
        }

        try
        {
            resources.Own("service:" + type.FullName, PluginResourceKind.Service, published);
            return published;
        }
        catch
        {
            published.Dispose();
            throw;
        }
    }

    private bool TryGet<TService>(PluginManifest consumer, out TService? service) where TService : class
    {
        lock (gate)
        {
            if (!services.TryGetValue(typeof(TService), out var published) || !CanConsume(consumer, published.Owner))
            {
                service = null;
                return false;
            }

            service = (TService)published.Service;
            return true;
        }
    }

    private void Remove(PublishedService published)
    {
        lock (gate)
        {
            if (services.TryGetValue(published.ContractType, out var current) && ReferenceEquals(current, published))
                services.Remove(published.ContractType);
        }
    }

    private static bool CanConsume(PluginManifest consumer, PluginId provider)
    {
        return consumer.Id == provider || consumer.Dependencies.Any(dependency => dependency.Id == provider);
    }

    private sealed class ScopedRegistry : IPluginServiceRegistry
    {
        private readonly PluginServiceHub hub;
        private readonly PluginManifest manifest;
        private readonly IPluginResourceScope resources;

        public ScopedRegistry(PluginServiceHub hub, PluginManifest manifest, IPluginResourceScope resources)
        {
            this.hub = hub;
            this.manifest = manifest;
            this.resources = resources;
        }

        public IPluginRegistration Publish<TService>(TService service) where TService : class => hub.Publish(manifest, resources, service);

        public bool TryGet<TService>(out TService? service) where TService : class => hub.TryGet(manifest, out service);

        public TService GetRequired<TService>() where TService : class
        {
            if (TryGet<TService>(out var service) && service != null)
                return service;
            throw new InvalidOperationException("Required service " + typeof(TService).FullName + " is unavailable or not declared as a plugin dependency.");
        }
    }

    private sealed class PublishedService : IPluginRegistration
    {
        private readonly Action<PublishedService> remove;
        private bool released;
        public PublishedService(Type contractType, PluginId owner, object service, Action<PublishedService> remove)
        {
            ContractType = contractType;
            Owner = owner;
            Service = service;
            this.remove = remove;
        }
        public Type ContractType { get; }
        public PluginId Owner { get; }
        public object Service { get; }
        public string Name => "service:" + ContractType.FullName;
        public bool IsReleased => released;
        public void Dispose()
        {
            if (released) return;
            released = true;
            remove(this);
        }
    }
}
