matrix transformMatrix;

texture rainTexture;

sampler2D RainSampler = sampler_state
{
    Texture = (rainTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;
};

struct VertexShaderInput
{
    float3 Position : POSITION;
    float2 VertexTexCoord : TEXCOORD0;
    float4 InstancePositionRotationScale : TEXCOORD1;
    float4 InstanceTexCoord : TEXCOORD2;
    float4 InstanceSourceSizeAndOrigin : TEXCOORD3;
    float4 InstanceColor : COLOR1;
};

struct VertexShaderOutput
{
    float4 Position : POSITION;
    float2 VertexTexCoord : TEXCOORD0;
    float4 InstanceTexCoord : TEXCOORD1;
    float4 InstanceColor : COLOR1;
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;
    float angle = input.InstancePositionRotationScale.z;
    float scale = input.InstancePositionRotationScale.w;
    float sine = sin(angle);
    float cosine = cos(angle);
    float2 localPosition =
        (input.Position.xy * input.InstanceSourceSizeAndOrigin.xy - input.InstanceSourceSizeAndOrigin.zw) * scale;
    float2 rotatedPosition = float2(
        localPosition.x * cosine - localPosition.y * sine,
        localPosition.x * sine + localPosition.y * cosine);
    float4 position = float4(
        rotatedPosition + input.InstancePositionRotationScale.xy,
        0.0f,
        1.0f);

    output.Position = mul(position, transformMatrix);
    output.VertexTexCoord = input.VertexTexCoord;
    output.InstanceTexCoord = input.InstanceTexCoord;
    output.InstanceColor = input.InstanceColor;
    return output;
}

float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
    float2 source = float2(
        lerp(input.InstanceTexCoord.x, input.InstanceTexCoord.z, input.VertexTexCoord.x),
        lerp(input.InstanceTexCoord.y, input.InstanceTexCoord.w, input.VertexTexCoord.y));
    return tex2D(RainSampler, source) * input.InstanceColor;
}

technique Technique1
{
    pass InstancedRainPresentation
    {
        VertexShader = compile vs_3_0 VertexShaderFunction();
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
};
