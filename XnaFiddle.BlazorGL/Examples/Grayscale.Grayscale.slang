// Grayscale effect for SpriteBatch.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(SpriteTextureSampler, uv) * color;
    col.rgb = (col.r + col.g + col.b) / 3.0f;
    return col;
}
