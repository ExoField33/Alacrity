using System;

namespace AlacrityTerraria.Rendering.Projection;

/// <summary>
/// Framework-free validation for the selected world-overlay draw phase. Zoom and gravity are
/// deliberately not projection inputs because Terraria has already applied the view transform.
/// </summary>
internal static class TerrariaWorldProjectionVerifier
{
    internal static bool TryVerify(TerrariaWorldProjectionState state, out string diagnostic)
    {
        if (!IsFinite(state.ScreenPositionX) || !IsFinite(state.ScreenPositionY))
        {
            diagnostic = "World-overlay projection received a non-finite camera position.";
            return false;
        }

        if (!IsFinite(state.ZoomX) || !IsFinite(state.ZoomY) || state.ZoomX <= 0f || state.ZoomY <= 0f)
        {
            diagnostic = "World-overlay projection received an invalid live zoom value.";
            return false;
        }

        if (!IsFinite(state.GravityDirection) || (state.GravityDirection != 1f && state.GravityDirection != -1f))
        {
            diagnostic = "World-overlay projection received an unsupported gravity direction.";
            return false;
        }

        // Translation must remain affine regardless of the active zoom/gravity presentation state.
        TerrariaWorldProjectionMath.Project(state.ScreenPositionX + 17f, state.ScreenPositionY - 23f,
            state.ScreenPositionX, state.ScreenPositionY, out float projectedX, out float projectedY);
        if (projectedX != 17f || projectedY != -23f)
        {
            diagnostic = "World-overlay projection no longer preserves the verified camera translation.";
            return false;
        }

        diagnostic = "World-overlay projection verified against live camera, zoom, and gravity state.";
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Detached live-view inputs captured solely to verify the active world-overlay hook contract.</summary>
internal readonly struct TerrariaWorldProjectionState
{
    internal TerrariaWorldProjectionState(float screenPositionX, float screenPositionY, float zoomX, float zoomY, float gravityDirection)
    {
        ScreenPositionX = screenPositionX;
        ScreenPositionY = screenPositionY;
        ZoomX = zoomX;
        ZoomY = zoomY;
        GravityDirection = gravityDirection;
    }

    internal float ScreenPositionX { get; }
    internal float ScreenPositionY { get; }
    internal float ZoomX { get; }
    internal float ZoomY { get; }
    internal float GravityDirection { get; }
}
