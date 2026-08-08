using System;
using System.Reflection;

namespace Alacrity.Core;

/// <summary>Host-owned assembly lifetime boundary. net472 cannot safely unload individual plugin assemblies.</summary>
public interface IPluginLoadContext
{
    bool SupportsVerifiedUnload { get; }
    Assembly Load(string assemblyPath);
    bool TryUnload(out string reason);
}

/// <summary>Current Terraria runtime implementation: load normally and require restart for reload/update.</summary>
public sealed class NetFrameworkPluginLoadContext : IPluginLoadContext
{
    public bool SupportsVerifiedUnload => false;
    public Assembly Load(string assemblyPath) => Assembly.LoadFrom(assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath)));
    public bool TryUnload(out string reason)
    {
        reason = "Plugin reload requires restarting Alacrity because the net472 runtime cannot verify individual assembly unloads.";
        return false;
    }
}
