using Mono.Cecil;
using Mono.Cecil.Cil;

internal enum ClientPatchStatus
{
    Applied,
    AlreadyApplied,
    UnsupportedTarget,
    AnchorNotFound,
    AmbiguousTarget,
    ValidationFailed,
    Failed
}

internal sealed class ClientPatchResult
{
    internal ClientPatchResult(string patchId, ClientPatchStatus status, string detail)
    {
        PatchId = patchId;
        Status = status;
        Detail = detail;
    }

    internal string PatchId { get; }
    internal ClientPatchStatus Status { get; }
    internal string Detail { get; }
}

internal sealed class ClientPatchDefinition
{
    internal ClientPatchDefinition(string id, Action<ModuleDefinition, string> apply, Func<ModuleDefinition, bool> isPresent, IReadOnlyList<ClientPatchOperation> operations, params string[] dependencies)
    {
        Id = id;
        Apply = apply;
        IsPresent = isPresent;
        Operations = operations;
        Dependencies = dependencies;
    }

    internal string Id { get; }
    internal Action<ModuleDefinition, string> Apply { get; }
    internal Func<ModuleDefinition, bool> IsPresent { get; }
    internal IReadOnlyList<ClientPatchOperation> Operations { get; }
    internal IReadOnlyList<string> Dependencies { get; }
}

/// <summary>
/// One concrete version-locked Terraria method transformation. This is both the human-readable
/// inventory and the data used to verify that the operation injected every required bridge call.
/// </summary>
internal sealed class ClientPatchTarget
{
    internal ClientPatchTarget(
        string id,
        string typeName,
        string memberSignature,
        string anchor,
        string injection,
        params string[] bridgeMethods)
        : this(id, typeName, memberSignature, anchor, injection, ClientPatchPostconditionMode.ExactlyCount, bridgeMethods)
    {
    }

    internal ClientPatchTarget(
        string id,
        string typeName,
        string memberSignature,
        string anchor,
        string injection,
        ClientPatchPostconditionMode bridgeCallMode,
        params string[] bridgeMethods)
    {
        Id = id;
        TypeName = typeName;
        MemberSignature = memberSignature;
        Anchor = anchor;
        Injection = injection;
        BridgeMethods = bridgeMethods;
        BridgeCallMode = bridgeCallMode;
        Precondition = "The exact member signature and unique anchor recorded for this target must be present in the clean, hash-verified Terraria 1.4.5.6 executable.";
        Postcondition = bridgeMethods.Length == 0
            ? "The recorded target mutation must survive the Cecil write/reopen validation."
            : "Every listed PluginUiRuntime ABI call must be present exactly once after Cecil write/reopen validation, except all-return capture sites which require one call before every return.";
    }

    internal ClientPatchTarget(
        string id,
        string typeName,
        string memberSignature,
        string anchor,
        string injection,
        int expectedBridgeCallCount,
        params string[] bridgeMethods)
        : this(id, typeName, memberSignature, anchor, injection, expectedBridgeCallCount, generatedMember: false, bridgeMethods)
    {
    }

    /// <summary>
    /// Describes a narrow helper method created by the same patch operation. Generated helpers
    /// are not expected to exist in the clean executable, but must satisfy the exact same
    /// structural and bridge-call postconditions after the module is written and reopened.
    /// </summary>
    internal ClientPatchTarget(
        string id,
        string typeName,
        string memberSignature,
        string anchor,
        string injection,
        int expectedBridgeCallCount,
        bool generatedMember,
        params string[] bridgeMethods)
        : this(id, typeName, memberSignature, anchor, injection, bridgeMethods)
    {
        if (expectedBridgeCallCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedBridgeCallCount));
        }

        ExpectedBridgeCallCount = expectedBridgeCallCount;
        GeneratedMember = generatedMember;
    }

    internal string Id { get; }
    internal string TypeName { get; }
    internal string MemberSignature { get; }
    internal string Anchor { get; }
    internal string Injection { get; }
    internal string Precondition { get; }
    internal string Postcondition { get; }
    internal IReadOnlyList<string> BridgeMethods { get; }
    internal int ExpectedBridgeCallCount { get; } = 1;
    internal ClientPatchPostconditionMode BridgeCallMode { get; }
    internal bool GeneratedMember { get; }
}

/// <summary>Explicit bridge placement semantics. Never infer IL requirements from prose inventory text.</summary>
internal enum ClientPatchPostconditionMode
{
    ExactlyCount,
    BeforeEveryReturn
}

/// <summary>Inspectable target and ABI contract for one independently applied patch set.</summary>
internal sealed class ClientPatchOperation
{
    internal ClientPatchOperation(string id, string targetType, string targetDescription, params string[] bridgeMethods)
    {
        Id = id;
        TargetType = targetType;
        TargetDescription = targetDescription;
        BridgeMethods = bridgeMethods;
        Targets = Array.Empty<ClientPatchTarget>();
    }

    internal ClientPatchOperation(
        string id,
        string targetType,
        string targetDescription,
        IReadOnlyList<ClientPatchTarget> targets,
        params string[] bridgeMethods)
        : this(id, targetType, targetDescription, targets, structuralOnly: false, bridgeMethods)
    {
    }

    internal ClientPatchOperation(
        string id,
        string targetType,
        string targetDescription,
        IReadOnlyList<ClientPatchTarget> targets,
        bool structuralOnly,
        params string[] bridgeMethods)
    {
        Id = id;
        TargetType = targetType;
        TargetDescription = targetDescription;
        Targets = targets;
        BridgeMethods = bridgeMethods;
        StructuralOnly = structuralOnly;
    }

    internal string Id { get; }
    internal string TargetType { get; }
    internal string TargetDescription { get; }
    internal IReadOnlyList<ClientPatchTarget> Targets { get; }
    internal IReadOnlyList<string> BridgeMethods { get; }
    internal bool StructuralOnly { get; }
}

/// <summary>Ordered, audited transformations for exactly one supported Terraria build.</summary>
internal static class PermanentPatchCatalog
{
    internal const string Identity = "alacrity-terraria-1.4.5.6-r25";

