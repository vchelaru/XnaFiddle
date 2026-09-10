// Tint effect for SpriteBatch: multiplies the sprite by TintColor.
float4 TintColor;

Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 pixelColor = SpriteTexture.Sample(SpriteTextureSampler, uv) * color;
    return pixelColor * TintColor;
}
