namespace AlacrityTerraria;

/// <summary>
/// Pure translation used by the verified world-overlay hook. The hook's SpriteBatch already has
/// the game's view transform, so callers must subtract only the camera origin and never apply
/// zoom or GameViewMatrix a second time.
/// </summary>
internal static class TerrariaWorldProjectionMath
{
    internal static void Project(
        float worldX,
        float worldY,
        float screenPositionX,
        float screenPositionY,
        out float screenX,
        out float screenY)
    {
        screenX = worldX - screenPositionX;
        screenY = worldY - screenPositionY;
    }
}
