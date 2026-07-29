using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace AlacrityTerraria
{
    /// <summary>Terraria-owned renderer for the Player List plugin's immutable local presentation state.</summary>
    internal static class PlayerListRuntime
    {
        private const int TombstoneItemId = 321;
        private const int BotMarkerItemId = 3015;
        private const int CyborgNpcId = 209;
        private static readonly Regex TerrariaTagRegex = new Regex("\\[[a-zA-Z]+(?:/[^:\\]]+)?[:]([^\\]]*)\\]", RegexOptions.Compiled);
        // Draw runs on Terraria's single UI thread. Reusing these bounded buffers avoids frame allocations
        // while Reset releases Player references whenever the list is no longer being presented.
        private static readonly List<Row> rowsBuffer = new List<Row>(256);
        private static readonly List<int> columnStartsBuffer = new List<int>(32);
        private static readonly List<int> columnCountsBuffer = new List<int>(32);

        private static bool rendererLookupAttempted;
        private static FieldInfo playerRendererField;
        private static FieldInfo cameraField;
        private static MethodInfo drawPlayerHeadMethod;
        private static MethodInfo cyborgHeadIndexMethod;
        private static FieldInfo npcHeadAssetsField;
        private static FieldInfo ghostTextureAssetsField;
        private static FieldInfo itemTextureAssetsField;
        private static PropertyInfo assetValueProperty;
        private static Texture2D sortTexture;
        private static Texture2D cyborgHeadTexture;
        private static Texture2D ghostTexture;
        private static Texture2D tombstoneTexture;

        internal static void Reset()
        {
            rowsBuffer.Clear();
            columnStartsBuffer.Clear();
            columnCountsBuffer.Clear();
        }

        internal static void Draw(SpriteBatch spriteBatch, IPlayerListService service)
        {
            if (spriteBatch == null || service == null)
                return;
            if (!service.IsVisible || Main.gameMenu || Main.drawingPlayerChat)
            {
                Reset();
                return;
            }

            List<Row> rows = BuildRows(service.HideBots);
            if (rows.Count == 0)
                return;

            Sort(rows, service.SortMode);
            int columns = BuildColumns(rows, service.PlayersPerColumn, service.SortMode, columnStartsBuffer, columnCountsBuffer);
            int rowsPerColumn = 0;
            for (int index = 0; index < columnCountsBuffer.Count; index++)
                rowsPerColumn = Math.Max(rowsPerColumn, columnCountsBuffer[index]);
            float uiScale = service.TextScale / PlayerListLayout.DefaultUiScale;
            PlayerListLayout layout = PlayerListLayout.Create(Main.screenWidth, Main.screenHeight, columns, rowsPerColumn, service.RowWidth, uiScale);

            Utils.DrawInvBG(spriteBatch, layout.PanelBounds, new Color(33, 15, 91, 230));
            string serverName = string.IsNullOrWhiteSpace(Main.worldName) ? "Server" : Main.worldName;
            DrawCentered(spriteBatch, serverName + " - " + rows.Count + "/" + Main.maxPlayers, layout.HeaderCenter, Color.White, 0.82f * service.TextScale * 0.8f);

            DrawBotToggleButton(spriteBatch, service, layout.BotToggleBounds);
            DrawSortButton(spriteBatch, service, layout.SortBounds);
            for (int column = 0; column < columns; column++)
            {
                int end = Math.Min(columnStartsBuffer[column] + columnCountsBuffer[column], rows.Count);
                for (int index = columnStartsBuffer[column]; index < end; index++)
                {
                    int rowIndex = index - columnStartsBuffer[column];
                    Rectangle rowBounds = layout.GetRowBounds(column, rowIndex);
                    Utils.DrawInvBG(spriteBatch, rowBounds, rowIndex % 2 == 0 ? new Color(48, 36, 112, 95) : new Color(21, 14, 64, 80));
                    DrawRowText(spriteBatch, service, rows[index], rowBounds);
                }
            }

            if (service.ShowPing)
                DrawPing(spriteBatch, layout.FooterCenter, service.TextScale);

            // Player-head rendering owns SpriteBatch state in Terraria. Draw it last so it cannot alter
            // the coordinate transform used by row backgrounds, names, or the footer.
            if (service.ShowPlayerHeads)
                DrawIcons(spriteBatch, rows, columns, columnStartsBuffer, columnCountsBuffer, layout, uiScale);
        }

        // A bad optional icon or renderer binding must not prevent the roster or ping from rendering.
        private static void DrawRowText(SpriteBatch spriteBatch, IPlayerListService service, Row row, Rectangle rowBounds)
        {
            try
            {
                float uiScale = service.TextScale / PlayerListLayout.DefaultUiScale;
                float nameX = rowBounds.X + (service.ShowPlayerHeads ? 44f * uiScale : 12f * uiScale);
                float textScale = service.TextScale;
                float nameY = rowBounds.Center.Y - 8f * textScale;
                float reservedRight = row.Dead && !row.Ghost ? 44f : 8f;
                string visibleName = FitText(row.Name, rowBounds.Right - nameX - reservedRight, 0.74f * textScale);
                DrawLeft(spriteBatch, visibleName, new Vector2(nameX, nameY), row.Dead ? new Color(145, 145, 145) : TeamColor(row.Team), 0.74f * textScale);
                if (row.Dead && !row.Ghost)
                    DrawLeft(spriteBatch, FormatRespawn(row.RespawnTimer), new Vector2(rowBounds.Right - 40f, nameY), new Color(220, 220, 220), 0.62f * textScale);
            }
            catch
            {
                // The next row and footer remain usable even if this player's cosmetic data is malformed.
            }
        }

        private static void DrawIcons(SpriteBatch spriteBatch, List<Row> rows, int columns, List<int> starts, List<int> counts, PlayerListLayout layout, float uiScale)
        {
            for (int column = 0; column < columns; column++)
            {
                int end = Math.Min(starts[column] + counts[column], rows.Count);
                for (int index = starts[column]; index < end; index++)
                {
                    Rectangle rowBounds = layout.GetRowBounds(column, index - starts[column]);
                    Vector2 position = new Vector2(rowBounds.X + 23f * uiScale, rowBounds.Center.Y - 4f * uiScale);
                    if (!TryDrawIcon(spriteBatch, rows[index], position, uiScale))
                        DrawCentered(spriteBatch, "*", position, new Color(255, 230, 140), 0.72f * uiScale);
                }
            }
        }

        private static List<Row> BuildRows(bool hideBots)
        {
            rowsBuffer.Clear();
            Player[] players = Main.player;
            if (players == null)
                return rowsBuffer;

            for (int slot = 0; slot < players.Length; slot++)
            {
                Player player = players[slot];
                if (player == null || (!player.active && !player.ghost))
                    continue;
                string normalizedName = NormalizeName(player.name);
                if (player.ghost && string.IsNullOrWhiteSpace(normalizedName))
                    continue;
                bool bot = IsLikelyBot(player, normalizedName);
                if (hideBots && bot)
                    continue;
                rowsBuffer.Add(new Row(player, normalizedName, player.team, player.statLife, player.dead, player.ghost, player.ghostFrame, player.respawnTimer));
            }
            return rowsBuffer;
        }

        private static bool IsLikelyBot(Player player, string normalizedName)
        {
            if (string.IsNullOrWhiteSpace(normalizedName))
                return true;
            int markerCount = CountItem(player.armor) + CountItem(player.miscEquips) + CountItem(player.dye) + CountItem(player.miscDyes);
            return markerCount >= 3;
        }

        private static int CountItem(Item[] items)
        {
            if (items == null)
                return 0;
            int count = 0;
            for (int index = 0; index < items.Length; index++)
            {
                if (items[index] != null && items[index].type == BotMarkerItemId)
                    count++;
            }
            return count;
        }

        private static void Sort(List<Row> rows, PlayerListSortMode mode)
        {
            rows.Sort((left, right) =>
            {
                int ghost = left.Ghost.CompareTo(right.Ghost);
                if (ghost != 0)
                    return ghost;
                if (mode == PlayerListSortMode.Team)
                {
                    int team = TeamRank(left.Team).CompareTo(TeamRank(right.Team));
                    if (team != 0)
                        return team;
                }
                else if (mode == PlayerListSortMode.Health)
                {
                    int health = right.Life.CompareTo(left.Life);
                    if (health != 0)
                        return health;
                }
                int name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                return name != 0 ? name : left.Player.whoAmI.CompareTo(right.Player.whoAmI);
            });
        }

        private static int BuildColumns(List<Row> rows, int perColumn, PlayerListSortMode mode, List<int> starts, List<int> counts)
        {
            starts.Clear();
            counts.Clear();
            int index = 0;
            while (index < rows.Count)
            {
                int groupEnd = rows.Count;
                if (mode == PlayerListSortMode.Team)
                {
                    int team = rows[index].Team;
                    groupEnd = index + 1;
                    while (groupEnd < rows.Count && rows[groupEnd].Team == team)
                        groupEnd++;
                }
                for (int start = index; start < groupEnd; start += perColumn)
                {
                    starts.Add(start);
                    counts.Add(Math.Min(perColumn, groupEnd - start));
                }
                index = groupEnd;
            }
            return starts.Count;
        }

        private static void DrawSortButton(SpriteBatch spriteBatch, IPlayerListService service, Rectangle bounds)
        {
            bool hover = bounds.Contains(Main.mouseX, Main.mouseY);
            if (sortTexture == null)
            {
                try { sortTexture = PluginUiRuntime.RequestApprovedTexture("Images/UI/CharCreation/HairStyle_Arrow"); }
                catch { }
            }
            if (sortTexture != null)
                spriteBatch.Draw(sortTexture, bounds, hover ? Color.White : new Color(190, 190, 190));
            else
                DrawCentered(spriteBatch, "S", new Vector2(bounds.Center.X, bounds.Center.Y), hover ? Color.White : new Color(190, 190, 190), 0.65f);
            if (!hover)
                return;
            Main.LocalPlayer.mouseInterface = true;
            Main.instance.MouseText("Sorted: " + service.SortMode);
            if (Main.mouseLeft && Main.mouseLeftRelease)
                service.CycleSortMode();
        }

        private static void DrawBotToggleButton(SpriteBatch spriteBatch, IPlayerListService service, Rectangle bounds)
        {
            bool hover = bounds.Contains(Main.mouseX, Main.mouseY);
            Color iconColor = hover ? Color.White : new Color(190, 190, 190);
            if (!TryDrawCyborgHead(spriteBatch, new Vector2(bounds.Center.X, bounds.Center.Y), 0.66f, iconColor))
                DrawCentered(spriteBatch, "B", new Vector2(bounds.Center.X, bounds.Center.Y), iconColor, 0.78f);
            if (!hover)
                return;
            Main.LocalPlayer.mouseInterface = true;
            Main.instance.MouseText(service.HideBots ? "Bots hidden" : "Bots listed");
            if (Main.mouseLeft && Main.mouseLeftRelease)
                service.ToggleBotFiltering();
        }

        private static void DrawPing(SpriteBatch spriteBatch, Vector2 center, float textScale)
        {
            int ping = PluginUiRuntime.GetCurrentPing();
            string value = ping + " ms";
            float scale = 0.68f * textScale;
            float x = center.X - GetTextWidth(value, scale) / 2f;
            DrawLeft(spriteBatch, value, new Vector2(x, center.Y - 8f), PingColor(ping), scale);
        }

        private static bool TryDrawIcon(SpriteBatch spriteBatch, Row row, Vector2 position, float uiScale)
        {
            if (row.Ghost)
            {
                Texture2D ghost = GetGhostTexture();
                if (ghost == null)
                    return false;
                int frameHeight = ghost.Height / 4;
                spriteBatch.Draw(ghost, position, new Rectangle(0, frameHeight * (row.GhostFrame % 4), ghost.Width, frameHeight), Color.White, 0f, new Vector2(ghost.Width / 2f, frameHeight / 2f), 0.42f * uiScale, SpriteEffects.None, 0f);
                return true;
            }
            if (row.Dead)
            {
                Texture2D tombstone = GetTombstoneTexture();
                if (tombstone == null)
                    return false;
                spriteBatch.Draw(tombstone, position, null, Color.White, 0f, new Vector2(tombstone.Width / 2f, tombstone.Height / 2f), 0.42f * uiScale, SpriteEffects.None, 0f);
                return true;
            }
            return TryDrawPlayerHead(row.Player, position + new Vector2(0f, -1f * uiScale), uiScale);
        }

        private static bool TryDrawCyborgHead(SpriteBatch spriteBatch, Vector2 position, float scale, Color color)
        {
            try
            {
                EnsureRendererLookup();
                if (cyborgHeadIndexMethod == null || npcHeadAssetsField == null)
                    return false;
                int index = (int)cyborgHeadIndexMethod.Invoke(null, new object[] { CyborgNpcId });
                Texture2D texture = cyborgHeadTexture ?? GetTextureFromAsset(npcHeadAssetsField.GetValue(null) as Array, index);
                if (texture == null)
                    return false;
                cyborgHeadTexture = texture;
                spriteBatch.Draw(texture, position, null, color, 0f, new Vector2(texture.Width / 2f, texture.Height / 2f), scale, SpriteEffects.None, 0f);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDrawPlayerHead(Player player, Vector2 position, float uiScale)
        {
            EnsureRendererLookup();
            if (playerRendererField == null || cameraField == null || drawPlayerHeadMethod == null)
                return false;

            try
            {
                object renderer = playerRendererField.GetValue(null);
                object camera = cameraField.GetValue(null);
                if (renderer == null || camera == null)
                    return false;
                // PlayerRenderer always creates a black outer pass when a border is supplied.
                // The Player List intentionally uses an unoutlined head instead of approximating it.
                drawPlayerHeadMethod.Invoke(renderer, new object[] { camera, player, position, 1f, 0.55f * 1.15f * 0.95f * 1.05f * uiScale, Color.Transparent });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureRendererLookup()
        {
            if (rendererLookupAttempted)
                return;
            rendererLookupAttempted = true;
            try
            {
                Type mainType = typeof(Main);
                playerRendererField = mainType.GetField("PlayerRenderer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                cameraField = mainType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object renderer = playerRendererField == null ? null : playerRendererField.GetValue(null);
                if (renderer != null)
                {
                    MethodInfo[] methods = renderer.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int index = 0; index < methods.Length; index++)
                    {
                        if (methods[index].Name == "DrawPlayerHead" && methods[index].GetParameters().Length == 6)
                        {
                            drawPlayerHeadMethod = methods[index];
                            break;
                        }
                    }
                }
                cyborgHeadIndexMethod = typeof(NPC).GetMethod("TypeToDefaultHeadIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(int) }, null);
                npcHeadAssetsField = typeof(TextureAssets).GetField("NpcHead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                ghostTextureAssetsField = typeof(TextureAssets).GetField("Ghost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                itemTextureAssetsField = typeof(TextureAssets).GetField("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }
            catch
            {
                playerRendererField = null;
                cameraField = null;
                drawPlayerHeadMethod = null;
                cyborgHeadIndexMethod = null;
                npcHeadAssetsField = null;
                ghostTextureAssetsField = null;
                itemTextureAssetsField = null;
            }
        }

        private static Texture2D GetGhostTexture()
        {
            if (ghostTexture != null)
                return ghostTexture;
            try
            {
                EnsureRendererLookup();
                ghostTexture = ghostTextureAssetsField == null ? null : GetTextureFromAsset(ghostTextureAssetsField.GetValue(null));
                return ghostTexture;
            }
            catch { return null; }
        }

        private static Texture2D GetTombstoneTexture()
        {
            if (tombstoneTexture != null)
                return tombstoneTexture;
            try
            {
                EnsureRendererLookup();
                tombstoneTexture = itemTextureAssetsField == null ? null : GetTextureFromAsset(itemTextureAssetsField.GetValue(null) as Array, TombstoneItemId);
                return tombstoneTexture;
            }
            catch { return null; }
        }

        private static Texture2D GetTextureFromAsset(Array assets, int index)
        {
            return assets == null || index < 0 || index >= assets.Length ? null : GetTextureFromAsset(assets.GetValue(index));
        }

        private static Texture2D GetTextureFromAsset(object asset)
        {
            if (asset == null)
                return null;
            assetValueProperty = assetValueProperty ?? asset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            return assetValueProperty == null ? null : assetValueProperty.GetValue(asset, null) as Texture2D;
        }

        private static int TeamRank(int team) => team >= 1 && team <= 5 ? team - 1 : 5;
        private static string NormalizeName(string value)
        {
            string normalized = value ?? string.Empty;
            for (int pass = 0; pass < 4; pass++)
            {
                string next = TerrariaTagRegex.Replace(normalized, "$1");
                if (string.Equals(next, normalized, StringComparison.Ordinal))
                    break;
                normalized = next;
            }
            return normalized.Trim();
        }
        private static Color TeamColor(int team)
        {
            switch (team)
            {
                case 1: return new Color(255, 80, 80);
                case 2: return new Color(80, 255, 80);
                case 3: return new Color(80, 160, 255);
                case 4: return new Color(255, 240, 80);
                case 5: return new Color(255, 120, 255);
                default: return Color.White;
            }
        }

        private static Color PingColor(int ping) => ping < 150 ? new Color(120, 255, 120) : ping <= 350 ? new Color(255, 230, 95) : new Color(255, 95, 95);
        private static string FormatRespawn(int ticks) => ticks <= 0 ? "now" : ((ticks + 59) / 60) + "s";
        private static void DrawCentered(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale) => Utils.DrawBorderString(spriteBatch, text, position, color, scale, 0.5f, 0f, -1);
        private static float GetTextWidth(string text, float scale) => text.Length * 11f * scale;
        private static string FitText(string text, float availableWidth, float scale)
        {
            if (string.IsNullOrEmpty(text) || GetTextWidth(text, scale) <= availableWidth)
                return text;
            const string ellipsis = "...";
            int maximum = Math.Max(0, (int)(availableWidth / (11f * scale)) - ellipsis.Length);
            return maximum == 0 ? string.Empty : text.Substring(0, Math.Min(maximum, text.Length)) + ellipsis;
        }
        private static void DrawLeft(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale) => Utils.DrawBorderString(spriteBatch, text, position, color, scale, 0f, 0f, -1);

        private readonly struct PlayerListLayout
        {
            internal const float DefaultUiScale = 1.2f;
            private const int Padding = 14;
            private const int ColumnGap = 12;
            private const int HeaderHeight = 43;
            private const int RowHeight = 32;
            private const int FooterHeight = 22;
            private const int ControlSize = 28;

            private readonly int padding;
            private readonly int headerHeight;
            private readonly int rowHeight;

            private PlayerListLayout(Rectangle panelBounds, int rowWidth, int padding, int headerHeight, int rowHeight, int footerHeight)
            {
                PanelBounds = panelBounds;
                this.rowWidth = rowWidth;
                this.padding = padding;
                this.headerHeight = headerHeight;
                this.rowHeight = rowHeight;
                HeaderCenter = new Vector2(panelBounds.Center.X, panelBounds.Y + padding + 2f);
                FooterCenter = new Vector2(panelBounds.Center.X, panelBounds.Bottom - padding - footerHeight / 2f);
                SortBounds = new Rectangle(panelBounds.Right - padding - ControlSize + 4, panelBounds.Bottom - padding - ControlSize + 5, ControlSize, ControlSize);
                BotToggleBounds = new Rectangle(SortBounds.X - ControlSize - 4, SortBounds.Y, ControlSize, ControlSize);
            }

            private readonly int rowWidth;
            internal Rectangle PanelBounds { get; }
            internal Vector2 HeaderCenter { get; }
            internal Vector2 FooterCenter { get; }
            internal Rectangle BotToggleBounds { get; }
            internal Rectangle SortBounds { get; }

            internal static PlayerListLayout Create(int screenWidth, int screenHeight, int columns, int rowsPerColumn, int rowWidth, float uiScale)
            {
                int scaledPadding = Math.Max(10, (int)Math.Round(Padding * uiScale));
                int scaledHeaderHeight = Math.Max(30, (int)Math.Round(HeaderHeight * uiScale));
                int scaledRowHeight = Math.Max(24, (int)Math.Round(RowHeight * uiScale));
                int scaledFooterHeight = Math.Max(20, (int)Math.Round(FooterHeight * uiScale));
                int width = scaledPadding * 2 + columns * rowWidth + Math.Max(0, columns - 1) * ColumnGap;
                int height = scaledPadding + scaledHeaderHeight + rowsPerColumn * scaledRowHeight + scaledFooterHeight + scaledPadding;
                int x = Math.Max(scaledPadding, (screenWidth - width) / 2);
                int y = Math.Max(scaledPadding, (screenHeight - height) / 8);
                return new PlayerListLayout(new Rectangle(x, y, width, height), rowWidth, scaledPadding, scaledHeaderHeight, scaledRowHeight, scaledFooterHeight);
            }

            internal Rectangle GetRowBounds(int column, int row)
            {
                int x = PanelBounds.X + padding + column * (rowWidth + ColumnGap);
                int y = PanelBounds.Y + padding + headerHeight + row * rowHeight;
                return new Rectangle(x, y, rowWidth, rowHeight - Math.Max(3, rowHeight / 8));
            }
        }

        private readonly struct Row
        {
            internal Row(Player player, string name, int team, int life, bool dead, bool ghost, int ghostFrame, int respawnTimer)
            {
                Player = player;
                Name = name;
                Team = team;
                Life = life;
                Dead = dead;
                Ghost = ghost;
                GhostFrame = ghostFrame;
                RespawnTimer = respawnTimer;
            }

            internal Player Player { get; }
            internal string Name { get; }
            internal int Team { get; }
            internal int Life { get; }
            internal bool Dead { get; }
            internal bool Ghost { get; }
            internal int GhostFrame { get; }
            internal int RespawnTimer { get; }
        }
    }
}
