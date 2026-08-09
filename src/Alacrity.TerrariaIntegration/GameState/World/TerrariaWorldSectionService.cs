using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria.GameState.World;

/// <summary>
/// Exposes a bounded, detached view of the local client's visible tile sections. It deliberately
/// never exposes Terraria's mutable section matrix or scans the whole world for a debug overlay.
/// </summary>
internal sealed class TerrariaWorldSectionService : IPluginWorldSectionService, IPluginRegistration
{
    private const int TilesPerSectionX = 200;
    private const int TilesPerSectionY = 150;
    private const int PixelsPerTile = 16;

    private bool released;

    private TerrariaWorldSectionService()
    {
    }

    internal static IPluginWorldSectionService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if ((manifest.Capabilities & PluginCapability.GameStateRead) == 0 || (manifest.Permissions & PluginPermission.ReadGameState) == 0)
        {
            return new DeniedService(manifest.Id);
        }

        var service = new TerrariaWorldSectionService();
        try
        {
            resources.Own("world-sections", PluginResourceKind.EventSubscription, service);
        }
        catch
        {
            service.Dispose();
            throw;
        }

        return service;
    }

    public string Name => "world-sections";

    public bool IsReleased => released;

    public void CopyVisibleSections(ICollection<PluginWorldSectionSnapshot> destination, int margin = 0)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (margin < 0) throw new ArgumentOutOfRangeException(nameof(margin));
        if (released) throw new ObjectDisposedException("IPluginWorldSectionService");
        if (Main.gameMenu || Main.maxSectionsX <= 0 || Main.maxSectionsY <= 0)
        {
            return;
        }

        if (Main.sectionManager == null)
        {
            return;
        }

        int sectionWidthPixels = TilesPerSectionX * PixelsPerTile;
        int sectionHeightPixels = TilesPerSectionY * PixelsPerTile;
        float zoom = Main.GameViewMatrix == null ? 1f : Math.Max(0.1f, Main.GameViewMatrix.Zoom.X);
        int startX = Math.Max(0, (int)Math.Floor(Main.screenPosition.X / sectionWidthPixels) - margin);
        int startY = Math.Max(0, (int)Math.Floor(Main.screenPosition.Y / sectionHeightPixels) - margin);
        int endX = Math.Min(Main.maxSectionsX - 1, (int)Math.Floor((Main.screenPosition.X + Main.screenWidth / zoom) / sectionWidthPixels) + margin);
        int endY = Math.Min(Main.maxSectionsY - 1, (int)Math.Floor((Main.screenPosition.Y + Main.screenHeight / zoom) / sectionHeightPixels) + margin);
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                bool loaded = Main.sectionManager.SectionLoaded(x, y);
                destination.Add(new PluginWorldSectionSnapshot(
                    x,
                    y,
                    x * sectionWidthPixels,
                    y * sectionHeightPixels,
                    sectionWidthPixels,
                    sectionHeightPixels,
                    loaded));
            }
        }
    }

    public void Dispose()
    {
        released = true;
    }

    private sealed class DeniedService : IPluginWorldSectionService
    {
        private readonly PluginId owner;

        internal DeniedService(PluginId owner)
        {
            this.owner = owner;
        }

        public void CopyVisibleSections(ICollection<PluginWorldSectionSnapshot> destination, int margin = 0)
        {
            throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare GameStateRead capability and ReadGameState permission before reading world section snapshots.");
        }
    }
}
