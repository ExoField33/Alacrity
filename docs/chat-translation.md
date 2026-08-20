# Chat Translation

`alacrity.chat-translation` is an ordinary bundled PluginSdk plugin. It does not use a
plugin-specific Terraria patch or receive a live chat monitor, `SpriteBatch`, or networking
client.

## Setup

The plugin uses the Google Translate-compatible `translate-pa.googleapis.com/v1/translate` HTTPS
endpoint. Its transport credential is supplied by the bundled plugin, so no API-key setting or
clipboard setup is required from players.

The default source language is Auto detect, the incoming target is English, and both automatic
directions are disabled by default. Both settings UIs and the chat action menu provide a searchable,
scrolling dropdown with the complete bundled Google language catalog. The action icon's root command
popover intentionally has no search field; only its nested language dropdowns are searchable. Those
language choices expand upward with the search field fixed below them. Pressing a keyboard key begins
search without requiring a click. Search matches a language's
display name or stable language code locally, so opening or filtering a chooser never competes with
an in-flight translation request. Host search supports word deletion/navigation (`Ctrl+Backspace`,
`Ctrl+Delete`, and `Ctrl+Left`/`Ctrl+Right`) without editing the pending chat message. Manual
translation still works when automatic incoming translation is disabled.

## In-game behavior

The host places an input-height icon immediately left of the player chat entry, aligned with its
bottom edge. A normal left click opens its side popover. Shift-left-click toggles automatic outgoing
translation; Shift-right-click toggles automatic incoming translation. A green icon background means incoming automatic translation is enabled, a red
background means automatic outgoing translation is enabled, and a split green/red background
indicates both. While a language search has focus, the first Escape leaves that search field; the
next Escape returns to the previous level: a language chooser returns to the action menu, and the
action menu returns to the chat field.
Host-rendered controls provide standard hover/click feedback and submenu open/back sounds.

Clicking an eligible message translates it. Clicking it again restores its original text. A
translated presentation includes the gray suffix ` (Translated from Language)` and is local-only.
Only stored chat-monitor messages enter this presentation path. Editable player-chat input and
outgoing submission remain independent, so incoming translation cannot change text that is still
being composed or sent.
Links are deliberately left to their producing chat plugin rather than being rewritten.

With automatic outgoing translation enabled, ordinary chat is translated in an activation-owned
background task before Terraria performs its usual submission. Slash commands keep native command
behavior. The host bounds each transform; timeout, cancellation, or failure releases the pending
submission and keeps the original input unsent. Editing the chat box revokes a pending transform so
a late result can never replace newer player text. Chat translation cache and message state are
cleared when returning to the game menu, disabling the plugin, or ending its activation.

## Generic host facilities

This plugin demonstrates reusable host capabilities rather than plugin-specific integration:

- `context.Network` accepts only bounded streamed HTTPS responses. Redirect hops are separately
  checked against the manifest's exact approved host list and are cancelled with the activation.
- `context.Terraria.Chat.RegisterMessageAction` associates a click with the span's producing
  plugin activation.
- `context.Terraria.Chat.RegisterActionButton` provides the generic chat action strip and
  host-rendered popover.
- `context.Terraria.Chat.RegisterOutgoingMessageTransformer` defers one selected non-command
  submission and resumes the normal Terraria path exactly once.

All registrations and background requests are activation-scoped. A disabled or re-enabled plugin
cannot publish an old response into a fresh activation.
