// Final bloom pass: blends the blurred glow over the original scene.
// SpriteBatch binds the blurred bloom texture to the first texture declared here; the scene is
// set from C#: effect.Parameters["BaseTexture"].
Texture2D SpriteTexture;
SamplerState BloomSampler;

Texture2D BaseTexture;
SamplerState BaseSampler;

float BloomIntensity;  // how strong the glow is
float BaseIntensity;   // how strong the underlying scene is
float BloomSaturation; // vividness of the glow; 1 = unchanged
float BaseSaturation;  // vividness of the scene; 1 = unchanged

// Push a color toward (saturation<1) or away from (>1) gray. 1 = unchanged.
float3 AdjustSaturation(float3 color, float saturation)
{
    float grey = dot(color, float3(0.3, 0.59, 0.11));
    return lerp(grey.xxx, color, saturation);
}

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float3 bloom = SpriteTexture.Sample(BloomSampler, uv).rgb;
    float3 scene = BaseTexture.Sample(BaseSampler, uv).rgb;

    bloom = AdjustSaturation(bloom, BloomSaturation) * BloomIntensity;
    scene = AdjustSaturation(scene, BaseSaturation) * BaseIntensity;

    // Darken the scene where the bloom is strong so bright glows don't wash out to white,
    // then add. With the intensities at 1 this is a screen-style blend that stays in
    // [0,1] and cannot clip -- which is what keeps the glow the right hue.
    scene *= (1.0 - saturate(bloom));

    return float4(scene + bloom, 1.0);
}
