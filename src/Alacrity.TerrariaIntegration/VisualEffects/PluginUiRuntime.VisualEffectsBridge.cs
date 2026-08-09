using Terraria;

namespace AlacrityTerraria
{
    public static partial class PluginUiRuntime
    {
        public static bool ShouldRunDustSystem() => _visualEffects == null || _visualEffects.ShouldRunDustSystem;

        /// <summary>Creation gate for Dust.NewDust. Exceptions remain live when ordinary Dust is disabled.</summary>
        public static bool ShouldCreateDust(int dustType) => _visualEffects == null || _visualEffects.ShouldCreateDust(dustType);

        /// <summary>Per-instance Dust update gate used only when exceptions require the Dust loop to run.</summary>
        public static bool ShouldUpdateDustInstance(Dust dust) => _visualEffects == null || _visualEffects.ShouldUpdateDustInstance(dust);

        /// <summary>Whole-system Gore gate. Gore has no exception path.</summary>
        public static bool ShouldRunGoreSystem() => _visualEffects == null || _visualEffects.ShouldRunGoreSystem;

        private static void RefreshVisualEffectsPolicy()
        {
            _visualEffects?.Refresh();
        }
    }
}