    private static readonly ClientPatchDefinition[] Definitions =
    {
        CreateDefinition(
            "patch.runtime.startup-and-menu",
            PermanentPatchPlan.ApplyPermanentStartupAndMenu,
            "runtime.startup-and-menu",
            "Terraria.Main / Terraria.IngameOptions",
            "Main-menu insertion, in-game settings replacement, and version labels",
            new ClientPatchTarget("menu.version-labels", "Terraria.Main", ".cctor()", "the exact v1.4.5.6 assignments to versionNumber and versionNumber2", "replace string literals with Terraria v1.4.5.6"),
            new ClientPatchTarget("menu.main-entry", "Terraria.Main", "DrawMenu(Microsoft.Xna.Framework.GameTime)", "SocialAPI.Workshop load following the verified seven-row count and locals 27/9/45", "insert a native Plugins row and call", "OpenPluginManager"),
            new ClientPatchTarget("menu.ingame-settings", "Terraria.IngameOptions", "Draw(Terraria.Main, Microsoft.Xna.Framework.Graphics.SpriteBatch)", "Lang.menu[118] Close Menu label/action and final Main.DrawThickCursor call", "replace Close Menu action and insert draw callback", "OpenIngamePluginSettings", "DrawIngamePluginSettings"),
            new ClientPatchTarget("menu.version-draw", "Terraria.Main", "DrawMenu(Microsoft.Xna.Framework.GameTime)", "Main.DrawVersionNumber(Color, Single) using verified locals 3 and 31", "insert after version draw", "DrawAlacrityVersion")),
        CreateDefinition(
            "patch.runtime.input-and-keybinds",
            PermanentPatchPlan.ApplyPermanentInputAndKeybinds,
            "runtime.input-and-keybinds",
            "Terraria.Main / Terraria.Player / Terraria.GameInput.PlayerInput / Terraria.GameContent.UI.States.UIManageControls",
            "Post-input keybind dispatch, scoped Escape handling, and controls-menu integration",
            new ClientPatchTarget("input.post-input", "Terraria.Main", "DoUpdate_HandleInput()", "final return after Terraria updates input state", "insert keybind update and dropdown Escape admission", "UpdatePluginKeybinds", "HandleInput"),
            new ClientPatchTarget("input.ingame-options-escape", "Terraria.Player", "ToggleInv()", "the unique IngameOptions.Close call in the active in-game-options branch", "skip only the native options close when an Alacrity dropdown consumed Escape", "HandleInput"),
            new ClientPatchTarget("input.key-state-shape", "Terraria.GameInput.PlayerInput", "UpdateInput()", "SettingsForUI.UpdateCounters() call", "insert before native state reset/copy", "EnsurePluginKeybindStateShape"),
            new ClientPatchTarget("input.controls-menu", "Terraria.GameContent.UI.States.UIManageControls", "OnInitialize()", "final return", "insert before return", "AppendPluginKeybindControls")),
        CreateDefinition(
            "patch.runtime.rendering-and-combat",
            PermanentPatchPlan.ApplyPermanentRenderingAndCombat,
            "runtime.rendering-and-combat",
            "Terraria.Main / Terraria.Player",
            "HUD notification, world-overlay, and melee collision capture hooks",
            new ClientPatchTarget("render.notifications", "Terraria.Main", "DrawInterface_33_MouseText()", "method entry and static Main.spriteBatch field", "insert before first instruction", "DrawNotifications"),
            new ClientPatchTarget("render.world-overlays", "Terraria.Main", "DrawInterface_1_1_DrawEmoteBubblesInWorld()", "EmoteBubble.DrawAll(SpriteBatch) continuation", "insert after native emote bubble draw", "DrawHitboxes"),
            new ClientPatchTarget("combat.melee-capture", "Terraria.Player", "ItemCheck_GetMeleeHitbox(Item, Rectangle, Boolean&, Rectangle&)", "all returns in the verified four-parameter method", "insert before return and retarget branch/EH references", ClientPatchPostconditionMode.BeforeEveryReturn, "CaptureSwingHitbox")),
        CreateDefinition(
            "patch.runtime.banner-search",
            PermanentPatchPlan.ApplyPermanentBannerSearch,
            "ui.banner-search",
            "Terraria.UI.BannerClaimingUI",
            "Native banner-claiming search that filters only the local player's current kill-count claimable entries",
            new ClientPatchTarget("banner-search.filter", "Terraria.UI.BannerClaimingUI", "UpdateAndGetClaimableItemsCount()", "the verified positive claimable-count branch before native entry compaction", "skip nonmatching available banner entries before both native list and grid views consume the compact array", "ShouldDisplayAvailableBanner"),
            new ClientPatchTarget("banner-search.grid-field", "Terraria.UI.BannerClaimingUI", "DrawBannersGrid(SpriteBatch)", "the refreshed claimable-count store at method entry", "draw the local search field immediately left of the native grid's first row", "DrawAvailableBannerSearch"),
            new ClientPatchTarget("banner-search.empty-results", "Terraria.UI.BannerClaimingUI", "UpdateAndGetClaimableItemsCount()", "the native AnyAvailableBanners assignment", "retain the local Banners menu while an active local filter has zero matches", "ShouldKeepBannerMenuAvailable")),
        CreateDefinition(
            "patch.runtime.presentation-suppression",
            PermanentPatchPlan.ApplyPermanentPresentationSuppression,
            "render.presentation-suppression",
            "Terraria.Main",
            "Optional local presentation element gates",
            new ClientPatchTarget("render.paladin-shield-icon", "Terraria.Main", "DrawPaladinsShieldBoundary(Vector2, Vector2)", "the verified endpoint sparkle followed by the unique LoadItem(938) icon anchor and SpriteBatch.Draw", "skip only the optional endpoint sparkle and icon when requested", "ShouldDrawPaladinShieldIcon")),
        CreateDefinition(
            "patch.runtime.render-culling",
            PermanentPatchPlan.ApplyPermanentRenderCulling,
            "render.culling",
            "Terraria.Main / Terraria.Graphics.Renderers.ParticleRenderer",
            "Conservative fully-off-screen player, dropped-item, and common world-particle presentation gates",
            new ClientPatchTarget("culling.player-draw-order", "Terraria.Main", "RefreshPlayerDrawOrder()", "Player.outOfRange branch and verified Player loop local", "skip fully off-screen remote player before native draw-list selection", "ShouldDrawWorldPlayer"),
            new ClientPatchTarget("culling.dropped-items", "Terraria.Main", "DrawItems()", "verified Main.item load and DrawItem(WorldItem, Int32) call", "skip fully off-screen dropped item before native DrawItem", "ShouldDrawWorldItem"),
            new ClientPatchTarget("culling.world-particles", "Terraria.Graphics.Renderers.ParticleRenderer", "Draw(SpriteBatch)", "IParticle removed-state loop branch and IParticle.Draw call", "skip only common particles with verified world positions", "ShouldDrawWorldParticle")),
        CreateDefinition(
            "patch.runtime.visual-effects",
            PermanentPatchPlan.ApplyPermanentVisualEffects,
            "runtime.visual-effects",
            "Terraria.Main / Terraria.Dust / Terraria.Gore",
            "Dust and gore simulation, creation, and draw policy gates",
            new ClientPatchTarget("effects.dust-draw", "Terraria.Main", "DrawDust()", "method entry and verified dust loop local", "entry gate and per-instance branch", "ShouldRunDustSystem", "ShouldDrawDustInstance"),
            new ClientPatchTarget("effects.dust-create", "Terraria.Dust", "NewDust(..., Int32 type, ...)", "method entry with type at parameter index 3", "return vanilla failure sentinel when denied", "ShouldCreateDust"),
            new ClientPatchTarget("effects.dust-update", "Terraria.Dust", "UpdateDust()", "method entry and active-field branch using the verified Dust loop local", "entry gate and per-instance loop branch", "ShouldRunDustSystem", "ShouldUpdateDustInstance"),
            new ClientPatchTarget("effects.gore-draw", "Terraria.Main", "DrawGore()", "method entry", "return gate", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-draw-behind", "Terraria.Main", "DrawGoreBehind()", "method entry", "return gate", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-draw-back", "Terraria.Main", "DrawBackGore()", "method entry", "return gate", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-create", "Terraria.Gore", "NewGore(...)", "method entry", "return sentinel", "ShouldRunGoreSystem"),
            new ClientPatchTarget("effects.gore-update", "Terraria.Gore", "Update()", "method entry", "return gate", "ShouldRunGoreSystem")),
        CreateDefinition(
            "patch.runtime.painted-tile-preparation",
            PermanentPatchPlan.ApplyPermanentPaintedTilePreparation,
            "render.painted-tile-preparation",
            "Terraria.GameContent.TilePaintSystemV2 / Terraria.GameContent.Drawing.TileDrawing",
            "Deduplicates unready paint holders, bypasses unpainted lazy-scan work, and avoids non-foliage extra-preparation work while a generic rendering optimization policy is active",
            new ClientPatchTarget("paint.pending-tile", "Terraria.GameContent.TilePaintSystemV2", "RequestTile(...)", "the verified IsReady branch and pending request-list insertion", "gate duplicate enqueue by a holder-local pending field", "IsPaintPreparationOptimizationEnabled"),
            new ClientPatchTarget("paint.pending-cage", "Terraria.GameContent.TilePaintSystemV2", "RequestCageTop(...)", "the verified IsReady branch and pending request-list insertion", "gate duplicate enqueue by a holder-local pending field", "IsPaintPreparationOptimizationEnabled"),
            new ClientPatchTarget("paint.pending-wall", "Terraria.GameContent.TilePaintSystemV2", "RequestWall(...)", "the verified IsReady branch and pending request-list insertion", "gate duplicate enqueue by a holder-local pending field", "IsPaintPreparationOptimizationEnabled"),
            new ClientPatchTarget("paint.pending-tree-top", "Terraria.GameContent.TilePaintSystemV2", "RequestTreeTop(...)", "the verified IsReady branch and pending request-list insertion", "gate duplicate enqueue by a holder-local pending field", "IsPaintPreparationOptimizationEnabled"),
            new ClientPatchTarget("paint.pending-tree-branch", "Terraria.GameContent.TilePaintSystemV2", "RequestTreeBranch(...)", "the verified IsReady branch and pending request-list insertion", "gate duplicate enqueue by a holder-local pending field", "IsPaintPreparationOptimizationEnabled"),
            new ClientPatchTarget("paint.lazy-unpainted-scan", "Terraria.GameContent.Drawing.TileDrawing", "PrepareForAreaDrawing(System.Int32, System.Int32, System.Int32, System.Int32, System.Boolean)", "the verified active-tile and wall asset-load entries in the lazy painted-area scan", "skip only unpainted lazy tile/wall preparation while preserving the native non-lazy continuation", "IsPaintPreparationOptimizationEnabled"),
            new ClientPatchTarget("paint.extra-preparation-prefilter", "Terraria.GameContent.Drawing.TileDrawing", "MakeExtraPreparations(Terraria.Tile, System.Int32, System.Int32)", "method entry before the verified tree-only type switch", "return immediately for ordinary types only while the generic optimization is active", "IsPaintExtraPreparationRelevant")),
        CreateDefinition(
            "patch.runtime.clothing-entity-presentation",
            PermanentPatchPlan.ApplyPermanentClothingEntityPresentation,
            "render.clothing-entity-presentation",
            "Terraria.GameContent.Drawing.TileDrawing / Terraria.DataStructures.TileEntity",
            "Reserve discovery-map capacity, deduplicate repeated multi-tile discovery, skip empty hat-rack presentation, use IDs already resolved during tile drawing, and spread first-use visual configurations across bounded draw frames",
            new ClientPatchTarget("clothing.dictionary-capacity", "Terraria.GameContent.Drawing.TileDrawing", ".ctor(Terraria.GameContent.TilePaintSystemV2)", "the exact default Dictionary<Point, Int32> constructors assigned to clothing position fields", "replace each with a capacity-reserving constructor"),
            new ClientPatchTarget("clothing.discovery-deduplication", "Terraria.GameContent.Drawing.TileDrawing", "ClearCachedTileDraws(System.Boolean)", "the verified solid-layer cache reset followed by display-doll/hat-rack ContainsKey branches", "capture the policy once and skip repeated lookups for consecutive segments resolving to the same clothing entity point", "IsClothingEntityPresentationOptimizationEnabled"),
            new ClientPatchTarget("clothing.post-draw", "Terraria.GameContent.Drawing.TileDrawing", "PostDrawTiles(System.Boolean)", "the consecutive verified DrawEntities_HatRacks and DrawEntities_DisplayDolls calls in the solid-layer branch", "replace both calls with policy-gated ID-based draw paths while preserving every native clothing draw", "IsClothingEntityPresentationOptimizationEnabled")),
        CreateDefinition(
            "patch.runtime.waterfall-presentation",
            PermanentPatchPlan.ApplyPermanentWaterfallPresentation,
            "render.waterfall-presentation",
            "Terraria.WaterfallManager",
            "Version-locked idle discovery reuse plus local waterfall state, camera, and solidity reductions",
            new ClientPatchTarget("waterfall.discovery-reuse", "Terraria.WaterfallManager", "FindWaterfalls(System.Boolean)", "the verified scheduled discovery-counter reset before native area scanning", "reuse the last native source set only for an unchanged view with no tracked geometry mutation or active liquid work", "IsWaterfallPresentationOptimizationEnabled"),
            new ClientPatchTarget("waterfall.discovery-invalidation", "Terraria.WorldGen", "PlaceTile(System.Int32,System.Int32,System.Int32,System.Boolean,System.Boolean,System.Int32,System.Int32)", "the verified local placement, removal, slope, actuation, liquid-transform, and received-tile-change paths", "mark the cached discovery result dirty so the next scheduled lookup executes the native scan"),
            new ClientPatchTarget("waterfall.discovery-liquid-invalidation", "Terraria.Liquid", "AddWater(System.Int32,System.Int32)", "the native liquid-work admission that remains observable after queues settle", "mark the cached discovery result dirty before liquid simulation can change a source"),
            new ClientPatchTarget("waterfall.discovery-buffered-liquid-invalidation", "Terraria.LiquidBuffer", "AddBuffer(System.Int32,System.Int32)", "the native buffered-liquid admission used when the active liquid queue is full", "mark the cached discovery result dirty before deferred liquid simulation can change a source"),
            new ClientPatchTarget("waterfall.layer-state", "Terraria.WaterfallManager", "DrawWaterfall(System.Int32,System.Single)", "the verified per-invocation camera/tile state reads and two TileBatch.SetLayer calls inside the rain and normal segment loops", "cache frame-local state and unchanged layer/stack selections only while the generic optimization policy is active", "IsWaterfallPresentationOptimizationEnabled"),
            new ClientPatchTarget("waterfall.solid-tile", "Terraria.WaterfallManager", "DrawWaterfall(System.Int32,System.Single)", "all verified WorldGen.SolidTile(Tile) calls with non-null tile preparation", "use the equivalent guarded local solidity fast path only while the generic optimization policy is active", "IsWaterfallPresentationOptimizationEnabled"),
            new ClientPatchTarget("waterfall.empty-pass", "Terraria.WaterfallManager", "DrawWaterfall(System.Int32,System.Single)", "the verified currentMax zero path and native ambient-state assignments", "preserve empty-pass ambient state and return before route-loop setup", "IsWaterfallPresentationOptimizationEnabled")),
        CreateDefinition(
            "patch.runtime.tile-drawing-presentation",
            PermanentPatchPlan.ApplyPermanentTileDrawingPresentation,
            "render.tile-drawing-presentation",
            "Terraria.GameContent.Drawing.TileDrawing",
            "Version-locked reduction for TileDrawing's unconditional glow-light lookup",
            new ClientPatchTarget("tile-drawing.activation-state", "Terraria.GameContent.Drawing.TileDrawing", "Draw(System.Boolean,System.Boolean,System.Int32)", "method entry before TileDrawing caches frame-local drawing state", "capture the generic optimization policy once for native helper fast paths", "IsTileDrawingPresentationOptimizationEnabled"),
            new ClientPatchTarget("tile-drawing.liquid-layer", "Terraria.GameContent.Drawing.TileDrawing", "DrawLiquidBehindTiles(System.Int32)", "the one verified TileBatch.SetLayer call repeated for each visible liquid-behind tile", "preserve the first native layer selection and skip only unchanged selections during this dedicated pass"),
            new ClientPatchTarget("tile-drawing.unused-light", "Terraria.GameContent.Drawing.TileDrawing", "GetTileDrawData(System.Int32,System.Int32,Terraria.Tile,System.UInt16,...)", "the single verified Lighting.GetColor(Int32, Int32) assignment consumed only by glow tile types 637 and 638", "avoid the lighting lookup for all other native tile types")),
        CreateDefinition(
            "patch.runtime.draw-orchestration",
            PermanentPatchPlan.ApplyPermanentDrawOrchestration,
            "render.draw-orchestration",
            "Terraria.Main",
            "Version-locked reductions for repeated draw orchestration work and transient draw-cache allocations",
            new ClientPatchTarget("draw.render-now-lighting-area", "Terraria.Main", "DoDraw(Microsoft.Xna.Framework.GameTime)", "the consecutive renderNow Lighting.LightTiles(GetAreaToLight()) calls after camera update", "reuse the first unchanged area only while the generic draw-orchestration policy is active", "IsDrawOrchestrationOptimizationEnabled"),
            new ClientPatchTarget("draw.baby-bird-cache-fast-path", "Terraria.Main", "SortBabyBirdProjectiles(...)", "the unique one-parameter private sort method before native temporary-list allocation", "skip native sorting when projectile type 759 is absent and the captured generic policy is active"),
            new ClientPatchTarget("draw.stardust-dragon-cache-fast-path", "Terraria.Main", "SortStardustDragonProjectiles(...)", "the unique one-parameter private sort method before native temporary-list allocation", "skip native sorting when projectile type 628 is absent and the captured generic policy is active")),
        CreateDefinition(
            "patch.runtime.laser-ruler-presentation",
            PermanentPatchPlan.ApplyPermanentLaserRulerPresentation,
            "render.laser-ruler-presentation",
            "Terraria.Main",
            "Version-locked batched mechanical laser-ruler presentation with native fallback",
            new ClientPatchTarget("laser-ruler.draw", "Terraria.Main", "DrawInterface_3_LaserRuler()", "the verified static ruler method with its native ReverseGravitySupport grid draws", "call the generic host renderer first and continue through vanilla when it declines", "TryDrawLaserRulerPresentation")),
        CreateDefinition(
            "patch.runtime.rain-presentation",
            PermanentPatchPlan.ApplyPermanentRainPresentation,
            "render.rain-presentation",
            "Terraria.Main",
            "Version-locked instanced rain presentation with native SpriteBatch fallback",
            new ClientPatchTarget("rain.presentation.draw", "Terraria.Main", "DrawRain()", "the one verified native SpriteBatch.Draw(Texture2D, Vector2, Rectangle?, Color, Single, Vector2, Single, SpriteEffects, Single) call", "wrap the native draw while retaining the exact Rain.Update loop position and restoring its known batch context", "TryBeginRainPresentation", "TryQueueRainPresentation", "EndRainPresentation")),
        CreateDefinition(
            "patch.runtime.lighting-parallelism",
            PermanentPatchPlan.ApplyPermanentLightingParallelism,
            "render.lighting-parallelism",
            "Terraria.Graphics.Light.LightMap / Terraria.Graphics.Light.TileLightScanner",
            "Version-locked balanced parallel scheduling for the native lighting blur and tile scan callbacks",
            new ClientPatchTarget("lighting.blur-ranges", "Terraria.Graphics.Light.LightMap", "BlurPass()", "the two exact ReLogic.Threading.FastParallel.For range calls for vertical and horizontal blur lines", "route through a generated native wrapper that retains FastParallel as its fallback", 2, "TryRunLightingParallel"),
            new ClientPatchTarget("lighting.export-ranges", "Terraria.Graphics.Light.TileLightScanner", "ExportTo(Rectangle, LightMap, TileLightScannerOptions)", "the one exact ReLogic.Threading.FastParallel.For range call for tile-light columns", "route through a generated native wrapper that retains FastParallel as its fallback", "TryRunLightingParallel")),
        CreateDefinition(
            "patch.runtime.static-tile-chunk-presentation",
            PermanentPatchPlan.ApplyPermanentStaticTileChunkPresentation,
            "render.static-tile-chunk-presentation",
            "Terraria.GameContent.Drawing.TileDrawing / Terraria.WorldGen / Terraria.Wiring / Terraria.Main",
            "Conservative fixed 20 by 20 static-tile descriptor cache with live native lighting and fallback",
            new ClientPatchTarget("static-tile-chunk.draw", "Terraria.GameContent.Drawing.TileDrawing", "Draw(System.Boolean,System.Boolean,System.Int32)", "the one verified DrawSingleTile invocation in the visible-tile loop", "replace the call with a wrapper that asks the generic host cache before using the untouched native method", "TryDrawStaticTileChunk"),
            new ClientPatchTarget("static-tile-chunk.network-mutation", "Terraria.Main", "OnTileChangeEvent(System.Int32,System.Int32,...)", "the shared native multiplayer tile-change event", "mark the affected static descriptor region dirty", "InvalidateStaticTileChunks")),
        CreateDefinition(
            "patch.runtime.chat-input-and-commands",
            PermanentPatchPlan.ApplyPermanentChatInputAndCommands,
            "runtime.chat-input-and-commands",
            "Terraria.Main / Terraria.Program",
            "Chat editing, command consumption, startup, and input formatting",
            new ClientPatchTarget("native-text.input-edit", "Terraria.Main", "GetInputText(String, Boolean)", "method entry and native fallback body", "try the core-owned generic text editor before retaining Terraria's original input path", "TryProcessNativeTextInput"),
            new ClientPatchTarget("native-text.caret", "Terraria.GameContent.UI.Elements.UITextBox", "DrawSelf(SpriteBatch)", "verified _cursor = Text.Length setup and UITextPanel base draw", "replace end-only cursor positioning and draw the retained selection without mutating stored text", "GetNativeTextInputCaretForField", "DrawNativeTextBoxSelectionForField"),
            new ClientPatchTarget("native-text.menu-presentation", "Terraria.Main", "DrawMenu(GameTime)", "the four verified menu String[] render loads after the first String measurement", "move Terraria's existing trailing input ticker to the generic edit-state caret for legacy menu fields", 4, "FormatNativeTextInputDisplay"),
            new ClientPatchTarget("native-text.reset", "Terraria.Main", "clrInput()", "method entry", "reset core-owned caret, selection, and repeat state with Terraria's text-input reset", "ResetNativeTextInput"),
            new ClientPatchTarget("chat.native-navigation", "Terraria.Main", "DoUpdate_HandleChat()", "verified independent Up and Down key branches before IChatMonitor.Offset", "guard each native direction independently without suppressing the other", 2, "ShouldHandleChatInputAction"),
            new ClientPatchTarget("chat.command-dispatch", "Terraria.Main", "DoUpdate_HandleChat()", "Main.chatText non-empty comparison and native close-chat path", "defer owned outgoing transforms, then record accepted input and consume handled commands; completed replacements are staged at the input boundary before this native path", "HasReadyOutgoingChatMessage", "TryDeferOutgoingChatMessage", "RecordSubmittedChatInput", "TryHandlePluginChatCommand"),
            new ClientPatchTarget("chat.bootstrap", "Terraria.Program", "LaunchGame(String[], Boolean)", "method entry", "insert before first instruction", "BootstrapPluginRuntime"),
            new ClientPatchTarget("chat.input-format", "Terraria.Main", "DrawPlayerChat()", "verified chatText capture into string local 2, editable-text draw, cursor literal/append region, and chat-monitor draw", "format input, draw selection behind snippets, remove vanilla cursor append, and append the generic host-owned chat action strip", "FormatPlayerChatText", "DrawNativePlayerChatSelection", "DrawChatActionStrip")),
        CreateDefinition(
            "patch.runtime.chat-display-and-interaction",
            PermanentPatchPlan.ApplyPermanentChatDisplayAndInteraction,
            "runtime.chat-display-and-interaction",
            "Terraria.UI.Chat.TextSnippet / Terraria.UI.Chat.ChatManager / Terraria.UI.Chat.ChatMessageContainer / Terraria.Chat.ChatHelper / Terraria.Main",
            "Chat decoration, display visibility, hover, click, color, and copy context",
            new ClientPatchTarget("chat.snippet-color", "Terraria.UI.Chat.TextSnippet", "GetVisibleColor()", "complete method body", "replace body", "GetChatSnippetVisibleColor"),
            new ClientPatchTarget("chat.snippet-hover", "Terraria.UI.Chat.TextSnippet", "OnHover()", "complete method body", "replace body", "HandleChatSnippetHover"),
            new ClientPatchTarget("chat.snippet-click", "Terraria.UI.Chat.TextSnippet", "OnClick()", "complete method body", "replace body", "HandleChatSnippetClick"),
            new ClientPatchTarget("chat.snippet-copy", "Terraria.UI.Chat.TextSnippet", "CopyMorph(String)", "final return", "insert copy-context callback before return", "CopyChatSnippetContext"),
            new ClientPatchTarget("chat.stored-message-decoration-scope", "Terraria.UI.Chat.ChatMessageContainer", "Refresh()", "verified OriginalText load and WordwrapStringSmart result store", "scope the shared parser to a stored chat-monitor message", "BeginStoredChatMessageDecorationForContainer", "EndStoredChatMessageDecoration"),
            new ClientPatchTarget("chat.stored-message-preparation", "Terraria.UI.Chat.ChatMessageContainer", "Refresh()", "verified OriginalText load immediately before WordwrapStringSmart", "prepare one complete retained message before Terraria wraps it into display fragments", "PrepareStoredChatMessageText"),
            new ClientPatchTarget("chat.stored-message-decoration", "Terraria.UI.Chat.ChatManager", "ParseMessage(String, Color)", "final return", "decorate only while ChatMessageContainer owns the parse scope", "DecorateStoredChatMessage"),
            new ClientPatchTarget("chat.stored-message-presentation-refresh", "Terraria.GameContent.UI.Chat.RemadeChatMonitor", "Update()", "method entry", "mark only host-updated retained messages for Terraria's normal rewrap before monitor update", "RefreshStoredChatMessagePresentations"),
            new ClientPatchTarget("chat.network-visibility", "Terraria.Chat.ChatHelper", "DisplayMessage(NetworkText, Color, Byte)", "method entry", "return gate using argument 2", "ShouldDisplayNetworkChatMessage"),
            new ClientPatchTarget("chat.local-visibility-text", "Terraria.Main", "NewText(String, Byte, Byte, Byte)", "method entry", "return gate", "ShouldDisplayLocalChatMessage"),
            new ClientPatchTarget("chat.local-visibility-multiline", "Terraria.Main", "NewTextMultiline(String, Boolean, Color, Int32)", "method entry", "return gate", "ShouldDisplayLocalChatMessage"))
    };

    private static ClientPatchDefinition CreateDefinition(string patchId, Action<ModuleDefinition, string> apply, string operationId, string targetType, string targetDescription, params ClientPatchTarget[] targets)
    {
        var bridgeMethods = GetBridgeMethods(targets);
        var operation = new ClientPatchOperation(operationId, targetType, targetDescription, targets, bridgeMethods);
        return new ClientPatchDefinition(patchId, apply, module => HasDefinitionPostconditions(module, targets), new[] { operation });
    }

    private static string[] GetBridgeMethods(IReadOnlyList<ClientPatchTarget> targets)
    {
        var bridgeMethods = new List<string>();
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var target = targets[targetIndex];
            for (var methodIndex = 0; methodIndex < target.BridgeMethods.Count; methodIndex++)
            {
                var bridgeMethod = target.BridgeMethods[methodIndex];
                if (!bridgeMethods.Contains(bridgeMethod, StringComparer.Ordinal))
                {
                    bridgeMethods.Add(bridgeMethod);
                }
            }
        }

        return bridgeMethods.ToArray();
    }

