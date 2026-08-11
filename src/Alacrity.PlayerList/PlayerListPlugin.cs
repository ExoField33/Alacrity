using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Alacrity.PluginSdk;

namespace Alacrity.PlayerList;

/// <summary>Owns Player List settings and renders the list through generic snapshot and HUD services.</summary>
public sealed class PlayerListPlugin : IAlacrityPlugin, IPlayerListService
{
    private const int CyborgNpcId = 209;
    private static readonly Regex TerrariaTagRegex = new Regex("\\[[a-zA-Z]+(?:/[^:\\]]+)?[:]([^\\]]*)\\]", RegexOptions.Compiled);
    private static readonly PluginOverlayColor PanelColor = new PluginOverlayColor(33, 15, 91, 230);
    private static readonly PluginOverlayColor LightRowColor = new PluginOverlayColor(48, 36, 112, 95);
    private static readonly PluginOverlayColor DarkRowColor = new PluginOverlayColor(21, 14, 64, 80);
    private static readonly PluginOverlayColor White = new PluginOverlayColor(255, 255, 255);

    private IPluginSetting<int>? playersPerColumnSetting;
    private IPluginSetting<int>? rowWidthSetting;
    private IPluginSetting<int>? textScaleSetting;
    private IPluginSetting<bool>? showPlayerHeadsSetting;
    private IPluginSetting<bool>? showPingSetting;
    private IPluginSetting<bool>? hideBotsSetting;
    private IPluginSetting<string>? sortModeSetting;
    private IPluginSetting<string>? displayModeSetting;
    private IPluginPlayerService? players;
    private IPluginPlayerSnapshotDemandService? playerSnapshotDemand;
    private IPluginSessionPresentationService? session;
    private IPluginHudService? hud;
    private IPluginRegistration? hudRegistration;
    private IPluginRegistration? botClassificationDemand;
    private readonly List<PluginPlayerSnapshot> playerSnapshots = new List<PluginPlayerSnapshot>(256);
    private readonly List<Row> rows = new List<Row>(256);
    private readonly List<int> columnStarts = new List<int>(32);
    private readonly List<int> columnCounts = new List<int>(32);
    private bool visible;
    private int playersPerColumn = 14;
    private int rowWidth = 260;
    private int textScalePercent = 120;
    private bool showPlayerHeads = true;
    private bool showPing = true;
    private bool hideBots;
    private bool botClassificationInitialized;
    private bool waitingForBotClassification;
    private long requestedBotClassificationVersion;
    private long observedBotClassificationVersion;
    private PlayerListSortMode sortMode;
    private PlayerListDisplayMode displayMode;
    private int nextRosterRefreshTick;
    private bool rosterDirty = true;

    public bool IsVisible => visible;
    public int PlayersPerColumn => playersPerColumn;
    public int RowWidth => rowWidth;
    public float TextScale => textScalePercent / 100f;
    public bool ShowPlayerHeads => showPlayerHeads;
    public bool ShowPing => showPing;
    public bool HideBots => hideBots;
    public PlayerListSortMode SortMode => sortMode;

    public void Initialize(IPluginContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        players = context.Terraria.Players;
        session = context.Terraria.Session;
        hud = context.Hud;
        playerSnapshotDemand = players as IPluginPlayerSnapshotDemandService;

        RegisterSettings(context);
        ReadSettings();
        SubscribeToSettings();
        RegisterPresentation(context);
    }

    public void Enable()
    {
    }

    public void Disable()
    {
        visible = false;
        botClassificationInitialized = false;
        waitingForBotClassification = false;
        ClearRoster();
        ReleaseRuntimeServices();
    }

    public void Shutdown()
    {
        Disable();
        playersPerColumnSetting = null;
        rowWidthSetting = null;
        textScaleSetting = null;
        showPlayerHeadsSetting = null;
        showPingSetting = null;
        hideBotsSetting = null;
        sortModeSetting = null;
        displayModeSetting = null;
    }

    public void CycleSortMode()
    {
        sortMode = sortMode switch
        {
            PlayerListSortMode.Alphabetical => PlayerListSortMode.Team,
            PlayerListSortMode.Team => PlayerListSortMode.Health,
            _ => PlayerListSortMode.Alphabetical
        };

        if (sortModeSetting != null)
        {
            sortModeSetting.Value = sortMode.ToString();
        }
    }

