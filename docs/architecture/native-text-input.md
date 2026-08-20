# Native Text Input

Alacrity modernizes Terraria's existing `Main.GetInputText(string, bool)` helper through the
version-locked `PluginUiRuntime` facade. This is an internal client feature: plugins continue to
receive only `IPluginContext` and never inspect native keyboard, clipboard, or caret state.

The helper is used by player chat and other focused desktop text fields, including signs, chest
names, server connection fields, and Terraria search controls. The core editor adds normal caret
movement, Shift selection, Home/End, Ctrl word movement and deletion, Ctrl A/C/X/V, Shift
Insert/Delete, tag-aware movement for Terraria item/glyph tags, and surrogate-pair-safe movement.
`clrInput()` resets that transient state with Terraria's own input reset. Terraria's version-locked
`UITextBox` draw path reads the retained caret and draws the active selection without modifying its
stored text. Presentation is tied to the concrete version-locked `UITextBox` instance rather than
its string value, so two equal fields cannot borrow one another's caret or selection. If that
identity cannot be established, the field retains Terraria's normal end-caret presentation.
Legacy menu fields retain Terraria's own blinking ticker, but the version-locked `DrawMenu` patch
places that ticker at the retained caret, including same-length password masks. Player chat draws the
same selection behind its normal chat snippets. IME composition remains delegated to
Terraria's platform service and falls back to the original helper while a composition is active.
When player chat is active, the same bridge dispatches only normalized non-text editor actions
(history navigation and wheel scrolling) through the existing activation-scoped chat host. It does
not re-run raw keyboard parsing or expose native input state to plugins.

The injected helper returns `false` whenever it cannot safely own the frame, so Terraria executes
its original method body. Player-chat history, chat scrolling, and action-menu search remain in
the generic chat host; the facade forwards only the narrow action-menu search/Escape claims needed
to keep those controls independent from text editing.
