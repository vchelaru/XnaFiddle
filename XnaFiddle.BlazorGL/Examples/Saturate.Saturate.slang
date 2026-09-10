// Bloom-style highlight boost for SpriteBatch: lifts pixels above BloomThreshold, then saturates.
Texture2D SpriteTexture;
SamplerState TextureSampler;

float4 BloomThreshold; // per-channel cutoff; only what is above it gets boosted
float BloomIntensity;  // how hard the highlights are pushed
float BloomSaturation; // 0 = untouched color, higher = more vivid

[shader("fragment")]
float4 BloomPass(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(TextureSampler, uv);
    col = saturate(col - BloomThreshold) * BloomIntensity + col;
    col = saturate(col);
    col = lerp(col, col.rgba + col.rgba * BloomSaturation, BloomSaturation);
    return col;
}
