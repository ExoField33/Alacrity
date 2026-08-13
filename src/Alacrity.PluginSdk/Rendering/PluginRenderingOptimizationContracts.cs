using System;

namespace Alacrity.PluginSdk;

/// <summary>
/// Host-verified local renderer preparation optimizations. Values are requests, not access to
/// Terraria renderer internals; unsupported values always preserve vanilla behavior.
/// </summary>
[Flags]
public enum PluginRenderingOptimization
{
    /// <summary>Does not request a renderer optimization.</summary>
    None = 0,

    /// <summary>Removes redundant pending painted-tile render-target preparations.</summary>
    PaintedTilePreparation = 1,

    /// <summary>Uses verified visible clothing-entity IDs to avoid redundant position lookups.</summary>
    ClothingEntityPresentation = 2,

    /// <summary>
    /// Reduces redundant local waterfall renderer state changes and repeated guarded solidity
    /// checks while preserving Terraria's live waterfall discovery and path evaluation.
    /// </summary>
    WaterfallPresentation = 4,

    /// <summary>
    /// Removes verified redundant work from Terraria's common tile drawing path while preserving
    /// the native paint, special-tile, and unloaded-asset paths.
    /// </summary>
    TileDrawingPresentation = 8,

    /// <summary>
    /// Removes verified allocation and repeated-state work from Terraria's top-level draw
    /// orchestration. This remains a host request: plugins do not receive renderer access.
    /// </summary>
    DrawOrchestration = 16,

    /// <summary>
    /// Replaces Terraria's repeated per-cell laser-ruler presentation with a version-verified
    /// batched grid. This is a local presentation request and never exposes renderer access.
    /// </summary>
    LaserRulerPresentation = 32,

    /// <summary>
    /// Reuses verified static tile draw descriptors in fixed 20 by 20 tile regions while
    /// continuing to calculate Terraria's lighting for every rendered frame. Unsupported,
    /// animated, painted, or visibility-sensitive tiles remain on Terraria's native path.
    /// </summary>
    StaticTileChunkPresentation = 64
}

/// <summary>
/// Immutable activation-scoped request for host-owned rendering optimizations. Active policies
/// compose as a bitwise union and are removed automatically when the activation scope ends.
/// </summary>
public sealed class PluginRenderingOptimizationPolicy
{
    /// <summary>Creates a policy containing the requested verified optimization categories.</summary>
    public PluginRenderingOptimizationPolicy(PluginRenderingOptimization optimizations)
    {
        Optimizations = optimizations;
    }

    /// <summary>Requested local optimization categories.</summary>
    public PluginRenderingOptimization Optimizations { get; }
}

/// <summary>
/// Registers local renderer optimization requests. The service is activation-scoped and requires
/// the generic <see cref="PluginCapability.Rendering"/> capability; it never exposes raw
/// Terraria/XNA rendering objects to plugins.
/// </summary>
public interface IPluginRenderingOptimizationService
{
    /// <summary>Registers an activation-owned policy request.</summary>
    IPluginRegistration RegisterPolicy(PluginRenderingOptimizationPolicy policy);
}
