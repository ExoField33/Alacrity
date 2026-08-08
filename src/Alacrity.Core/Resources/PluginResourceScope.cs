using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Thread-safe resource owner used by the host for plugin and feature lifetimes. A child scope is
/// registered as a single parent resource, so parent cleanup remains deterministic and reverse ordered.
/// </summary>
public sealed class PluginResourceScope : IPluginResourceScope
{
    private readonly object gate = new object();
    private readonly List<OwnedResource> resources = new List<OwnedResource>();
    private readonly List<PluginResourceReleaseFailure> failures = new List<PluginResourceReleaseFailure>();
    private PluginResourceScopeState state = PluginResourceScopeState.Open;

    public bool IsDisposed
    {
        get { lock (gate) return state == PluginResourceScopeState.Disposed; }
    }

    public PluginResourceScopeState State
    {
        get { lock (gate) return state; }
    }

    /// <summary>Test diagnostic for retained activation resources.</summary>
    internal int ResourceCount
    {
        get { lock (gate) return resources.Count; }
    }

    /// <summary>Failures from the most recently completed release operation.</summary>
    public IReadOnlyList<PluginResourceReleaseFailure> LastReleaseFailures
    {
        get { lock (gate) return failures.ToArray(); }
    }

    public IPluginResourceHandle Own(string name, PluginResourceKind kind, IDisposable resource)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A resource name is required.", nameof(name));
        if (resource == null)
            throw new ArgumentNullException(nameof(resource));

        lock (gate)
        {
            EnsureCanRegister();
            var owned = new OwnedResource(name, kind, resource, Remove);
            resources.Add(owned);
            return owned;
        }
    }

    public IPluginResourceScope CreateChildScope(string name)
    {
        var child = new PluginResourceScope();
        Own(name, PluginResourceKind.Other, child);
        return child;
    }

    public void ReleaseAll()
    {
        var failures = ReleaseAllCore(permanentlyDispose: false);
        ThrowIfFailed(failures);
    }

    public void Dispose()
    {
        List<PluginResourceReleaseFailure> releaseFailures;
        lock (gate)
        {
            if (state == PluginResourceScopeState.Disposed)
                return;
        }

        releaseFailures = ReleaseAllCore(permanentlyDispose: true);
        ThrowIfFailed(releaseFailures);
    }

    private List<PluginResourceReleaseFailure> ReleaseAllCore(bool permanentlyDispose)
    {
        List<OwnedResource> release;
        lock (gate)
        {
            if (state == PluginResourceScopeState.Disposed)
                return new List<PluginResourceReleaseFailure>();
            if (state == PluginResourceScopeState.Releasing)
                throw new InvalidOperationException("Resource scope cleanup is already in progress.");

            state = PluginResourceScopeState.Releasing;
            release = resources.ToList();
            resources.Clear();
            failures.Clear();
        }

        for (var index = release.Count - 1; index >= 0; index--)
        {
            var resource = release[index];
            try
            {
                resource.Release();
            }
            catch (Exception exception)
            {
                lock (gate)
                    failures.Add(new PluginResourceReleaseFailure(resource.Name, resource.Kind, exception));
            }
        }

        lock (gate)
        {
            state = permanentlyDispose ? PluginResourceScopeState.Disposed : PluginResourceScopeState.Released;
            return failures.ToList();
        }
    }

    private void EnsureCanRegister()
    {
        if (state == PluginResourceScopeState.Disposed)
            throw new ObjectDisposedException(nameof(PluginResourceScope));
        if (state == PluginResourceScopeState.Releasing)
            throw new InvalidOperationException("Resources cannot be registered while cleanup is in progress.");
        if (state == PluginResourceScopeState.Released)
            state = PluginResourceScopeState.Open;
    }

    private void Remove(OwnedResource resource)
    {
        lock (gate)
            resources.Remove(resource);
    }

    private static void ThrowIfFailed(List<PluginResourceReleaseFailure> releaseFailures)
    {
        if (releaseFailures.Count == 1)
            throw releaseFailures[0].Exception;
        if (releaseFailures.Count > 1)
            throw new AggregateException(releaseFailures.Select(failure => failure.Exception));
    }

    private sealed class OwnedResource : IPluginResourceHandle
    {
        private readonly object gate = new object();
        private readonly IDisposable resource;
        private readonly Action<OwnedResource> remove;
        private bool released;

        public OwnedResource(string name, PluginResourceKind kind, IDisposable resource, Action<OwnedResource> remove)
        {
            Name = name;
            Kind = kind;
            this.resource = resource;
            this.remove = remove;
        }

        public string Name { get; }
        public PluginResourceKind Kind { get; }
        public bool IsReleased { get { lock (gate) return released; } }

        public void Dispose() => Release();

        public void Release()
        {
            lock (gate)
            {
                if (released)
                    return;
                released = true;
            }

            try
            {
                resource.Dispose();
            }
            finally
            {
                remove(this);
            }
        }
    }
}

/// <summary>Resource-specific cleanup diagnostic retained after release continues.</summary>
public sealed class PluginResourceReleaseFailure
{
    internal PluginResourceReleaseFailure(string name, PluginResourceKind kind, Exception exception)
    {
        Name = name;
        Kind = kind;
        Exception = exception;
    }

    /// <summary>Diagnostic resource name.</summary>
    public string Name { get; }
    /// <summary>Category of the resource that failed cleanup.</summary>
    public PluginResourceKind Kind { get; }
    /// <summary>Failure observed while releasing the resource.</summary>
    public Exception Exception { get; }
}
