using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace AlacrityTerraria.Rendering.Rain;

/// <summary>
/// Version-locked instanced presentation for Terraria's existing rain draw loop. The permanent
/// patch keeps <see cref="Rain.Update"/> at its native position; this class only replaces the
/// active rain sprite submissions after every render resource is known to be available.
/// </summary>
internal static class TerrariaInstancedRainPresentation
{
    private const string EffectResourceName = "AlacrityTerraria.Rendering.Rain.InstancedRainPresentation.mgfxo";

    private static readonly VertexPositionTexture[] QuadVertices =
    {
        new VertexPositionTexture(new Vector3(0f, 0f, 0f), new Vector2(0f, 0f)),
        new VertexPositionTexture(new Vector3(1f, 0f, 0f), new Vector2(1f, 0f)),
        new VertexPositionTexture(new Vector3(1f, 1f, 0f), new Vector2(1f, 1f)),
        new VertexPositionTexture(new Vector3(0f, 1f, 0f), new Vector2(0f, 1f))
    };

    private static readonly short[] QuadIndices = { 0, 1, 2, 2, 3, 0 };
    private static readonly VertexDeclaration InstanceDeclaration = new VertexDeclaration(
        new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
        new VertexElement(48, VertexElementFormat.Color, VertexElementUsage.Color, 1));
    private static readonly object TextureGate = new object();

    private static GraphicsDevice graphicsDevice;
    private static VertexBuffer quadVertexBuffer;
    private static IndexBuffer quadIndexBuffer;
    private static DynamicVertexBuffer instanceBuffer;
    private static VertexBufferBinding[] vertexBindings;
    private static Effect effect;
    private static EffectParameter transformParameter;
    private static EffectParameter textureParameter;
    private static RainInstance[] instances;
    private static bool sessionActive;
    private static bool sessionUsesWorldTransform;
    private static Texture2D sessionRainTexture;
    private static int instanceCount;
    private static int rendererFaulted;
    private static Func<Texture2D> rainTextureGetter;
    private static int rainTextureResolutionAttempted;

    /// <summary>
    /// Starts a direct GPU pass only after every resource is ready. The patched caller chooses the
    /// exact native SpriteBatch state to restore: normal world drawing uses Main.Transform while
    /// capture drawing uses SpriteBatch's untransformed default state.
    /// </summary>
    internal static bool TryBegin(bool useWorldTransform)
    {
        if (sessionActive || rendererFaulted != 0)
        {
            return false;
        }

        // The effect intentionally follows SpriteBatch's linear rain sampling. The only native
        // world pass that uses point sampling is a render-target path; that path remains vanilla.
        if (useWorldTransform && !Main.drawToScreen)
        {
            return false;
        }

        if (!HasActiveRain())
        {
            return false;
        }

        try
        {
            Texture2D rainTexture = GetRainTexture();
            GraphicsDevice device = Main.graphics == null ? null : Main.graphics.GraphicsDevice;
            if (rainTexture == null || device == null || !EnsureResources(device, Main.maxRain))
            {
                return false;
            }

            Main.spriteBatch.End();
            sessionActive = true;
            sessionUsesWorldTransform = useWorldTransform;
            sessionRainTexture = rainTexture;
            instanceCount = 0;
            return true;
        }
        catch (Exception exception)
        {
            RecordRendererFailure(exception);
            return false;
        }
    }

    /// <summary>
    /// Captures one already-active native rain entry. This method is called from the original loop
    /// immediately before its native SpriteBatch.Draw call, so color and later Rain.Update timing
    /// exactly follow Terraria's draw/update sequence.
    /// </summary>
    internal static bool TryQueue(
        Texture2D rainTexture,
        Vector2 position,
        Rectangle? source,
        Color color,
        float rotation,
        Vector2 origin,
        float scale,
        SpriteEffects effects,
        float layerDepth)
    {
        if (!sessionActive)
        {
            return false;
        }

        try
        {
            if (rainTexture == null || !ReferenceEquals(rainTexture, sessionRainTexture) || !source.HasValue || instanceCount >= instances.Length)
            {
                AbortToNative();
                return false;
            }

            Rectangle sourceRectangle = source.Value;
            float sourceLeft = sourceRectangle.X / (float)rainTexture.Width;
            float sourceTop = sourceRectangle.Y / (float)rainTexture.Height;
            float sourceRight = (sourceRectangle.X + sourceRectangle.Width) / (float)rainTexture.Width;
            float sourceBottom = (sourceRectangle.Y + sourceRectangle.Height) / (float)rainTexture.Height;
            instances[instanceCount++] = new RainInstance(
                position.X,
                position.Y,
                rotation,
                scale,
                sourceLeft,
                sourceTop,
                sourceRight,
                sourceBottom,
                sourceRectangle.Width,
                sourceRectangle.Height,
                origin.X,
                origin.Y,
                color);
            return true;
        }
        catch (Exception exception)
        {
            RecordRendererFailure(exception);
            AbortToNative();
            return false;
        }
    }

