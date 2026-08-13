using System;
using Alacrity.PluginSdk;

namespace Alacrity.RemovePaladinShieldIcon;

/// <summary>
/// Removes only the local Paladin's Shield endpoint indicator: its sparkle and item icon.
/// Terraria's protection range arcs, defense mechanics, and multiplayer behavior remain native.
/// </summary>
public sealed class RemovePaladinShieldIconPlugin : IAlacrityPlugin
{
    private IPluginPresentationSuppressionService? presentation;
    private IPluginRegistration? registration;

    public void Initialize(IPluginContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        presentation = context.Terraria.Presentation;
    }

    public void Enable()
    {
        if (registration != null && !registration.IsReleased)
        {
            return;
        }

        IPluginPresentationSuppressionService? service = presentation;
        if (service == null)
        {
            throw new InvalidOperationException("The plugin must be initialized before it is enabled.");
        }

        registration = service.RegisterPolicy(
            new PluginPresentationSuppressionPolicy(PluginPresentationElement.PaladinShieldIcon));
    }

    public void Disable()
    {
        registration?.Dispose();
        registration = null;
    }

    public void Shutdown()
    {
        Disable();
        presentation = null;
    }
}
