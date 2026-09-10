// Sepia effect for SpriteBatch.
Texture2D SpriteTexture;
SamplerState s0;

float3 _sepiaTone; // defaults to 1.2, 1.0, 0.8

[shader("fragment")]
float4 PixelShaderFunction(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 tex = SpriteTexture.Sample(s0, uv);

    // first we need to convert to greyscale
    float grayScale = dot(tex.rgb, float3(0.3, 0.59, 0.11));

    tex.rgb = grayScale * _sepiaTone;

    return tex;
}