    /// <summary>Flushes the compact active-rain buffer and restores the exact known native batch.</summary>
    internal static void End()
    {
        if (!sessionActive)
        {
            return;
        }

        bool useWorldTransform = sessionUsesWorldTransform;
        try
        {
            if (rendererFaulted == 0 && instanceCount != 0)
            {
                DrawInstances();
            }
        }
        catch (Exception exception)
        {
            RecordRendererFailure(exception);
        }
        finally
        {
            sessionActive = false;
            sessionUsesWorldTransform = false;
            sessionRainTexture = null;
            instanceCount = 0;
            RestoreNativeSpriteBatch(useWorldTransform);
        }
    }

    private static bool HasActiveRain()
    {
        for (int index = 0; index < Main.maxRain; index++)
        {
            Terraria.Rain rain = Main.rain[index];
            if (rain != null && rain.active)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Allocates optional GPU state before rain is drawn, at an existing main-thread update
    /// boundary.  A failed prewarm merely disables this presentation; native rain remains intact.
    /// </summary>
    internal static void Prewarm()
    {
        if (rendererFaulted != 0 || sessionActive || Main.gameMenu)
        {
            return;
        }

        try
        {
            GraphicsDevice device = Main.graphics == null ? null : Main.graphics.GraphicsDevice;
            Texture2D texture = GetRainTexture();
            if (device != null && texture != null)
            {
                EnsureResources(device, Main.maxRain);
            }
        }
        catch (Exception exception)
        {
            RecordRendererFailure(exception);
        }
    }

    private static void AbortToNative()
    {
        if (!sessionActive)
        {
            return;
        }

        bool useWorldTransform = sessionUsesWorldTransform;
        try
        {
            if (rendererFaulted == 0 && instanceCount != 0)
            {
                DrawInstances();
            }
        }
        catch (Exception exception)
        {
            RecordRendererFailure(exception);
        }
        finally
        {
            sessionActive = false;
            sessionUsesWorldTransform = false;
            sessionRainTexture = null;
            instanceCount = 0;
            RestoreNativeSpriteBatch(useWorldTransform);
        }
    }

    private static bool EnsureResources(GraphicsDevice device, int capacity)
    {
        if (capacity <= 0)
        {
            return false;
        }

        if (graphicsDevice != device || ResourcesDisposed() || instances == null || instances.Length < capacity)
        {
            DisposeResources();
            graphicsDevice = device;
            quadVertexBuffer = new VertexBuffer(device, VertexPositionTexture.VertexDeclaration, QuadVertices.Length, BufferUsage.None);
            quadVertexBuffer.SetData(QuadVertices);
            quadIndexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits, QuadIndices.Length, BufferUsage.None);
            quadIndexBuffer.SetData(QuadIndices);
            instanceBuffer = new DynamicVertexBuffer(device, InstanceDeclaration, capacity, BufferUsage.None);
            vertexBindings = new[]
            {
                new VertexBufferBinding(quadVertexBuffer, 0, 0),
                new VertexBufferBinding(instanceBuffer, 0, 1)
            };
            instances = new RainInstance[capacity];
            effect = new Effect(device, LoadEffectBytecode());
            transformParameter = effect.Parameters["transformMatrix"];
            textureParameter = effect.Parameters["rainTexture"];
            if (transformParameter == null || textureParameter == null)
            {
                throw new InvalidOperationException("The compiled instanced rain effect does not expose the expected parameters.");
            }
        }

        return true;
    }

    private static bool ResourcesDisposed()
    {
        return quadVertexBuffer == null ||
            quadIndexBuffer == null ||
            instanceBuffer == null ||
            effect == null ||
            quadVertexBuffer.IsDisposed ||
            quadIndexBuffer.IsDisposed ||
            instanceBuffer.IsDisposed ||
            effect.IsDisposed;
    }

    private static byte[] LoadEffectBytecode()
    {
        Assembly assembly = typeof(TerrariaInstancedRainPresentation).Assembly;
        using (Stream stream = assembly.GetManifestResourceStream(EffectResourceName))
        {
            if (stream == null)
            {
                throw new InvalidOperationException("The compiled instanced rain effect resource is missing.");
            }

            byte[] bytecode = new byte[stream.Length];
            int offset = 0;
            while (offset < bytecode.Length)
            {
                int read = stream.Read(bytecode, offset, bytecode.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("The compiled instanced rain effect resource ended unexpectedly.");
                }

                offset += read;
            }

            return bytecode;
        }
    }

    private static void DrawInstances()
    {
        Texture2D rainTexture = GetRainTexture();
        if (rainTexture == null)
        {
            return;
        }

        instanceBuffer.SetData(instances, 0, instanceCount, SetDataOptions.Discard);
        Matrix projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, 0f, 1f);
        transformParameter.SetValue(sessionUsesWorldTransform ? Main.Transform * projection : projection);
        textureParameter.SetValue(rainTexture);
        graphicsDevice.BlendState = BlendState.AlphaBlend;
        graphicsDevice.DepthStencilState = DepthStencilState.None;
        graphicsDevice.RasterizerState = sessionUsesWorldTransform ? Main.Rasterizer : RasterizerState.CullCounterClockwise;
        graphicsDevice.SamplerStates[0] = sessionUsesWorldTransform ? Main.DefaultSamplerState : SamplerState.LinearClamp;
        graphicsDevice.SetVertexBuffers(vertexBindings);
        graphicsDevice.Indices = quadIndexBuffer;
        effect.CurrentTechnique.Passes[0].Apply();
        graphicsDevice.DrawInstancedPrimitives(
            PrimitiveType.TriangleList,
            0,
            0,
            QuadVertices.Length,
            0,
            QuadIndices.Length / 3,
            instanceCount);
    }

