using AlacrityTerraria.Rendering.Culling;
using Xunit;

namespace AlacrityTerraria;

public sealed class RenderCullingBoundsTests
{
    [Fact]
    public void ReusesNormalizedBoundsUntilCameraPositionOrSizeChanges()
    {
        var bounds = new TerrariaRenderCullingBounds();
        Assert.True(bounds.Update(10.2f, 20.7f, 100f, 50f));
        Assert.False(bounds.Update(10.2f, 20.7f, 100f, 50f));
        Assert.True(bounds.IsVisible(110f, 30f, 1, 1, 0));
        Assert.False(bounds.IsVisible(112f, 30f, 1, 1, 0));
        Assert.True(bounds.Update(11.2f, 20.7f, 100f, 50f));
        Assert.True(bounds.IsVisible(112f, 30f, 1, 1, 0));
    }

    [Fact]
    public void VisibilityMarginPreservesExistingInclusiveBoundaryBehavior()
    {
        var bounds = new TerrariaRenderCullingBounds();
        bounds.Update(100f, 100f, 50f, 50f);

        Assert.True(bounds.IsVisible(49f, 100f, 1, 1, 50));
        Assert.False(bounds.IsVisible(48f, 100f, 1, 1, 50));
    }
}
