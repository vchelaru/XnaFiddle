// Pixelation effect for SpriteBatch: samples on a coarse grid so the sprite reads as blocks.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float pixels = 128.0f;     // resolution to sample at
    float pixelation = 4.0f;   // block size

    float mx = uv.x * pixels;
    float my = uv.y * pixels;

    float x = round(mx / pixelation) * pixelation;
    float y = round(my / pixelation) * pixelation;
    float2 coord = float2(x / pixels, y / pixels);

    return SpriteTexture.Sample(SpriteTextureSampler, coord);
}
