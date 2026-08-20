using System;
using System.Diagnostics;
using Alacrity.PluginSdk;
using Terraria;
using Terraria.GameContent.UI.States;

namespace AlacrityTerraria
{
    public static partial class PluginUiRuntime
    {
        public static void AppendPluginKeybindControls(UIManageControls controls)
        {
            if (RuntimeHost.IsBootstrapped && controls != null) _keybindRuntime?.AppendControls(controls);
        }

        /// <summary>
        /// Polls host-owned plugin bindings at the established gameplay UI boundary. The native
        /// input profile remains the source of persisted bindings; no plugin receives raw input.
        /// </summary>
        public static void UpdatePluginKeybinds()
        {
            if (!RuntimeHost.IsBootstrapped || RuntimeHost.IsShuttingDown || _extensions == null)
                return;
            try
            {
                _entitySnapshots?.CaptureForCurrentTick();
                _sessionPresentation?.CaptureForCurrentTick();
                _worldSections?.CaptureForCurrentTick();
                UpdateIconInteractionInput();
                _dispatcher?.Drain(exception => ReportOptionalUiFailure("Plugin dispatcher callback", exception));
                _scheduler?.Tick(Main.GameUpdateCount);
                _chatAdapter?.QueueReadyOutgoingMessageForNativeSubmit();
                RefreshVisualEffectsPolicy();
                RefreshRenderCullingPolicy();
                TimeSpan timestamp = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                _presentationStates.Update(Main.gameMenu, Main.drawingPlayerChat, out bool menuChanged, out bool chatInputChanged);
                if (menuChanged) _extensions.Publish(new ClientMenuStateChangedEvent(Main.gameMenu, Main.GameUpdateCount, timestamp));
                if (chatInputChanged) _extensions.Publish(new ChatInputStateChangedEvent(Main.drawingPlayerChat, Main.GameUpdateCount, timestamp));
                _extensions.Publish(new ClientUpdatedEvent(Main.GameUpdateCount, timestamp));
                // Terraria's keyboard state remains observable while the window is unfocused. Do
                // not turn an alt-tabbed keystroke into a client action such as Player List display.
                if (Main.instance == null || !Main.instance.IsActive || Main.gameMenu || Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.blockInput)
                    return;
                _keybindRuntime?.Dispatch();

            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin keybind dispatch", exception);
            }
        }

        /// <summary>
        /// Synchronizes plugin IDs into Terraria's native trigger dictionaries before KeyboardInput
        /// calls KeyConfiguration.CopyKeyState. Terraria indexes those dictionaries directly.
        /// </summary>
        public static void EnsurePluginKeybindStateShape()
        {
            if (!RuntimeHost.IsBootstrapped || _keybindRuntime == null)
                return;

            _keybindRuntime.EnsureNativeStateShape();
        }

        /// <summary>Whole-system Dust fast path used before Terraria enters the DrawDust or UpdateDust loops.</summary>
    }
}