    internal static IReadOnlyList<ClientPatchDefinition> GetDefinitions() => Definitions;

    internal static List<ClientPatchResult> ApplyAll(ModuleDefinition module, string cleanSourcePath)
    {
        return ApplyDefinitions(module, cleanSourcePath, Definitions);
    }

    /// <summary>Runs the complete bridge and structural catalog against an already-written module.</summary>
    internal static void ValidatePostconditions(ModuleDefinition module)
    {
        if (module == null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        ValidateDefinitions(Definitions);
        for (int definitionIndex = 0; definitionIndex < Definitions.Length; definitionIndex++)
        {
            ClientPatchDefinition definition = Definitions[definitionIndex];
            if (!definition.IsPresent(module))
            {
                throw new ClientBuildException("Patched executable is missing postconditions for " + definition.Id + ".");
            }

            for (int operationIndex = 0; operationIndex < definition.Operations.Count; operationIndex++)
            {
                ValidateOperationPostcondition(module, definition.Operations[operationIndex]);
            }
        }
    }

    internal static List<ClientPatchResult> ApplyDefinitions(ModuleDefinition module, string cleanSourcePath, IReadOnlyList<ClientPatchDefinition> definitions)
    {
        ValidateDefinitions(definitions);
        var results = new List<ClientPatchResult>(definitions.Count);
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            if (definition.IsPresent(module))
            {
                throw new ClientBuildException("Patch " + definition.Id + " is already present. Client generation requires an unmodified supported Terraria.exe source.");
            }

            try
            {
                ValidateOperationPreconditions(module, definition);
                definition.Apply(module, cleanSourcePath);
            }
            catch (ClientBuildException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ClientBuildException("Patch " + definition.Id + " failed: " + exception.Message);
            }

            if (!definition.IsPresent(module))
            {
                try
                {
                    for (var operationIndex = 0; operationIndex < definition.Operations.Count; operationIndex++)
                    {
                        ValidateOperationPostcondition(module, definition.Operations[operationIndex]);
                    }
                }
                catch (ClientBuildException exception)
                {
                    throw new ClientBuildException(
                        "Patch " + definition.Id + " completed without satisfying its verified postcondition: " + exception.Message);
                }

                throw new ClientBuildException("Patch " + definition.Id + " completed without producing its verified runtime bridge call.");
            }

            for (var operationIndex = 0; operationIndex < definition.Operations.Count; operationIndex++)
            {
                var operation = definition.Operations[operationIndex];
                ValidateOperationPostcondition(module, operation);
                results.Add(new ClientPatchResult(operation.Id, ClientPatchStatus.Applied, operation.TargetType + ": " + operation.TargetDescription));
            }
        }

        return results;
    }

