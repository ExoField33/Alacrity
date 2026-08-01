# Player List Contract Placement

`IPlayerListService` remains in `Alacrity.PluginSdk` for the current migration because the
Player List package is already loaded independently and future inspection packages need a
stable compile-time contract. It now combines `IPlayerListPresentationState` with
`IPlayerListController`; consumers can request only the read-only state when they do not need
to change local list controls. None of these contracts expose Terraria entities or mutable game state.

When the first independently distributed bundled plugin consumes this contract, move the
interface and its value types into a small `Alacrity.BundledPluginContracts` assembly shared
by those packages. Keep a type-forwarding or compatibility adapter path during that migration.
The foundational SDK should not accumulate contracts for unrelated bundled features.