    public void ToggleBotFiltering()
    {
        hideBots = !hideBots;
        if (hideBotsSetting != null)
        {
            hideBotsSetting.Value = hideBots;
        }

        if (hideBots)
        {
            botClassificationInitialized = true;
            RequestBotClassification();
        }
    }

    public void ToggleVisibility()
    {
        SetVisible(!visible);
    }

    public void SetVisibility(bool isVisible)
    {
        SetVisible(isVisible);
    }

    private void RegisterSettings(IPluginContext context)
    {
        playersPerColumnSetting = context.Settings.Register(new PluginSettingDefinition<int>("playersPerColumn", 14, value => Clamp(value, 8, 20)));
        rowWidthSetting = context.Settings.Register(new PluginSettingDefinition<int>("rowWidth", 260, value => Clamp(value, 180, 420)));
        textScaleSetting = context.Settings.Register(new PluginSettingDefinition<int>("textScalePercent", 120, value => Clamp(value, 80, 160)));
        showPlayerHeadsSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showPlayerHeads", true));
        showPingSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showPing", true));
        hideBotsSetting = context.Settings.Register(new PluginSettingDefinition<bool>("hideBots", false));
        sortModeSetting = context.Settings.Register(new PluginSettingDefinition<string>("sortMode", "Alphabetical", value => ReadSortMode(value).ToString()));
        displayModeSetting = context.Settings.Register(new PluginSettingDefinition<string>("displayMode", "Hold", value => ReadDisplayMode(value).ToString()));
    }

    private void ReadSettings()
    {
        playersPerColumn = playersPerColumnSetting!.Value;
        rowWidth = rowWidthSetting!.Value;
        textScalePercent = textScaleSetting!.Value;
        showPlayerHeads = showPlayerHeadsSetting!.Value;
        showPing = showPingSetting!.Value;
        hideBots = hideBotsSetting!.Value;
        UpdateBotClassificationDemand();
        sortMode = ReadSortMode(sortModeSetting!.Value);
        displayMode = ReadDisplayMode(displayModeSetting!.Value);
    }

    private void SubscribeToSettings()
    {
        playersPerColumnSetting!.Subscribe(value =>
        {
            playersPerColumn = value;
            rosterDirty = true;
        });
        rowWidthSetting!.Subscribe(value => rowWidth = value);
        textScaleSetting!.Subscribe(value => textScalePercent = value);
        showPlayerHeadsSetting!.Subscribe(value => showPlayerHeads = value);
        showPingSetting!.Subscribe(value => showPing = value);
        hideBotsSetting!.Subscribe(OnHideBotsChanged);
        sortModeSetting!.Subscribe(value =>
        {
            sortMode = ReadSortMode(value);
            rosterDirty = true;
        });
        displayModeSetting!.Subscribe(value => displayMode = ReadDisplayMode(value));
    }

