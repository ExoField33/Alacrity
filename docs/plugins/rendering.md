# Rendering

Use `context.Hud` for retained interactive gameplay widgets and `context.Overlays` for constrained world, HUD, or menu drawing. The integration owns SpriteBatch state, approved assets, world projection, graphics resources, ordering, and failure isolation. Plugins receive only SDK canvases and immutable frame data.
