namespace Alacrity.PluginSdk;

/// <summary>Marker for immutable event snapshots delivered through <see cref="IPluginEventService"/>.</summary>
public interface IPluginEvent { }

/// <summary>Marks an event delivered on the host's documented main update thread.</summary>
public interface IMainThreadPluginEvent : IPluginEvent { }

/// <summary>Marks an event delivered while the host owns a rendering phase.</summary>
public interface IRenderThreadPluginEvent : IPluginEvent { }

/// <summary>Marker for events that describe completed work and therefore cannot be cancelled.</summary>
public interface INonCancellablePluginEvent : IPluginEvent { }