    private static void RestoreNativeSpriteBatch(bool useWorldTransform)
    {
        try
        {
            if (useWorldTransform)
            {
                Main.spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    Main.DefaultSamplerState,
                    DepthStencilState.None,
                    Main.Rasterizer,
                    null,
                    Main.Transform);
            }
            else
            {
                Main.spriteBatch.Begin();
            }
        }
        catch (Exception exception)
        {
            RecordRendererFailure(exception);
        }
    }

    private static Texture2D GetRainTexture()
    {
        Func<Texture2D> getter = rainTextureGetter;
        if (getter != null)
        {
            return getter();
        }

        if (rainTextureResolutionAttempted != 0)
        {
            return null;
        }

        lock (TextureGate)
        {
            getter = rainTextureGetter;
            if (getter == null && rainTextureResolutionAttempted == 0)
            {
                try
                {
                    FieldInfo rainField = typeof(TextureAssets).GetField("Rain", BindingFlags.Public | BindingFlags.Static);
                    object asset = rainField == null ? null : rainField.GetValue(null);
                    MethodInfo valueGetter = asset == null
                        ? null
                        : asset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetMethod;
                    if (valueGetter != null && valueGetter.ReturnType == typeof(Texture2D))
                    {
                        getter = (Func<Texture2D>)Delegate.CreateDelegate(typeof(Func<Texture2D>), valueGetter);
                        rainTextureGetter = getter;
                    }
                }
                catch
                {
                    // The native draw loop remains authoritative when a stale asset surface cannot
                    // provide a typed texture getter.
                }
                finally
                {
                    rainTextureResolutionAttempted = 1;
                }
            }
        }

        return getter == null ? null : getter();
    }

    private static void RecordRendererFailure(Exception exception)
    {
        if (rendererFaulted != 0)
        {
            return;
        }

        rendererFaulted = 1;
        Debug.WriteLine("Alacrity disabled instanced rain presentation: " + exception.Message);
    }

    private static void DisposeResources()
    {
        quadVertexBuffer?.Dispose();
        quadIndexBuffer?.Dispose();
        instanceBuffer?.Dispose();
        effect?.Dispose();
        quadVertexBuffer = null;
        quadIndexBuffer = null;
        instanceBuffer = null;
        vertexBindings = null;
        effect = null;
        transformParameter = null;
        textureParameter = null;
        instances = null;
    }

    private readonly struct RainInstance
    {
        public readonly Vector4 PositionRotationScale;
        public readonly Vector4 TextureCoordinates;
        public readonly Vector4 SourceSizeAndOrigin;
        public readonly Color Color;

        internal RainInstance(
            float positionX,
            float positionY,
            float rotation,
            float scale,
            float sourceLeft,
            float sourceTop,
            float sourceRight,
            float sourceBottom,
            float sourceWidth,
            float sourceHeight,
            float originX,
            float originY,
            Color color)
        {
            PositionRotationScale = new Vector4(positionX, positionY, rotation, scale);
            TextureCoordinates = new Vector4(sourceLeft, sourceTop, sourceRight, sourceBottom);
            SourceSizeAndOrigin = new Vector4(sourceWidth, sourceHeight, originX, originY);
            Color = color;
        }
    }
}
