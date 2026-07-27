namespace Alacrity.PluginSdk;

/// <summary>
/// Marker for a stable, host-provided Terraria integration service contract.
/// Plugins acquire implementations through <see cref="IPluginContext.Services"/> rather than by
/// receiving raw Terraria objects or by extending the plugin context for each new integration area.
/// </summary>
public interface ITerrariaService
{
}
