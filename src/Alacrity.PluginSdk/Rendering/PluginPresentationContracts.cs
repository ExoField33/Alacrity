using System;

namespace Alacrity.PluginSdk;

/// <summary>
/// Individually suppressible local presentation elements implemented by a supported game
/// integration. Values are requests: plugins never receive renderer access or native objects.
/// </summary>
[Flags]
public enum PluginPresentationElement
{
    /// <summary>Does not suppress any presentation element.</summary>
    None = 0,

    /// <summary>
    /// The optional sparkle and item icon displayed at a Paladin's Shield protection endpoint.
    /// The boundary arcs and range effects remain native Terraria presentation.
    /// </summary>
    PaladinShieldIcon = 1
}

/// <summary>
/// Immutable activation-scoped request to omit supported local presentation elements. Multiple
/// policies compose as a bitwise union and are removed automatically when their activation ends.
/// </summary>
public sealed class PluginPresentationSuppressionPolicy
{
    /// <summary>Creates a request for the supplied local presentation elements.</summary>
    public PluginPresentationSuppressionPolicy(PluginPresentationElement elements)
    {
        Elements = elements;
    }

    /// <summary>The locally rendered elements requested for suppression.</summary>
    public PluginPresentationElement Elements { get; }
}

/// <summary>
/// Registers host-owned local presentation suppression requests. This rendering-capability
/// service is activation-scoped, thread-safe for registration and never exposes Terraria/XNA
/// renderer state. Unsupported elements always retain native rendering.
/// </summary>
public interface IPluginPresentationSuppressionService
{
    /// <summary>Registers an activation-owned local presentation suppression request.</summary>
    IPluginRegistration RegisterPolicy(PluginPresentationSuppressionPolicy policy);
}
