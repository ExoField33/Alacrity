using System;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AlacrityTerraria;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            using (var window = new Form())
            {
                window.CreateControl();
                using (GraphicsDevice firstDevice = CreateDevice(window.Handle))
                using (GraphicsDevice secondDevice = CreateDevice(window.Handle))
                using (var firstBatch = new SpriteBatch(firstDevice))
                using (var secondBatch = new SpriteBatch(secondDevice))
                using (var resources = new TerrariaOverlayGraphicsResources())
                {
                    resources.Prepare(firstBatch.GraphicsDevice);
                    Assert(resources.TryGetPixel(out Texture2D firstPixel), "The first SpriteBatch device must create an integration-owned pixel texture.");
                    resources.Prepare(secondBatch.GraphicsDevice);
                    Assert(firstPixel.IsDisposed, "Replacing the GraphicsDevice must dispose the texture owned by the prior device.");
                    Assert(resources.TryGetPixel(out Texture2D secondPixel) && !ReferenceEquals(firstPixel, secondPixel), "The replacement SpriteBatch device must receive a new pixel texture.");
                }
            }
            Console.WriteLine("Terraria graphics integration tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static GraphicsDevice CreateDevice(IntPtr handle)
    {
        var parameters = new PresentationParameters
        {
            BackBufferWidth = 1,
            BackBufferHeight = 1,
            BackBufferFormat = SurfaceFormat.Color,
            DepthStencilFormat = DepthFormat.None,
            DeviceWindowHandle = handle,
            IsFullScreen = false,
            PresentationInterval = PresentInterval.Immediate
        };
        return new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.Reach, parameters);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