    internal static void ValidateCatalog()
    {
        ValidateDefinitions(Definitions);
        ValidateBridgeImports(PermanentPatchPlan.GetImportedBridgeMethods());
        for (var definitionIndex = 0; definitionIndex < Definitions.Length; definitionIndex++)
        {
            var operations = Definitions[definitionIndex].Operations;
            for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                if (operations[operationIndex].Targets.Count == 0)
                {
                    throw new ClientBuildException("Permanent patch catalog operation " + operations[operationIndex].Id + " has no detailed target inventory.");
                }
            }
        }
    }

    internal static void ValidateImportedBridgeMethod(string bridgeMethod)
    {
        if (string.IsNullOrWhiteSpace(bridgeMethod))
        {
            throw new ClientBuildException("Permanent patch plan attempted to import a missing bridge method name.");
        }

        for (int definitionIndex = 0; definitionIndex < Definitions.Length; definitionIndex++)
        {
            IReadOnlyList<ClientPatchOperation> operations = Definitions[definitionIndex].Operations;
            for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                if (operations[operationIndex].BridgeMethods.Contains(bridgeMethod, StringComparer.Ordinal))
                {
                    return;
                }
            }
        }

        throw new ClientBuildException("Permanent patch plan imports bridge method '" + bridgeMethod + "' without a catalog target postcondition.");
    }

    internal static void ValidateBridgeImports(IReadOnlyList<string> importedBridgeMethods)
    {
        if (importedBridgeMethods == null)
        {
            throw new ArgumentNullException(nameof(importedBridgeMethods));
        }

        var imports = new HashSet<string>(importedBridgeMethods, StringComparer.Ordinal);
        if (imports.Count != importedBridgeMethods.Count)
        {
            throw new ClientBuildException("Permanent patch plan imports the same bridge method more than once in its authoritative import catalog.");
        }

        var targets = new HashSet<string>(StringComparer.Ordinal);
        for (int definitionIndex = 0; definitionIndex < Definitions.Length; definitionIndex++)
        {
            IReadOnlyList<ClientPatchOperation> operations = Definitions[definitionIndex].Operations;
            for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                IReadOnlyList<string> bridgeMethods = operations[operationIndex].BridgeMethods;
                for (int methodIndex = 0; methodIndex < bridgeMethods.Count; methodIndex++)
                {
                    targets.Add(bridgeMethods[methodIndex]);
                }
            }
        }

        if (!imports.SetEquals(targets))
        {
            string missingFromCatalog = string.Join(", ", imports.Except(targets).OrderBy(value => value, StringComparer.Ordinal));
            string missingFromPlan = string.Join(", ", targets.Except(imports).OrderBy(value => value, StringComparer.Ordinal));
            throw new ClientBuildException(
                "Permanent patch bridge catalog drift detected. Missing catalog targets: [" + missingFromCatalog +
                "]; catalog-only targets: [" + missingFromPlan + "].");
        }
    }

    internal static void ValidateDefinitions(IReadOnlyList<ClientPatchDefinition> definitions)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < definitions.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(definitions[index].Id) || !ids.Add(definitions[index].Id))
            {
                throw new ClientBuildException("Permanent patch catalog contains a missing or duplicate patch ID.");
            }

            indexes.Add(definitions[index].Id, index);

            for (var operationIndex = 0; operationIndex < definitions[index].Operations.Count; operationIndex++)
            {
                var operation = definitions[index].Operations[operationIndex];
                if (string.IsNullOrWhiteSpace(operation.Id) ||
                    !ids.Add(operation.Id) ||
                    (!operation.StructuralOnly && operation.BridgeMethods.Count == 0))
                {
                    throw new ClientBuildException("Permanent patch catalog contains a missing or duplicate operation ID or an operation with no postcondition.");
                }

                var targetIds = new HashSet<string>(StringComparer.Ordinal);
                for (var targetIndex = 0; targetIndex < operation.Targets.Count; targetIndex++)
                {
                    var target = operation.Targets[targetIndex];
                    if (string.IsNullOrWhiteSpace(target.Id) ||
                        !targetIds.Add(target.Id) ||
                        string.IsNullOrWhiteSpace(target.TypeName) ||
                        string.IsNullOrWhiteSpace(target.MemberSignature) ||
                        string.IsNullOrWhiteSpace(target.Anchor) ||
                        string.IsNullOrWhiteSpace(target.Injection) ||
                        string.IsNullOrWhiteSpace(target.Precondition) ||
                        string.IsNullOrWhiteSpace(target.Postcondition))
                    {
                        throw new ClientBuildException("Permanent patch catalog contains an incomplete or duplicate detailed target for operation " + operation.Id + ".");
                    }
                }
            }
        }

        for (var index = 0; index < definitions.Count; index++)
        {
            var dependencies = definitions[index].Dependencies;
            for (var dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++)
            {
                var dependency = dependencies[dependencyIndex];
                if (!indexes.TryGetValue(dependency, out var dependencyPosition))
                {
                    throw new ClientBuildException("Patch " + definitions[index].Id + " depends on missing patch " + dependency + ".");
                }
                if (dependencyPosition >= index)
                {
                    throw new ClientBuildException("Patch " + definitions[index].Id + " has a cyclic or non-deterministic dependency on " + dependency + ". Dependencies must appear earlier in the explicit catalog.");
                }
            }
        }
    }

    internal static bool HasRuntimeBridgeCall(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            if (HasRuntimeBridgeCall(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBridgeMethodCalls(ModuleDefinition module, IReadOnlyList<string> methodNames)
    {
        for (var index = 0; index < methodNames.Count; index++)
        {
            if (!HasBridgeMethodCall(module, methodNames[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDefinitionPostconditions(ModuleDefinition module, IReadOnlyList<ClientPatchTarget> targets)
    {
        try
        {
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                ClientPatchTarget target = targets[targetIndex];
                TypeDefinition type = CecilPatchPrimitives.RequireType(module, target.TypeName);
                IReadOnlyList<MethodDefinition> methods = ResolveTargetMethods(type, target);
                ValidateTargetStructuralPostcondition(target, type, methods);
                for (int bridgeIndex = 0; bridgeIndex < target.BridgeMethods.Count; bridgeIndex++)
                {
                    ValidateTargetBridgePostcondition(target, methods, target.BridgeMethods[bridgeIndex]);
                }
            }

            return true;
        }
        catch (ClientBuildException)
        {
            return false;
        }
    }

    private static void ValidateOperationPostcondition(ModuleDefinition module, ClientPatchOperation operation)
    {
        for (int targetIndex = 0; targetIndex < operation.Targets.Count; targetIndex++)
        {
            ClientPatchTarget target = operation.Targets[targetIndex];
            TypeDefinition type = CecilPatchPrimitives.RequireType(module, target.TypeName);
            IReadOnlyList<MethodDefinition> targetMethods = ResolveTargetMethods(type, target);
            ValidateTargetStructuralPostcondition(target, type, targetMethods);
            for (int bridgeIndex = 0; bridgeIndex < target.BridgeMethods.Count; bridgeIndex++)
            {
                string bridgeMethod = target.BridgeMethods[bridgeIndex];
                ValidateTargetBridgePostcondition(target, targetMethods, bridgeMethod);
            }
        }
    }

    private static void ValidateOperationPreconditions(ModuleDefinition module, ClientPatchDefinition definition)
    {
        for (int operationIndex = 0; operationIndex < definition.Operations.Count; operationIndex++)
        {
            ClientPatchOperation operation = definition.Operations[operationIndex];
            for (int targetIndex = 0; targetIndex < operation.Targets.Count; targetIndex++)
            {
                ClientPatchTarget target = operation.Targets[targetIndex];
                if (target.GeneratedMember)
                {
                    continue;
                }

                TypeDefinition type = CecilPatchPrimitives.RequireType(module, target.TypeName);
                _ = ResolveTargetMethods(type, target);
            }
        }
    }

    private static IReadOnlyList<MethodDefinition> ResolveTargetMethods(TypeDefinition type, ClientPatchTarget target)
    {
        var methods = new List<MethodDefinition>();
        string[] alternatives = target.MemberSignature.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (alternatives.Length != 1)
        {
            throw new ClientBuildException("Patch target " + target.Id + " must name one exact method. Split grouped target signatures into independently verified patch sites.");
        }
        for (int index = 0; index < alternatives.Length; index++)
        {
            string candidate = alternatives[index].Trim();
            int argumentStart = candidate.IndexOf('(');
            int argumentEnd = candidate.LastIndexOf(')');
            if (argumentStart <= 0 || argumentEnd != candidate.Length - 1)
            {
                throw new ClientBuildException("Patch target " + target.Id + " has an invalid member signature: " + target.MemberSignature + ".");
            }

            string name = candidate.Substring(0, argumentStart).Trim();
            if (name.Length == 0)
            {
                throw new ClientBuildException("Patch target " + target.Id + " has an invalid member signature: " + target.MemberSignature + ".");
            }

            string parameters = candidate.Substring(argumentStart + 1, argumentEnd - argumentStart - 1).Trim();
            bool usesEllipsis = parameters.IndexOf("...", StringComparison.Ordinal) >= 0;
            string[] parameterTypes = usesEllipsis || parameters.Length == 0
                ? Array.Empty<string>()
                : parameters.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            MethodDefinition? match = null;
            for (int methodIndex = 0; methodIndex < type.Methods.Count; methodIndex++)
            {
                MethodDefinition method = type.Methods[methodIndex];
                if (!method.HasBody || !string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!usesEllipsis && !MatchesTargetParameters(method, parameterTypes))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new ClientBuildException("Patch target " + target.Id + " resolves ambiguously to multiple methods in " + type.FullName + ".");
                }

                match = method;
            }

            if (match == null)
            {
                throw new ClientBuildException("Patch target " + target.Id + " could not resolve " + type.FullName + "::" + candidate + ".");
            }

            methods.Add(match);
        }

        if (methods.Count == 0)
        {
            throw new ClientBuildException("Patch target " + target.Id + " does not identify a target member.");
        }

        return methods;
    }

    private static bool MatchesTargetParameters(MethodDefinition method, IReadOnlyList<string> expectedTypes)
    {
        if (method.Parameters.Count != expectedTypes.Count)
        {
            return false;
        }

        for (int parameterIndex = 0; parameterIndex < expectedTypes.Count; parameterIndex++)
        {
            string expected = expectedTypes[parameterIndex].Trim();
            string actual = method.Parameters[parameterIndex].ParameterType.FullName;
            if (!string.Equals(actual, expected, StringComparison.Ordinal) &&
                !actual.EndsWith("." + expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasBridgeMethodCall(IReadOnlyList<MethodDefinition> targetMethods, string bridgeMethod)
    {
        for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
        {
            MethodDefinition method = targetMethods[methodIndex];
            for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].Operand is MethodReference reference &&
                    string.Equals(reference.DeclaringType.FullName, BridgeAbiContractCatalog.FacadeTypeName, StringComparison.Ordinal) &&
                    string.Equals(reference.Name, bridgeMethod, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A successful Cecil write is not enough: each target has a deliberately small, exact ABI
    /// footprint. Counting calls detects duplicate application, and the melee hook additionally
    /// proves that every return remains covered after branch/EH retargeting.
    /// </summary>
    private static void ValidateTargetBridgePostcondition(ClientPatchTarget target, IReadOnlyList<MethodDefinition> targetMethods, string bridgeMethod)
    {
        if (string.Equals(target.Id, "static-tile-chunk.draw", StringComparison.Ordinal))
        {
            ValidateStaticTileChunkWrapperBridgeCall(target, targetMethods, bridgeMethod);
            return;
        }

        if (string.Equals(target.Id, "rain.presentation.draw", StringComparison.Ordinal))
        {
            ValidateRainPresentationBridgeCall(target, targetMethods, bridgeMethod);
            return;
        }

        if (string.Equals(target.Id, "lighting.blur-ranges", StringComparison.Ordinal) ||
            string.Equals(target.Id, "lighting.export-ranges", StringComparison.Ordinal))
        {
            ValidateLightingParallelBridgeCall(target, targetMethods, bridgeMethod);
            return;
        }

        bool allReturns = target.BridgeCallMode == ClientPatchPostconditionMode.BeforeEveryReturn;
        int expected = allReturns ? CountReturns(targetMethods) : target.ExpectedBridgeCallCount;
        int actual = CountBridgeMethodCalls(targetMethods, bridgeMethod);
        if (actual != expected)
        {
            throw new ClientBuildException(
                "Patch target " + target.Id + " expected " + expected + " call(s) to " + bridgeMethod +
                " but found " + actual + " in " + target.TypeName + "::" + target.MemberSignature + ".");
        }

        if (allReturns)
        {
            for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
            {
                MethodDefinition method = targetMethods[methodIndex];
                for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
                {
                    Instruction instruction = method.Body.Instructions[instructionIndex];
                    if (instruction.OpCode != OpCodes.Ret)
                    {
                        continue;
                    }

                    Instruction? previous = instruction.Previous;
                    if (!IsBridgeMethodCall(previous, bridgeMethod))
                    {
                        throw new ClientBuildException(
                            "Patch target " + target.Id + " does not invoke " + bridgeMethod +
                            " immediately before every return in " + method.FullName + ".");
                    }
                }
            }
        }
    }

    private static void ValidateStaticTileChunkWrapperBridgeCall(
        ClientPatchTarget target,
        IReadOnlyList<MethodDefinition> targetMethods,
        string bridgeMethod)
    {
        TypeDefinition type = targetMethods[0].DeclaringType;
        MethodDefinition? wrapper = type.Methods.SingleOrDefault(method =>
            string.Equals(method.Name, "AlacrityDrawStaticChunkAwareTile", StringComparison.Ordinal));
        if (wrapper == null || !wrapper.HasBody)
        {
            throw new ClientBuildException("Patch target " + target.Id + " did not create its verified static tile wrapper.");
        }

        int actual = CountBridgeMethodCalls(new[] { wrapper }, bridgeMethod);
        if (actual != target.ExpectedBridgeCallCount)
        {
            throw new ClientBuildException(
                "Patch target " + target.Id + " expected " + target.ExpectedBridgeCallCount +
                " call(s) to " + bridgeMethod + " in its generated wrapper but found " + actual + ".");
        }
    }

    private static void ValidateRainPresentationBridgeCall(
        ClientPatchTarget target,
        IReadOnlyList<MethodDefinition> targetMethods,
        string bridgeMethod)
    {
        TypeDefinition type = targetMethods[0].DeclaringType;
        IReadOnlyList<MethodDefinition> expectedLocation;
        if (string.Equals(bridgeMethod, "TryQueueRainPresentation", StringComparison.Ordinal))
        {
            MethodDefinition? wrapper = type.Methods.SingleOrDefault(method =>
                string.Equals(method.Name, "AlacrityDrawRainSprite", StringComparison.Ordinal));
            if (wrapper == null || !wrapper.HasBody)
            {
                throw new ClientBuildException("Patch target " + target.Id + " did not create its verified rain SpriteBatch wrapper.");
            }

            expectedLocation = new[] { wrapper };
        }
        else
        {
            expectedLocation = targetMethods;
        }

        int actual = CountBridgeMethodCalls(expectedLocation, bridgeMethod);
        if (actual != target.ExpectedBridgeCallCount)
        {
            throw new ClientBuildException(
                "Patch target " + target.Id + " expected " + target.ExpectedBridgeCallCount +
                " call(s) to " + bridgeMethod + " in its verified rain presentation location but found " + actual + ".");
        }
    }

    private static void ValidateLightingParallelBridgeCall(
        ClientPatchTarget target,
        IReadOnlyList<MethodDefinition> targetMethods,
        string bridgeMethod)
    {
        TypeDefinition type = targetMethods[0].DeclaringType;
        MethodDefinition? wrapper = type.Methods.SingleOrDefault(method =>
            string.Equals(method.Name, "AlacrityRunLightingParallel", StringComparison.Ordinal));
        if (wrapper == null || !wrapper.HasBody)
        {
            throw new ClientBuildException("Patch target " + target.Id + " did not create its verified lighting wrapper.");
        }

        int bridgeCalls = CountBridgeMethodCalls(new[] { wrapper }, bridgeMethod);
        if (bridgeCalls != 1)
        {
            throw new ClientBuildException(
                "Patch target " + target.Id + " expected one call to " + bridgeMethod +
                " in its generated wrapper but found " + bridgeCalls + ".");
        }

        int wrapperCalls = CountMethodCalls(targetMethods, "AlacrityRunLightingParallel");
        if (wrapperCalls != target.ExpectedBridgeCallCount)
        {
            throw new ClientBuildException(
                "Patch target " + target.Id + " expected " + target.ExpectedBridgeCallCount +
                " calls to its generated wrapper but found " + wrapperCalls + ".");
        }
    }

    private static void ValidateTargetStructuralPostcondition(
        ClientPatchTarget target,
        TypeDefinition type,
        IReadOnlyList<MethodDefinition> methods)
    {
        // The catalog is intentionally target-local rather than a general IL verifier. These checks
        // cover permanent mutations that cannot be proven by a PluginUiRuntime call alone.
        switch (target.Id)
        {
            case "menu.version-labels":
                RequireVersionLabel(methods[0], "versionNumber");
                RequireVersionLabel(methods[0], "versionNumber2");
                return;

            case "paint.pending-tile":
            case "paint.pending-cage":
            case "paint.pending-wall":
            case "paint.pending-tree-top":
            case "paint.pending-tree-branch":
                RequireGeneratedMembers(
                    CecilPatchPrimitives.RequireType(type.Module, "Terraria.GameContent.TilePaintSystemV2/ARenderTargetHolder"),
                    "alacrityPendingPaintPreparation",
                    "TryMarkAlacrityPaintPreparationPending",
                    "ClearAlacrityPaintPreparationPending");
                return;

            case "paint.lazy-unpainted-scan":
            case "paint.extra-preparation-prefilter":
                RequireGeneratedMembers(
                    type,
                    "alacrityPaintPreparationOptimizationEnabled");
                return;

            case "clothing.dictionary-capacity":
                RequireDictionaryCapacityMutation(methods[0]);
                return;

            case "clothing.discovery-deduplication":
                RequireGeneratedMembers(
                    type,
                    "alacrityClothingEntityPresentationOptimizationEnabled",
                    "alacrityDisplayDollLastPointValid",
                    "alacrityHatRackLastPointValid");
                return;

            case "clothing.post-draw":
                RequireGeneratedMembers(
                    type,
                    "DrawEntities_AlacrityHatRacks",
                    "DrawEntities_AlacrityDisplayDolls",
                    "DrawEntities_AlacrityHatRackEntries",
                    "DrawEntities_AlacrityDisplayDollEntries");
                RequireMethodCall(methods[0], "DrawEntities_AlacrityHatRacks");
                RequireMethodCall(methods[0], "DrawEntities_AlacrityDisplayDolls");
                return;

            case "rain.presentation.draw":
                RequireGeneratedMembers(type, "alacrityRainUsesWorldTransform", "AlacrityDrawRainSprite");
                RequireMethodCall(methods[0], "AlacrityDrawRainSprite");
                return;

            case "lighting.blur-ranges":
            case "lighting.export-ranges":
                RequireGeneratedMembers(type, "AlacrityRunLightingParallel");
                RequireMethodCall(methods[0], "AlacrityRunLightingParallel");
                return;

            case "waterfall.discovery-reuse":
                RequireGeneratedMembers(
                    type,
                    "alacrityWaterfallDiscoveryValid",
                    "alacrityWaterfallDiscoveryDirty",
                    "AlacrityTryReuseWaterfallDiscovery",
                    "AlacrityRememberWaterfallDiscovery");
                RequireMethodCall(methods[0], "AlacrityTryReuseWaterfallDiscovery");
                RequireMethodCall(methods[0], "AlacrityRememberWaterfallDiscovery");
                return;

            case "waterfall.discovery-invalidation":
            case "waterfall.discovery-liquid-invalidation":
            case "waterfall.discovery-buffered-liquid-invalidation":
                RequireGeneratedMembers(
                    CecilPatchPrimitives.RequireType(type.Module, "Terraria.WaterfallManager"),
                    "AlacrityInvalidateWaterfallDiscovery");
                RequireMethodCall(methods[0], "AlacrityInvalidateWaterfallDiscovery");
                return;

            case "waterfall.layer-state":
                RequireGeneratedMembers(type, "alacrityWaterfallLayerInitialized", "AlacritySetWaterfallLayer");
                RequireMethodCall(methods[0], "AlacritySetWaterfallLayer");
                return;

            case "waterfall.solid-tile":
                RequireGeneratedMembers(type, "AlacrityIsWaterfallSolidTile");
                RequireMethodCall(methods[0], "AlacrityIsWaterfallSolidTile");
                return;

            case "waterfall.empty-pass":
                RequireEmptyWaterfallFastPath(methods[0]);
                return;

            case "tile-drawing.activation-state":
            case "tile-drawing.liquid-layer":
            case "tile-drawing.unused-light":
                RequireGeneratedMembers(
                    type,
                    "alacrityTileDrawingOptimizationEnabled",
                    "alacrityLiquidBehindLayerInitialized",
                    "AlacrityGetTileDrawDataLight",
                    "AlacritySetLiquidBehindLayer");
                return;

            case "static-tile-chunk.draw":
                RequireGeneratedMembers(type, "AlacrityDrawStaticChunkAwareTile");
                RequireMethodCall(methods[0], "AlacrityDrawStaticChunkAwareTile");
                return;

            case "static-tile-chunk.network-mutation":
                RequireMethodCall(methods[0], "InvalidateStaticTileChunks");
                return;

            case "draw.render-now-lighting-area":
                TypeDefinition main = CecilPatchPrimitives.RequireType(type.Module, "Terraria.Main");
                RequireGeneratedMembers(main, "alacrityDrawOrchestrationOptimizationEnabled", "AlacrityShouldSortProjectileCache");
                return;

            case "draw.baby-bird-cache-fast-path":
            case "draw.stardust-dragon-cache-fast-path":
                TypeDefinition projectileMain = CecilPatchPrimitives.RequireType(type.Module, "Terraria.Main");
                RequireGeneratedMembers(projectileMain, "AlacrityShouldSortProjectileCache");
                RequireMethodCall(methods[0], "AlacrityShouldSortProjectileCache");
                return;

            default:
                if (methods.Count == 0 || !methods[0].HasBody)
                {
                    throw new ClientBuildException("Patch target " + target.Id + " no longer has a verified method body.");
                }

                return;
        }
    }

    private static void RequireGeneratedMembers(TypeDefinition type, params string[] names)
    {
        for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            string name = names[nameIndex];
            bool exists = type.Fields.Any(field => string.Equals(field.Name, name, StringComparison.Ordinal)) ||
                type.Methods.Any(method => string.Equals(method.Name, name, StringComparison.Ordinal));
            if (!exists)
            {
                throw new ClientBuildException("Patch postcondition is missing generated member '" + name + "' on " + type.FullName + ".");
            }
        }
    }

    private static void RequireMethodCall(MethodDefinition method, string name)
    {
        for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
        {
            if (method.Body.Instructions[instructionIndex].Operand is MethodReference reference &&
                string.Equals(reference.Name, name, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new ClientBuildException("Patch postcondition is missing '" + name + "' in " + method.FullName + ".");
    }

    private static int CountMethodCalls(IReadOnlyList<MethodDefinition> methods, string name)
    {
        int count = 0;
        for (int methodIndex = 0; methodIndex < methods.Count; methodIndex++)
        {
            MethodDefinition method = methods[methodIndex];
            for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].Operand is MethodReference reference &&
                    string.Equals(reference.Name, name, StringComparison.Ordinal))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void RequireDictionaryCapacityMutation(MethodDefinition constructor)
    {
        for (int index = 1; index < constructor.Body.Instructions.Count; index++)
        {
            Instruction instruction = constructor.Body.Instructions[index];
            if (instruction.Operand is MethodReference reference &&
                string.Equals(reference.DeclaringType.FullName, "System.Collections.Generic.Dictionary`2<Microsoft.Xna.Framework.Point,System.Int32>", StringComparison.Ordinal) &&
                string.Equals(reference.Name, ".ctor", StringComparison.Ordinal) &&
                reference.Parameters.Count == 1 &&
                constructor.Body.Instructions[index - 1].OpCode == OpCodes.Ldc_I4 &&
                Equals(constructor.Body.Instructions[index - 1].Operand, 2048))
            {
                return;
            }
        }

        throw new ClientBuildException("Patch postcondition did not find the verified clothing dictionary capacity constructor.");
    }

    private static void RequireVersionLabel(MethodDefinition constructor, string fieldName)
    {
        for (int index = 1; index < constructor.Body.Instructions.Count; index++)
        {
            Instruction instruction = constructor.Body.Instructions[index];
            if (instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference field &&
                string.Equals(field.Name, fieldName, StringComparison.Ordinal) &&
                instruction.Previous != null && instruction.Previous.OpCode == OpCodes.Ldstr &&
                string.Equals(instruction.Previous.Operand as string, "Terraria v1.4.5.6", StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new ClientBuildException("Patch postcondition is missing the Terraria version label mutation for " + fieldName + ".");
    }

    private static void RequireEmptyWaterfallFastPath(MethodDefinition method)
    {
        // The fast path is verified by its observable reset stores, not merely by unrelated
        // waterfall helpers which may also appear elsewhere in DrawWaterfall.
        RequireStaticFieldStore(method, "drewLava");
        RequireStaticFieldStore(method, "ambientWaterfallX");
        RequireStaticFieldStore(method, "ambientWaterfallY");
        RequireStaticFieldStore(method, "ambientWaterfallStrength");
        RequireStaticFieldStore(method, "ambientLavafallX");
        RequireStaticFieldStore(method, "ambientLavafallY");
        RequireStaticFieldStore(method, "ambientLavafallStrength");

        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            Instruction instruction = method.Body.Instructions[index];
            if (instruction.OpCode == OpCodes.Ret && instruction.Previous != null && instruction.Previous.OpCode == OpCodes.Stelem_I1)
            {
                return;
            }
        }

        throw new ClientBuildException("Waterfall empty-pass postcondition is missing the tileSolid[546] restoration before its injected return.");
    }

    private static void RequireStaticFieldStore(MethodDefinition method, string fieldName)
    {
        for (int index = 0; index < method.Body.Instructions.Count; index++)
        {
            Instruction instruction = method.Body.Instructions[index];
            if (instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference field &&
                string.Equals(field.DeclaringType.FullName, "Terraria.Main", StringComparison.Ordinal) &&
                string.Equals(field.Name, fieldName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new ClientBuildException("Waterfall empty-pass postcondition is missing the Terraria.Main." + fieldName + " reset.");
    }

    private static int CountReturns(IReadOnlyList<MethodDefinition> targetMethods)
    {
        int count = 0;
        for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
        {
            MethodDefinition method = targetMethods[methodIndex];
            for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].OpCode == OpCodes.Ret)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountBridgeMethodCalls(IReadOnlyList<MethodDefinition> targetMethods, string bridgeMethod)
    {
        int count = 0;
        for (int methodIndex = 0; methodIndex < targetMethods.Count; methodIndex++)
        {
            MethodDefinition method = targetMethods[methodIndex];
            for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (IsBridgeMethodCall(method.Body.Instructions[instructionIndex], bridgeMethod))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsBridgeMethodCall(Instruction? instruction, string bridgeMethod)
    {
        return instruction != null &&
            instruction.Operand is MethodReference reference &&
            string.Equals(reference.DeclaringType.FullName, BridgeAbiContractCatalog.FacadeTypeName, StringComparison.Ordinal) &&
            string.Equals(reference.Name, bridgeMethod, StringComparison.Ordinal);
    }

    private static bool HasBridgeMethodCall(ModuleDefinition module, string methodName)
    {
        foreach (var type in module.Types)
        {
            if (HasBridgeMethodCall(type, methodName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRuntimeBridgeCall(TypeDefinition type)
    {
        for (var methodIndex = 0; methodIndex < type.Methods.Count; methodIndex++)
        {
            var method = type.Methods[methodIndex];
            if (!method.HasBody)
            {
                continue;
            }

            for (var instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                var instruction = method.Body.Instructions[instructionIndex];
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference reference &&
                    string.Equals(reference.DeclaringType.FullName, "AlacrityTerraria.PluginUiRuntime", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        for (var nestedIndex = 0; nestedIndex < type.NestedTypes.Count; nestedIndex++)
        {
            if (HasRuntimeBridgeCall(type.NestedTypes[nestedIndex]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBridgeMethodCall(TypeDefinition type, string methodName)
    {
        for (var methodIndex = 0; methodIndex < type.Methods.Count; methodIndex++)
        {
            var method = type.Methods[methodIndex];
            if (!method.HasBody)
            {
                continue;
            }

            for (var instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
            {
                if (method.Body.Instructions[instructionIndex].Operand is MethodReference reference &&
                    string.Equals(reference.DeclaringType.FullName, "AlacrityTerraria.PluginUiRuntime", StringComparison.Ordinal) &&
                    string.Equals(reference.Name, methodName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        for (var nestedIndex = 0; nestedIndex < type.NestedTypes.Count; nestedIndex++)
        {
            if (HasBridgeMethodCall(type.NestedTypes[nestedIndex], methodName))
            {
                return true;
            }
        }

        return false;
    }
}
