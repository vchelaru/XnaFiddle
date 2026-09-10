// Bright-pass for a bloom chain: keeps only what is brighter than Threshold, blacks out the rest.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

// Bright-pass cutoff in [0,1]. A pixel blooms only when its brightness (max channel)
// exceeds Threshold; the whole color is then scaled uniformly by how far above it is,
// so hue is preserved and dimmer pixels fade to black -- the later blur passes glow
// only around the bright shapes, not the whole frame. Lower = more of the scene blooms.
float Threshold;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 c = SpriteTexture.Sample(SpriteTextureSampler, uv);
    // Gate on overall brightness (the max channel) and scale the ORIGINAL color by the
    // same factor on every channel. Uniform scaling preserves hue, so an orange square
    // blooms orange and cyan blooms cyan.
    float brightness = max(c.r, max(c.g, c.b));
    float k = saturate((brightness - Threshold) / max(1.0 - Threshold, 0.0001));
    return float4(c.rgb * k, 1.0) * color;
}
