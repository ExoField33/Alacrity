# Instanced Rain Presentation

## Scope

Kinesin 0.8.0 adds **Optimize Rain Rendering**, enabled by default. The setting requests
`PluginRenderingOptimization.RainPresentation` through
`context.Terraria.RenderingOptimizations`. Kinesin remains an ordinary PluginSdk plugin and
never receives Terraria, XNA, texture, or graphics-device access.

## Vanilla behavior

Terraria 1.4.5.6's `Main.DrawRain` iterates every active rain object, calculates its live lit
and shimmer-adjusted color, submits one `SpriteBatch.Draw`, then runs `Rain.Update` when rain is
allowed. In the ordinary deferred pass those submissions are normally already combined into a
small number of GPU draws, but the main thread still performs one managed SpriteBatch submission
per active drop.

## Alacrity method

The version-locked patch leaves Terraria's active-rain loop and each `Rain.Update` call in their
native order. It replaces only the audited sprite submission with a compact append into a reusable
active-instance buffer. At the end of the loop, TerrariaIntegration uploads that contiguous range
once and draws a single indexed instanced quad pass using the native rain texture, source frame,
position, rotation, scale, and already-calculated color.

No lighting, shimmer, weather, animation, camera, random-number, or rain-update logic is moved
or cached. The common rain frame therefore has one reusable CPU buffer upload and one instanced
draw rather than a SpriteBatch submission for every active drop. CPU savings grow with active
rain count; GPU submission count can already be low in vanilla deferred SpriteBatch mode.

## Fallback and safety

The renderer checks all version-sensitive graphics resources before closing the native batch. If
the effect, device, texture, or supported SpriteBatch context is unavailable, the patch calls
Terraria's original `SpriteBatch.Draw` path. It also retains vanilla rendering for the uncommon
world render-target context that uses point sampling, because the embedded effect is verified for
the normal linear-sampled rain pass. A renderer fault restores the native SpriteBatch state and
disables the optional presentation for later frames.

## Build requirement

The embedded rain effect is compiled from `InstancedRainPresentation.fx` through the committed
tool manifest. Run `dotnet tool restore` once before building TerrariaIntegration.