    private void RegisterPresentation(IPluginContext context)
    {
        context.Ui.RegisterSettingsPage(new PluginUiContribution("player-list", "Player List"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("players-per-column", "Players Per Column", 8f, 20f, 1f, playersPerColumnSetting!, value => ((int)Math.Round(value)).ToString()).InPage("player-list"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("row-width", "Row Width", 180f, 420f, 5f, rowWidthSetting!, value => ((int)Math.Round(value)).ToString()).InPage("player-list"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("ui-size", "UI Size", 80f, 160f, 5f, textScaleSetting!, value => ((int)Math.Round(value)).ToString() + "%").InPage("player-list"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("player-heads", "Show Player Icons", showPlayerHeadsSetting!).InPage("player-list"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("ping", "Show Ping", showPingSetting!).InPage("player-list"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("hide-bots", "Hide Suspected Bots", hideBotsSetting!).InPage("player-list"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Cycle("sort", "Sort", new[] { "Alphabetical", "Team", "Health" }, sortModeSetting!).InPage("player-list"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Cycle("display-mode", "Display Mode", new[] { "Hold", "Toggle" }, displayModeSetting!).InPage("player-list"));
        context.Ui.RegisterIconInteraction(new PluginIconInteractionDescriptor("sort", PluginIconHoverEffect.HighlightAndExpand, 1.12f, new PluginColor(190, 190, 190), new PluginColor(255, 255, 255), null, GetSortTooltip), CycleSortMode);
        context.Ui.RegisterIconInteraction(new PluginIconInteractionDescriptor("bot-filter", PluginIconHoverEffect.HighlightAndExpand, 1.12f, new PluginColor(190, 190, 190), new PluginColor(255, 255, 255), null, GetBotFilterTooltip), ToggleBotFiltering);
        context.Keybinds.Register(new PluginKeybindDescriptor("display-player-list", "T", "Display Player List", PluginKeybindActivation.Hold), HandleDisplayKeybind);
        context.Services.Publish<IPlayerListService>(this);
    }

    private void OnHideBotsChanged(bool value)
    {
        hideBots = value;
        UpdateBotClassificationDemand();

        if (hideBots && visible && !botClassificationInitialized)
        {
            botClassificationInitialized = true;
            RequestBotClassification();
        }

        rosterDirty = true;
    }

    private void ReleaseRuntimeServices()
    {
        hudRegistration?.Dispose();
        hudRegistration = null;
        hud = null;
        botClassificationDemand?.Dispose();
        botClassificationDemand = null;
        playerSnapshotDemand = null;
        players = null;
        session = null;
    }

    private void UpdateBotClassificationDemand()
    {
        if (hideBots)
        {
            if ((botClassificationDemand == null || botClassificationDemand.IsReleased) && playerSnapshotDemand != null)
            {
                botClassificationDemand = playerSnapshotDemand.RequestSuspectedBotClassification();
            }
        }
        else
        {
            botClassificationDemand?.Dispose();
            botClassificationDemand = null;
            waitingForBotClassification = false;
        }
    }

    private void RequestBotClassification()
    {
        if (hideBots && playerSnapshotDemand != null)
        {
            UpdateBotClassificationDemand();
            requestedBotClassificationVersion = playerSnapshotDemand.SuspectedBotClassificationVersion;
            observedBotClassificationVersion = requestedBotClassificationVersion;
            waitingForBotClassification = true;
            playerSnapshotDemand.RefreshSuspectedBotClassification();
            // Do not display an unclassified first roster and then make the user toggle twice.
            // The next update-thread capture publishes a newer version and releases this gate.
            ClearRoster();
        }
    }

    private void OnListShown()
    {
        if (hideBots)
        {
            if (botClassificationInitialized)
            {
                return;
            }
            botClassificationInitialized = true;
            RequestBotClassification();
        }
    }

    private void SetVisible(bool isVisible)
    {
        bool wasVisible = visible;
        visible = isVisible;
        UpdateHudRegistration();
        if (!wasVisible && isVisible)
        {
            OnListShown();
        }
    }

    /// <summary>The retained HUD host sees this widget only while the player list is visible.</summary>
    private void UpdateHudRegistration()
    {
        if (!visible)
        {
            hudRegistration?.Dispose();
            hudRegistration = null;
            return;
        }

        if ((hudRegistration == null || hudRegistration.IsReleased) && hud != null)
        {
            hudRegistration = hud.Register(new PluginHudWidgetDescriptor("player-list", 100), DrawHud);
        }
    }


    private void DrawHud(IPluginHudCanvas canvas, PluginHudFrame frame)
    {
        if (!visible || players == null || session == null)
        {
            return;
        }

        RefreshRoster();
        if (rows.Count == 0)
        {
            return;
        }

        int columns = columnStarts.Count;
        int rowsPerColumn = 0;
        for (int index = 0; index < columnCounts.Count; index++)
        {
            if (columnCounts[index] > rowsPerColumn)
            {
                rowsPerColumn = columnCounts[index];
            }
        }

        float uiScale = TextScale / PlayerListLayout.DefaultUiScale;
        PlayerListLayout layout = PlayerListLayout.Create(frame.ScreenWidth, frame.ScreenHeight, columns, rowsPerColumn, rowWidth, uiScale);
        canvas.DrawPanel(layout.PanelBounds, PanelColor);
        canvas.CapturePointer(layout.PanelBounds);
        PluginSessionPresentationSnapshot sessionSnapshot = session.GetCurrent();
        canvas.DrawText(sessionSnapshot.ServerName + " - " + rows.Count + "/" + sessionSnapshot.PlayerCapacity, layout.HeaderCenter.X, layout.HeaderCenter.Y, White, 0.82f * TextScale * 0.8f, 0.5f, 0f);
        canvas.DrawInteractiveNpcHead("bot-filter", CyborgNpcId, layout.BotToggleBounds);
        canvas.DrawInteractiveAsset("sort", "Images/UI/CharCreation/HairStyle_Arrow", layout.SortBounds);
        for (int column = 0; column < columns; column++)
        {
            int end = Math.Min(columnStarts[column] + columnCounts[column], rows.Count);
            for (int index = columnStarts[column]; index < end; index++)
            {
                PluginUiRect bounds = layout.GetRowBounds(column, index - columnStarts[column]);
                canvas.DrawPanel(bounds, (index - columnStarts[column]) % 2 == 0 ? LightRowColor : DarkRowColor);
                DrawRow(canvas, rows[index], bounds, uiScale);
            }
        }
        if (showPing)
        {
            DrawPing(canvas, layout.FooterCenter, TextScale, sessionSnapshot.PingMilliseconds);
        }
    }

    private void DrawRow(IPluginHudCanvas canvas, Row row, PluginUiRect bounds, float uiScale)
    {
        float nameX = bounds.X + (showPlayerHeads ? 44f * uiScale : 12f * uiScale) - 3f;
        float nameY = bounds.Y + bounds.Height / 2f - 8f * TextScale;
        float reservedRight = row.Player.IsDead && !row.Player.IsGhost ? 44f : 8f;
        const float playerNameScale = 0.703f; // 5% smaller than the established 0.74 player-row scale.
        string name = FitText(row.Name, bounds.Width - (nameX - bounds.X) - reservedRight, playerNameScale * TextScale);
        PluginOverlayColor nameColor = row.Player.IsDead
            ? new PluginOverlayColor(145, 145, 145)
            : TeamColor(row.Player.Team);
        canvas.DrawText(name, nameX, nameY, nameColor, playerNameScale * TextScale);

        if (row.Player.IsDead && !row.Player.IsGhost)
        {
            canvas.DrawText(FormatRespawn(row.Player.RespawnTimer), bounds.X + bounds.Width - 40f, nameY, new PluginOverlayColor(220, 220, 220), 0.62f * TextScale);
        }

        if (showPlayerHeads && (!row.Player.IsDead || row.Player.IsGhost))
        {
            canvas.DrawPlayerAvatar(row.Player.Id, bounds.X + 23f * uiScale - 3f, bounds.Y + bounds.Height / 2f - 4f * uiScale, uiScale);
        }
    }

    private void DrawPing(IPluginHudCanvas canvas, PluginHudPoint center, float scale, int? ping)
    {
        string text = ping.HasValue ? ping.Value + " ms" : "N/A";
        canvas.DrawText(text, center.X, center.Y - 8f, ping.HasValue ? PingColor(ping.Value) : new PluginOverlayColor(192, 192, 192), 0.68f * scale, 0.5f, 0f);
    }

    // Snapshot copying is shared and allocation-conscious; this plugin rebuilds its local order at a
    // bounded interval instead of sorting every HUD draw.
    private void RefreshRoster()
    {
        int now = Environment.TickCount;
        if (hideBots && playerSnapshotDemand != null)
        {
            long currentBotClassificationVersion = playerSnapshotDemand.SuspectedBotClassificationVersion;
            if (waitingForBotClassification && currentBotClassificationVersion <= requestedBotClassificationVersion)
            {
                rosterDirty = true;
                nextRosterRefreshTick = unchecked(now + 16);
                return;
            }
            if (waitingForBotClassification)
                waitingForBotClassification = false;
            if (currentBotClassificationVersion != observedBotClassificationVersion)
            {
                observedBotClassificationVersion = currentBotClassificationVersion;
                rosterDirty = true;
            }
        }
        int interval = sortMode == PlayerListSortMode.Health ? 200 : 500;
        if (!rosterDirty && unchecked(now - nextRosterRefreshTick) < 0)
        {
            return;
        }

        playerSnapshots.Clear();
        players!.CopyPlayers(playerSnapshots);
        rows.Clear();
        for (int index = 0; index < playerSnapshots.Count; index++)
        {
            PluginPlayerSnapshot player = playerSnapshots[index];
            string name = NormalizeName(player.Name);
            if (player.IsGhost && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (hideBots && player.IsSuspectedBot)
            {
                continue;
            }
            rows.Add(new Row(player, name));
        }
        rows.Sort(CompareRows);
        BuildColumns();
        nextRosterRefreshTick = unchecked(now + interval);
        rosterDirty = false;
    }

    private int CompareRows(Row left, Row right)
    {
        int ghost = left.Player.IsGhost.CompareTo(right.Player.IsGhost);
        if (ghost != 0)
        {
            return ghost;
        }

        if (sortMode == PlayerListSortMode.Team)
        {
            int team = TeamRank(left.Player.Team).CompareTo(TeamRank(right.Player.Team));
            if (team != 0)
            {
                return team;
            }
        }
        else if (sortMode == PlayerListSortMode.Health)
        {
            int life = right.Player.Life.CompareTo(left.Player.Life);
            if (life != 0)
            {
                return life;
            }
        }

        int name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        return name != 0
            ? name
            : left.Player.Id.CompareTo(right.Player.Id);
    }

    private void BuildColumns()
    {
        columnStarts.Clear();
        columnCounts.Clear();
        int index = 0;
        while (index < rows.Count)
        {
            int groupEnd = rows.Count;
            if (sortMode == PlayerListSortMode.Team)
            {
                int team = rows[index].Player.Team;
                groupEnd = index + 1;
                while (groupEnd < rows.Count && rows[groupEnd].Player.Team == team)
                {
                    groupEnd++;
                }
            }

            for (int start = index; start < groupEnd; start += playersPerColumn)
            {
                columnStarts.Add(start);
                columnCounts.Add(Math.Min(playersPerColumn, groupEnd - start));
            }
            index = groupEnd;
        }
    }

    private void ClearRoster()
    {
        playerSnapshots.Clear();
        rows.Clear();
        columnStarts.Clear();
        columnCounts.Clear();
        rosterDirty = true;
        nextRosterRefreshTick = 0;
    }

    private void HandleDisplayKeybind(bool isDown)
    {
        if (displayMode == PlayerListDisplayMode.Hold)
        {
            SetVisible(isDown);
        }
        else if (isDown)
        {
            SetVisible(!visible);
        }
    }

    private PluginTooltipOptions GetSortTooltip()
    {
        string name = sortMode switch
        {
            PlayerListSortMode.Team => "Teams",
            PlayerListSortMode.Health => "Health",
            _ => "Alphabetically"
        };
        return new PluginTooltipOptions("Sorted: " + name, PluginTooltipPlacement.Mouse, new PluginColor(255, 255, 255), 0.75f);
    }

    private PluginTooltipOptions GetBotFilterTooltip()
    {
        string visibility = hideBots ? "Hidden" : "Visible";
        return new PluginTooltipOptions("Bots: " + visibility, PluginTooltipPlacement.Mouse, new PluginColor(255, 255, 255), 0.75f);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static PlayerListSortMode ReadSortMode(string? value)
    {
        if (string.Equals(value, "Team", StringComparison.OrdinalIgnoreCase))
        {
            return PlayerListSortMode.Team;
        }

        return string.Equals(value, "Health", StringComparison.OrdinalIgnoreCase)
            ? PlayerListSortMode.Health
            : PlayerListSortMode.Alphabetical;
    }

    private static PlayerListDisplayMode ReadDisplayMode(string? value)
    {
        return string.Equals(value, "Toggle", StringComparison.OrdinalIgnoreCase)
            ? PlayerListDisplayMode.Toggle
            : PlayerListDisplayMode.Hold;
    }

    private static int TeamRank(int team)
    {
        return team >= 1 && team <= 5 ? team - 1 : 5;
    }

    private static string NormalizeName(string value)
    {
        string normalized = value ?? string.Empty;
        for (int pass = 0; pass < 4; pass++)
        {
            string next = TerrariaTagRegex.Replace(normalized, "$1");
            if (string.Equals(next, normalized, StringComparison.Ordinal))
            {
                break;
            }

            normalized = next;
        }

        return normalized.Trim();
    }

    private static string FitText(string text, float availableWidth, float scale)
    {
        if (string.IsNullOrEmpty(text) || availableWidth <= 0f)
        {
            return string.Empty;
        }

        int maxCharacters = Math.Max(1, (int)(availableWidth / Math.Max(1f, 7f * scale)));
        return text.Length <= maxCharacters
            ? text
            : text.Substring(0, Math.Max(0, maxCharacters - 3)) + "...";
    }

    private static PluginOverlayColor TeamColor(int team)
    {
        return team switch
        {
            1 => new PluginOverlayColor(255, 80, 80),
            2 => new PluginOverlayColor(80, 255, 80),
            3 => new PluginOverlayColor(80, 160, 255),
            4 => new PluginOverlayColor(255, 240, 80),
            5 => new PluginOverlayColor(255, 120, 255),
            _ => White
        };
    }

    private static PluginOverlayColor PingColor(int ping)
    {
        if (ping < 150)
        {
            return new PluginOverlayColor(120, 255, 120);
        }

        return ping <= 350
            ? new PluginOverlayColor(255, 230, 95)
            : new PluginOverlayColor(255, 95, 95);
    }

    private static string FormatRespawn(int ticks)
    {
        return ticks <= 0 ? "now" : ((ticks + 59) / 60) + "s";
    }

    private enum PlayerListDisplayMode
    {
        Hold,
        Toggle
    }

    private readonly struct Row
    {
        internal Row(PluginPlayerSnapshot player, string name)
        {
            Player = player;
            Name = name;
        }

        internal PluginPlayerSnapshot Player { get; }

        internal string Name { get; }
    }

    private readonly struct PluginHudPoint
    {
        internal PluginHudPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        internal float X { get; }

        internal float Y { get; }
    }
    private readonly struct PlayerListLayout
    {
        internal const float DefaultUiScale = 1.2f;

        private const int Padding = 14;
        private const int ColumnGap = 12;
        private const int HeaderHeight = 43;
        private const int RowHeight = 32;
        private const int FooterHeight = 22;
        private const int ControlSize = 28;

        private readonly int rowWidth;
        private readonly int padding;
        private readonly int headerHeight;
        private readonly int rowHeight;

        private PlayerListLayout(PluginUiRect panelBounds, int rowWidth, int padding, int headerHeight, int rowHeight, int footerHeight)
        {
            PanelBounds = panelBounds;
            this.rowWidth = rowWidth;
            this.padding = padding;
            this.headerHeight = headerHeight;
            this.rowHeight = rowHeight;
            HeaderCenter = new PluginHudPoint(panelBounds.X + panelBounds.Width / 2f, panelBounds.Y + padding + 2f);
            FooterCenter = new PluginHudPoint(panelBounds.X + panelBounds.Width / 2f, panelBounds.Y + panelBounds.Height - padding - footerHeight / 2f);
            SortBounds = new PluginUiRect(panelBounds.X + panelBounds.Width - padding - ControlSize + 4, panelBounds.Y + panelBounds.Height - padding - ControlSize + 5, ControlSize, ControlSize);
            BotToggleBounds = new PluginUiRect(SortBounds.X - ControlSize - 4, SortBounds.Y, ControlSize, ControlSize);
        }

        internal PluginUiRect PanelBounds { get; }

        internal PluginHudPoint HeaderCenter { get; }

        internal PluginHudPoint FooterCenter { get; }

        internal PluginUiRect BotToggleBounds { get; }

        internal PluginUiRect SortBounds { get; }

        internal static PlayerListLayout Create(int screenWidth, int screenHeight, int columns, int rowsPerColumn, int rowWidth, float uiScale)
        {
            int padding = Math.Max(10, (int)Math.Round(Padding * uiScale));
            int header = Math.Max(30, (int)Math.Round(HeaderHeight * uiScale));
            int row = Math.Max(24, (int)Math.Round(RowHeight * uiScale));
            int footer = Math.Max(20, (int)Math.Round(FooterHeight * uiScale));
            int width = padding * 2 + columns * rowWidth + Math.Max(0, columns - 1) * ColumnGap;
            int height = padding + header + rowsPerColumn * row + footer + padding;
            int x = Math.Max(padding, (screenWidth - width) / 2);
            int y = Math.Max(padding, (screenHeight - height) / 8);
            return new PlayerListLayout(new PluginUiRect(x, y, width, height), rowWidth, padding, header, row, footer);
        }

        internal PluginUiRect GetRowBounds(int column, int row)
        {
            return new PluginUiRect(
                PanelBounds.X + padding + column * (rowWidth + ColumnGap),
                PanelBounds.Y + padding + headerHeight + row * rowHeight,
                rowWidth,
                rowHeight - Math.Max(3, rowHeight / 8));
        }
    }
}
